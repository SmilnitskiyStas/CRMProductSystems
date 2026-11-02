using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Contracts;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class ErpSchemaPostgresTests(ErpSchemaFixture fixture)
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [ErpPostgresFact]
    public async Task Migration_preserves_legacy_data_and_adds_eleven_RLS_protected_tables()
    {
        await using var db = fixture.Context();
        Assert.Equal(fixture.LegacyFingerprint, await fixture.FingerprintAsync(db));
        Assert.Equal(4.25m, (await db.ProductStocks.SingleAsync(s => s.Id == fixture.LegacyBatchId)).Quantity);
        var count = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM pg_class
            WHERE relnamespace='public'::regnamespace AND relname IN
              ('business_documents','document_lines','document_postings','document_links','document_approvals',
               'inventory_ledger_entries','inventory_balance_projections','inventory_reservations','inventory_reconciliations','outbox_events','inbox_deduplication')
              AND relrowsecurity AND relforcerowsecurity
            """).SingleAsync();
        Assert.Equal(11, count);
    }

    [ErpPostgresFact]
    public async Task Document_ledger_outbox_commit_together_without_changing_the_legacy_write_path()
    {
        var graph = await CommittedAsync();
        await using var db = fixture.Context();
        Assert.Equal("posted", (await db.BusinessDocuments.SingleAsync(d => d.Id == graph.Document.Id)).Status);
        Assert.Equal(1, await db.DocumentPostings.CountAsync(p => p.DocumentId == graph.Document.Id));
        Assert.Equal(5m, (await db.InventoryLedgerEntries.SingleAsync(e => e.PostingId == graph.Posting.Id)).QuantityDelta);
        var outbox = await db.OutboxEvents.SingleAsync(e => e.PostingId == graph.Posting.Id);
        Assert.Equal(graph.Outbox.Id, ErpEventContracts.FromOutbox(outbox).MessageId);
        Assert.Equal(5m, (await db.ProductStocks.SingleAsync(s => s.Id == graph.Seed.Batch)).Quantity);
    }

    [ErpPostgresTheory]
    [InlineData("outbox")]
    [InlineData("ledger")]
    [InlineData("finalize")]
    [InlineData("payload")]
    public async Task Incomplete_or_inconsistent_posting_rolls_back_the_entire_document_cohort(string missing)
    {
        var seed = await SeedAsync();
        await using (var db = fixture.Context())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await StageAsync(db, seed, omitOutbox: missing == "outbox", omitLedger: missing == "ledger",
                finalize: missing != "finalize", wrongPayload: missing == "payload");
            var error = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        await using var verify = fixture.Context();
        Assert.Equal(0, await verify.BusinessDocuments.CountAsync(d => d.TenantId == seed.Tenant));
        Assert.Equal(0, await verify.InventoryLedgerEntries.CountAsync(e => e.TenantId == seed.Tenant));
        Assert.Equal(0, await verify.OutboxEvents.CountAsync(e => e.TenantId == seed.Tenant));
        Assert.Equal(5m, (await verify.ProductStocks.SingleAsync(s => s.Id == seed.Batch)).Quantity);
    }

    [ErpPostgresTheory]
    [InlineData("document")]
    [InlineData("idempotency")]
    public async Task One_version_or_idempotency_key_cannot_create_two_postings(string duplicate)
    {
        var seed = await SeedAsync();
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var graph = await StageAsync(db, seed, finalize: false);
        var document = graph.Document;
        if (duplicate == "idempotency")
        {
            document = await DraftAsync(db, seed);
            await ApproveAsync(db, document);
        }
        db.DocumentPostings.Add(new DocumentPosting { TenantId = seed.Tenant, DocumentId = document.Id,
            IdempotencyKey = duplicate == "idempotency" ? graph.Posting.IdempotencyKey : Guid.NewGuid().ToString("N"), RequestHash = Hash });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, SqlState(error));
    }

    [ErpPostgresFact]
    public async Task Revisions_have_distinct_rows_and_duplicate_logical_versions_are_rejected()
    {
        var seed = await SeedAsync();
        await using var db = fixture.Context();
        var first = await DraftAsync(db, seed);
        var next = new BusinessDocument { TenantId = seed.Tenant, DocumentKey = first.DocumentKey, Version = 2, DocumentType = first.DocumentType };
        db.Add(next); await db.SaveChangesAsync();
        Assert.NotEqual(first.Id, next.Id);
        db.Add(new BusinessDocument { TenantId = seed.Tenant, DocumentKey = first.DocumentKey, Version = 2, DocumentType = first.DocumentType });
        Assert.Equal(PostgresErrorCodes.UniqueViolation, SqlState(await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())));
    }

    [ErpPostgresFact]
    public async Task Another_revision_cannot_be_posted_until_the_active_version_is_reversed()
    {
        var original = await CommittedAsync();
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => StageAsync(db, original.Seed,
            documentKey: original.Document.DocumentKey, version: 2));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, SqlState(error));
    }

    [ErpPostgresTheory]
    [InlineData("document_postings", "UPDATE")]
    [InlineData("document_postings", "DELETE")]
    [InlineData("document_links", "UPDATE")]
    [InlineData("document_approvals", "DELETE")]
    [InlineData("inventory_ledger_entries", "UPDATE")]
    [InlineData("inventory_ledger_entries", "DELETE")]
    [InlineData("inventory_reconciliations", "UPDATE")]
    [InlineData("inbox_deduplication", "DELETE")]
    [InlineData("outbox_events", "DELETE")]
    public async Task Raw_SQL_cannot_modify_or_delete_history(string table, string operation)
    {
        var graph = await CommittedAsync(extras: true);
        await using var db = fixture.Context();
        var sql = operation == "UPDATE" ? $"UPDATE {table} SET \"Id\"=\"Id\" WHERE \"TenantId\"={{0}}"
            : $"DELETE FROM {table} WHERE \"TenantId\"={{0}}";
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, graph.Seed.Tenant));
        Assert.Equal("P0001", error.SqlState);
    }

    [ErpPostgresFact]
    public async Task Posted_header_lines_and_late_ledger_appends_are_sealed()
    {
        var graph = await CommittedAsync();
        await using var db = fixture.Context();
        foreach (var sql in new[] { "UPDATE business_documents SET \"Number\"='changed' WHERE \"Id\"={0}",
                     "UPDATE document_lines SET \"Quantity\"=100 WHERE \"DocumentId\"={0}" })
            Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, graph.Document.Id))).SqlState);
        db.InventoryLedgerEntries.Add(Entry(graph.Seed, graph.Document, graph.Line, graph.Posting, number: 2));
        Assert.Equal("P0001", SqlState(await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())));
    }

    [ErpPostgresFact]
    public async Task Truncate_cannot_erase_inbox_identity_and_unbacked_posted_status_cannot_commit()
    {
        var seed = await SeedAsync();
        await using var db = fixture.Context();
        Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE inbox_deduplication"))).SqlState);
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.BusinessDocuments.Add(new BusinessDocument { TenantId = seed.Tenant, DocumentType = "goods_receipt", Status = "posted", PostedAt = DateTime.UtcNow, ContentHash = Hash });
        await db.SaveChangesAsync();
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync())).SqlState);
    }

    [ErpPostgresFact]
    public async Task Full_reversal_keeps_the_original_and_appends_exact_negative_entries_and_its_own_event()
    {
        var original = await CommittedAsync();
        await using (var db = fixture.Context())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await StageAsync(db, original.Seed, original: original);
            await transaction.CommitAsync();
        }
        await using var verify = fixture.Context();
        Assert.Equal("reversed", (await verify.BusinessDocuments.SingleAsync(d => d.Id == original.Document.Id)).Status);
        Assert.Equal(0m, await verify.InventoryLedgerEntries.Where(e => e.TenantId == original.Seed.Tenant).SumAsync(e => e.QuantityDelta));
        Assert.Equal(2, await verify.InventoryLedgerEntries.CountAsync(e => e.TenantId == original.Seed.Tenant));
        Assert.Equal(2, await verify.OutboxEvents.CountAsync(e => e.TenantId == original.Seed.Tenant));
        Assert.Equal(5m, (await verify.ProductStocks.SingleAsync(s => s.Id == original.Seed.Batch)).Quantity);
    }

    [ErpPostgresFact]
    public async Task Reversed_status_without_a_compensating_posting_is_rejected_at_commit()
    {
        var graph = await CommittedAsync();
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var document = await db.BusinessDocuments.SingleAsync(d => d.Id == graph.Document.Id);
        document.Status = "reversed"; await db.SaveChangesAsync();
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync())).SqlState);
    }

    [ErpPostgresFact]
    public async Task Partial_reversal_cannot_masquerade_as_full_compensation()
    {
        var original = await CommittedAsync();
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => StageAsync(db, original.Seed, original: original, badReversal: true));
        Assert.Equal(PostgresErrorCodes.CheckViolation, SqlState(error));
    }

    [ErpPostgresTheory]
    [InlineData("item")]
    [InlineData("location")]
    [InlineData("legal_entity")]
    [InlineData("batch")]
    [InlineData("line")]
    public async Task Worker_bypass_still_cannot_create_cross_tenant_references(string reference)
    {
        var own = await SeedAsync(); var foreign = await SeedAsync();
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL app.role='worker'");
        if (reference == "legal_entity")
            db.BusinessDocuments.Add(new BusinessDocument { TenantId = own.Tenant, DocumentType = "goods_receipt", LegalEntityId = foreign.Legal });
        else
        {
            var graph = await StageAsync(db, own, finalize: false);
            if (reference is "batch" or "line")
            {
                Guid line = graph.Line.Id;
                if (reference == "line")
                {
                    var foreignDoc = await DraftAsync(db, foreign);
                    var foreignLine = new DocumentLine { TenantId = foreign.Tenant, DocumentId = foreignDoc.Id, LineNumber = 1, ItemId = foreign.Item, Quantity = 1, ItemNameSnapshot = "Foreign" };
                    db.Add(foreignLine); await db.SaveChangesAsync(); line = foreignLine.Id;
                }
                db.InventoryLedgerEntries.Add(new InventoryLedgerEntry { TenantId = own.Tenant, DocumentId = graph.Document.Id,
                    DocumentLineId = line, PostingId = graph.Posting.Id, EntryNumber = 2, ItemId = own.Item,
                    LocationId = own.Location, BatchId = reference == "batch" ? foreign.Batch : own.Batch,
                    QuantityDelta = 1, Currency = "UAH", OccurredAt = DateTime.UtcNow });
            }
            else
            {
                var draft = await DraftAsync(db, own);
                db.DocumentLines.Add(new DocumentLine { TenantId = own.Tenant, DocumentId = draft.Id, LineNumber = 1,
                    ItemId = reference == "item" ? foreign.Item : own.Item, ToLocationId = reference == "location" ? foreign.Location : own.Location,
                    Quantity = 1, ItemNameSnapshot = "Scoped" });
            }
        }
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, SqlState(await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())));
    }

    [ErpPostgresFact]
    public async Task A_batch_cannot_be_relabelled_as_another_item_or_location_in_the_ledger()
    {
        var seed = await SeedAsync();
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var graph = await StageAsync(db, seed, finalize: false);
        // This same-tenant batch has another product; its primary UUID alone must not be sufficient.
        var secondItem = new Item { TenantId = seed.Tenant, Name = "Different item" };
        var batch = new ProductStock { TenantId = seed.Tenant, ProductId = secondItem.Id, StoreId = seed.Location,
            Quantity = 5, QuantityInitial = 5, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) };
        db.AddRange(secondItem, batch); await db.SaveChangesAsync();
        db.InventoryLedgerEntries.Add(new InventoryLedgerEntry { TenantId = seed.Tenant, DocumentId = graph.Document.Id,
            DocumentLineId = graph.Line.Id, PostingId = graph.Posting.Id, EntryNumber = 2, ItemId = seed.Item,
            LocationId = seed.Location, BatchId = batch.Id, QuantityDelta = 1, Currency = "UAH", OccurredAt = DateTime.UtcNow });
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, SqlState(await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())));
    }

    [ErpPostgresFact]
    public async Task Every_W2_table_is_fail_closed_and_tenant_scoped_with_worker_access()
    {
        var own = await CommittedAsync(extras: true); var foreign = await CommittedAsync(extras: true);
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var role = $"SET LOCAL ROLE {fixture.ReaderRole}"; await db.Database.ExecuteSqlRawAsync(role);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL app.role=''; SET LOCAL app.tenant_id=''");
        var tables = new ShelfGuard.Infrastructure.Migrations.AddErpParallelSchema().UpOperations
            .OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>().Select(t => t.Name).ToList();
        foreach (var table in tables)
        {
            var sql = $"SELECT \"TenantId\" AS \"Value\" FROM {table}";
            Assert.Empty(await db.Database.SqlQueryRaw<Guid>(sql).ToListAsync());
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{own.Seed.Tenant.ToString()},true)");
        foreach (var table in tables)
        {
            var sql = $"SELECT \"TenantId\" AS \"Value\" FROM {table}";
            var rows = await db.Database.SqlQueryRaw<Guid>(sql).ToListAsync();
            Assert.NotEmpty(rows); Assert.All(rows, id => Assert.Equal(own.Seed.Tenant, id));
        }
        await transaction.CreateSavepointAsync("foreign_write");
        var denied = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO inbox_deduplication (\"TenantId\",\"Consumer\",\"MessageId\",\"PayloadHash\") VALUES ({foreign.Seed.Tenant},'bad',{Guid.NewGuid()},{Hash})"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        await transaction.RollbackToSavepointAsync("foreign_write");
        await db.Database.ExecuteSqlRawAsync("SET LOCAL app.role='worker'");
        foreach (var table in tables)
        {
            var sql = $"SELECT \"TenantId\" AS \"Value\" FROM {table}";
            var rows = await db.Database.SqlQueryRaw<Guid>(sql).ToListAsync();
            Assert.Contains(own.Seed.Tenant, rows); Assert.Contains(foreign.Seed.Tenant, rows);
        }
    }

    [ErpPostgresFact]
    public async Task Outbox_claim_has_concurrency_control_and_cannot_change_the_envelope()
    {
        var graph = await CommittedAsync();
        await using var first = fixture.Context(); await using var second = fixture.Context();
        var a = await first.OutboxEvents.SingleAsync(e => e.Id == graph.Outbox.Id);
        var b = await second.OutboxEvents.SingleAsync(e => e.Id == graph.Outbox.Id);
        a.Status = "processing"; a.LockToken = Guid.NewGuid(); a.LockedUntil = DateTime.UtcNow.AddMinutes(1); a.Attempts++;
        b.Status = "processing"; b.LockToken = Guid.NewGuid(); b.LockedUntil = DateTime.UtcNow.AddMinutes(1); b.Attempts++;
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        a.Status = "published"; a.PublishedAt = DateTime.UtcNow; a.LockToken = null; a.LockedUntil = null;
        await first.SaveChangesAsync();
        Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => first.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE outbox_events SET \"Payload\"='{{}}'::jsonb WHERE \"Id\"={a.Id}"))).SqlState);
    }

    [ErpPostgresFact]
    public async Task Historical_backfill_cannot_enter_the_live_delivery_queue()
    {
        var seed = await SeedAsync();
        PostedGraph graph;
        await using (var db = fixture.Context())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            graph = await StageAsync(db, seed, historical: true);
            await transaction.CommitAsync();
        }
        await using var verify = fixture.Context();
        var row = await verify.OutboxEvents.SingleAsync(e => e.Id == graph.Outbox.Id);
        Assert.Equal("historical", row.DeliveryScope); Assert.Equal("suppressed", row.Status);
        Assert.Equal(0, await verify.OutboxEvents.CountAsync(e => e.TenantId == seed.Tenant && e.Status == "pending"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE outbox_events SET \"Status\"='pending' WHERE \"Id\"={row.Id}"))).SqlState);
        Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE outbox_events SET \"DeliveryScope\"='live', \"Status\"='pending' WHERE \"Id\"={row.Id}"))).SqlState);
    }

    [ErpPostgresFact]
    public async Task Inbox_key_is_per_tenant_and_consumer_and_cannot_be_reused()
    {
        var graph = await CommittedAsync();
        await using var db = fixture.Context();
        var first = new InboxDeduplication { TenantId = graph.Seed.Tenant, Consumer = "projection.v1", MessageId = graph.Outbox.Id, PayloadHash = Hash };
        db.Add(first); await db.SaveChangesAsync();
        db.Add(new InboxDeduplication { TenantId = first.TenantId, Consumer = first.Consumer, MessageId = first.MessageId, PayloadHash = new string('b', 64) });
        Assert.Equal(PostgresErrorCodes.UniqueViolation, SqlState(await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())));
        db.ChangeTracker.Clear();
        db.Add(new InboxDeduplication { TenantId = first.TenantId, Consumer = "analytics.v1", MessageId = first.MessageId, PayloadHash = Hash });
        await db.SaveChangesAsync();
    }

    [ErpPostgresTheory]
    [InlineData(-1, 0)]
    [InlineData(5, -1)]
    [InlineData(5, 6)]
    public async Task Invalid_projection_balances_are_rejected(int quantity, int reserved)
    {
        var seed = await SeedAsync();
        await using var db = fixture.Context();
        db.Add(new InventoryBalanceProjection { TenantId = seed.Tenant, ItemId = seed.Item, LocationId = seed.Location, BatchId = seed.Batch,
            Quantity = quantity, ReservedQuantity = reserved });
        Assert.Equal(PostgresErrorCodes.CheckViolation, SqlState(await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())));
    }

    [ErpPostgresFact]
    public async Task Reservation_and_reconciliation_preserve_physical_stock_and_unknown_history()
    {
        var graph = await CommittedAsync(extras: true);
        await using var db = fixture.Context();
        Assert.Equal(5m, (await db.ProductStocks.SingleAsync(s => s.Id == graph.Seed.Batch)).Quantity);
        Assert.Equal(1, await db.InventoryLedgerEntries.CountAsync(e => e.PostingId == graph.Posting.Id));
        var unknown = new InventoryReconciliation { TenantId = graph.Seed.Tenant, RunId = Guid.NewGuid(), ItemId = graph.Seed.Item,
            LocationId = graph.Seed.Location, BatchId = graph.Seed.Batch, LegacyQuantity = 5, LedgerQuantity = null, Status = "unverifiable", AsOf = DateTime.UtcNow };
        db.Add(unknown); await db.SaveChangesAsync();
        Assert.Null(unknown.Difference);
        var difference = new InventoryReconciliation { TenantId = graph.Seed.Tenant, RunId = Guid.NewGuid(), ItemId = graph.Seed.Item,
            LocationId = graph.Seed.Location, BatchId = graph.Seed.Batch, LegacyQuantity = 5, LedgerQuantity = 3, Status = "discrepancy", AsOf = DateTime.UtcNow };
        db.Add(difference); await db.SaveChangesAsync();
        Assert.Equal(2m, difference.Difference);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE inventory_balance_projections SET \"Quantity\"='NaN'::numeric WHERE \"TenantId\"={graph.Seed.Tenant}"))).SqlState);
    }

    private async Task<Seed> SeedAsync()
    {
        var tenant = Tenant.Create("Synthetic ERP acceptance", Guid.NewGuid().ToString("N"));
        var location = new Location { TenantId = tenant.Id, Name = "ERP location" };
        var zone = new LocationZone { LocationId = location.Id, Name = "ERP zone" };
        var item = new Item { TenantId = tenant.Id, Name = "ERP item" };
        var legal = new LegalEntity { TenantId = tenant.Id, LegalName = "Synthetic legal entity" };
        var batch = new ProductStock { TenantId = tenant.Id, ProductId = item.Id, StoreId = location.Id, ZoneId = zone.Id,
            Quantity = 5m, QuantityInitial = 5m, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) };
        await using var db = fixture.Context(); db.AddRange(tenant, location, zone, item, legal, batch); await db.SaveChangesAsync();
        return new(tenant.Id, item.Id, location.Id, zone.Id, batch.Id, legal.Id);
    }

    private static async Task<BusinessDocument> DraftAsync(AppDbContext db, Seed seed, PostedGraph? original = null,
        Guid? documentKey = null, int version = 1)
    {
        var document = new BusinessDocument { TenantId = seed.Tenant, DocumentType = original is null ? "opening_balance" : "stock_reversal",
            SourceSystem = "acceptance", SourceId = Guid.NewGuid().ToString("N"), LegalEntityId = seed.Legal,
            DocumentKey = documentKey ?? Guid.NewGuid(), Version = version,
            ContentHash = Hash, ReversesDocumentId = original?.Document.Id, OccurredAt = DateTime.UtcNow };
        db.Add(document); await db.SaveChangesAsync(); return document;
    }

    private static async Task ApproveAsync(AppDbContext db, BusinessDocument document)
    {
        document.Status = "submitted"; await db.SaveChangesAsync();
        document.Status = "approved"; await db.SaveChangesAsync();
    }

    private static InventoryLedgerEntry Entry(Seed seed, BusinessDocument document, DocumentLine line, DocumentPosting posting,
        PostedGraph? original = null, int number = 1, bool badReversal = false) => new()
    {
        TenantId = seed.Tenant, DocumentId = document.Id, DocumentLineId = line.Id, PostingId = posting.Id, EntryNumber = number,
        ItemId = seed.Item, LocationId = seed.Location, BatchId = seed.Batch, ZoneId = seed.Zone,
        QuantityDelta = original is null ? 5 : badReversal ? -4 : -5, QuantityBefore = original is null ? 0 : 5,
        QuantityAfter = original is null ? 5 : badReversal ? 1 : 0, UnitCost = 10, ValueDelta = original is null ? 50 : badReversal ? -40 : -50,
        Currency = "UAH", OccurredAt = DateTime.UtcNow, ReversesEntryId = original?.Entry.Id,
    };

    private static async Task<PostedGraph> StageAsync(AppDbContext db, Seed seed, bool omitOutbox = false, bool omitLedger = false,
        bool finalize = true, bool wrongPayload = false, PostedGraph? original = null, bool badReversal = false,
        Guid? documentKey = null, int version = 1, bool historical = false)
    {
        var document = await DraftAsync(db, seed, original, documentKey, version);
        var line = new DocumentLine { TenantId = seed.Tenant, DocumentId = document.Id, LineNumber = 1, ItemId = seed.Item,
            ToLocationId = seed.Location, Quantity = 5, ItemNameSnapshot = "Historical ERP item", VatRate = 7, UnitCost = 10 };
        db.Add(line); await db.SaveChangesAsync(); await ApproveAsync(db, document);
        var posting = new DocumentPosting { TenantId = seed.Tenant, DocumentId = document.Id, IdempotencyKey = Guid.NewGuid().ToString("N"), RequestHash = Hash, ReversesPostingId = original?.Posting.Id };
        db.Add(posting); await db.SaveChangesAsync();
        var entry = Entry(seed, document, line, posting, original, badReversal: badReversal);
        if (!omitLedger) { db.Add(entry); await db.SaveChangesAsync(); }
        var delta = new InventoryLedgerDeltaV1(entry.Id, entry.EntryNumber, entry.ItemId, entry.LocationId, entry.BatchId,
            wrongPayload ? 6 : entry.QuantityDelta, entry.UnitCost, wrongPayload ? 60 : entry.ValueDelta,
            entry.Currency, entry.ZoneId, entry.ReversesEntryId);
        var outbox = ErpEventContracts.ToOutbox(ErpEventContracts.ForPosting(seed.Tenant, Guid.NewGuid(), DateTimeOffset.UtcNow,
            new InventoryDocumentPostedV1(document.Id, document.DocumentKey, document.Version, posting.Id, [delta], original?.Posting.Id)), historical);
        if (!omitOutbox) { db.Add(outbox); await db.SaveChangesAsync(); }
        if (finalize)
        {
            document.Status = "posted"; document.PostedAt = DateTime.UtcNow; await db.SaveChangesAsync();
            if (original is not null)
            {
                var source = await db.BusinessDocuments.SingleAsync(d => d.Id == original.Document.Id);
                source.Status = "reversed"; await db.SaveChangesAsync();
            }
        }
        return new(seed, document, line, posting, entry, outbox);
    }

    private async Task<PostedGraph> CommittedAsync(bool extras = false)
    {
        var seed = await SeedAsync();
        await using var db = fixture.Context(); await using var transaction = await db.Database.BeginTransactionAsync();
        var graph = await StageAsync(db, seed);
        if (extras)
        {
            var other = await DraftAsync(db, seed);
            db.AddRange(new DocumentLink { TenantId = seed.Tenant, FromDocumentId = graph.Document.Id, ToDocumentId = other.Id, RelationType = "derived_from" },
                new DocumentApproval { TenantId = seed.Tenant, DocumentId = graph.Document.Id, ApproverId = Guid.NewGuid(), Decision = "approved", ContentHash = Hash },
                new InventoryBalanceProjection { TenantId = seed.Tenant, ItemId = seed.Item, LocationId = seed.Location, BatchId = seed.Batch, Quantity = 5, ReservedQuantity = 1, LastLedgerEntryId = graph.Entry.Id },
                new InventoryReservation { TenantId = seed.Tenant, DocumentId = graph.Document.Id, DocumentLineId = graph.Line.Id, ItemId = seed.Item,
                    LocationId = seed.Location, BatchId = seed.Batch, Quantity = 1, RemainingQuantity = 1, IdempotencyKey = Guid.NewGuid().ToString("N") },
                new InventoryReconciliation { TenantId = seed.Tenant, RunId = Guid.NewGuid(), ItemId = seed.Item, LocationId = seed.Location,
                    BatchId = seed.Batch, LegacyQuantity = 5, LedgerQuantity = 5, Status = "matched", AsOf = DateTime.UtcNow },
                new InboxDeduplication { TenantId = seed.Tenant, Consumer = "acceptance.v1", MessageId = graph.Outbox.Id, PayloadHash = Hash });
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync(); return graph;
    }

    private static string SqlState(Exception error) => Assert.IsType<PostgresException>(error.GetBaseException()).SqlState;
    private sealed record Seed(Guid Tenant, Guid Item, Guid Location, Guid Zone, Guid Batch, Guid Legal);
    private sealed record PostedGraph(Seed Seed, BusinessDocument Document, DocumentLine Line, DocumentPosting Posting, InventoryLedgerEntry Entry, OutboxEvent Outbox);
}
