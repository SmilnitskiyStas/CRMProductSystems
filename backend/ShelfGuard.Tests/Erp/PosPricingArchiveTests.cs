using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class PosPricingArchiveTests(ErpSchemaFixture fixture)
{
    private sealed class Access(WorkAccessScope scope) : IWorkAccessContext
    { public Task<WorkAccessScope?> GetAsync(CancellationToken ct = default) => Task.FromResult<WorkAccessScope?>(scope); }
    private sealed class RuntimeRole(string role) : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection c, ConnectionEndEventData e, CancellationToken ct = default)
        { await using var cmd = c.CreateCommand(); cmd.CommandText = "SET ROLE " + role; await cmd.ExecuteNonQueryAsync(ct); }
    }
    private DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(
        Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).AddInterceptors(new RuntimeRole(fixture.ReaderRole)).IgnoreManyServiceProvidersWarning().Options;
    private PosPricingHistoryStore Store(PosInventoryPostingPostgresTests.Scenario s, WorkAccessScope? scope = null) =>
        new(Options(), new Access(scope ?? s.Scope));
    private async Task<(PosInventoryPostingPostgresTests.Scenario S, Guid Sale)> Sell(bool fenced = true)
    {
        var s = await PosInventoryPostingPostgresTests.Seed(fixture, fenced: fenced);
        await using var db = fixture.Context();
        var result = await PosInventoryPostingPostgresTests.Service(db, s, enabled: fenced).CreateSaleAsync(s.S.Tenant, s.S.Actor, s.Request());
        Assert.Null(result.Error); return (s, result.Sale!.TransactionId);
    }

    [ErpPostgresFact]
    public async Task Commit_archives_original_context_and_history_never_reads_live_prices()
    {
        var (s, id) = await Sell(); var history = Store(s);
        var original = await history.SnapshotAsync(id, default);
        Assert.Equal("matched", original.Comparison.Outcome); Assert.Equal("pos-online", original.Channel);
        Assert.Equal(50, System.Text.Json.JsonDocument.Parse(original.Payload).RootElement.GetProperty("result").GetProperty("paid").GetDecimal());
        Assert.DoesNotContain("unitCost", original.Payload); Assert.DoesNotContain("barcode", original.Payload);
        await using (var db = fixture.Context())
        {
            var item = await db.Items.SingleAsync(i => i.Id == s.S.Item); item.PriceRetail = 750; item.VatRate = 7;
            await db.SaveChangesAsync();
        }
        var saved = await history.SnapshotAsync(id, default);
        Assert.Equal(original.ContentHash, saved.ContentHash); Assert.Equal(original.Payload, saved.Payload);
        var transport = System.Text.Json.JsonSerializer.Serialize(saved, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var response = System.Text.Json.JsonDocument.Parse(transport);
        Assert.Equal(saved.ContentHash, PosPricingCapture.Hash(response.RootElement.GetProperty("payload").GetString()!));
        await using var verify = fixture.Context();
        Assert.Equal(1, await verify.Set<PosPricingSnapshot>().CountAsync(a => a.TenantId == s.S.Tenant));
        Assert.Equal(1, await verify.DocumentPostings.CountAsync(a => a.TenantId == s.S.Tenant && verify.BusinessDocuments.Any(d => d.Id == a.DocumentId && d.SourceSystem == "pos.sale.v1")));
        Assert.Equal(1, await verify.OutboxEvents.CountAsync(a => a.TenantId == s.S.Tenant && a.DeliveryScope == "live"));
    }

    [ErpPostgresTheory]
    [InlineData("UPDATE pos_pricing_snapshots SET \"Payload\"=\"Payload\"")]
    [InlineData("DELETE FROM pos_pricing_snapshots")]
    [InlineData("TRUNCATE pos_pricing_snapshots")]
    public async Task Archive_is_immutable_even_when_runtime_role_has_table_write_privileges(string sql)
    {
        var (s, _) = await Sell();
        await using var db = new AppDbContext(Options()); await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{s.S.Tenant.ToString()},true)");
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal("P0001", error.SqlState);
    }

    private sealed class Fault(string kind) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            foreach (var a in data.Context!.ChangeTracker.Entries<PosPricingSnapshot>().Where(e => e.State == EntityState.Added).ToArray())
            {
                if (kind == "missing") { a.State = EntityState.Unchanged; continue; }
                var p = JsonNode.Parse(a.Entity.Payload)!;
                if (kind == "money") p["result"]!["paid"] = 51;
                if (kind == "segment") p["result"]!["segments"]![0]!["finalPrice"] = 51;
                if (kind == "input") p["lines"]![0]!["input"]!["retailPrice"] = 51;
                var json = p.ToJsonString() + (kind == "hash" ? " " : "");
                a.Property(x => x.Payload).CurrentValue = json;
                if (kind != "hash") a.Property(x => x.ContentHash).CurrentValue = PosPricingCapture.Hash(json);
            }
            return ValueTask.FromResult(result);
        }
    }

    [ErpPostgresTheory]
    [InlineData("missing")][InlineData("hash")][InlineData("money")][InlineData("segment")]
    public async Task Invalid_archive_rolls_back_sale_stock_ERP_ledger_and_outbox(string kind)
    {
        var s = await PosInventoryPostingPostgresTests.Seed(fixture);
        decimal before; int ledger, outbox;
        await using (var db = fixture.Context())
        {
            before = (await db.ProductStocks.SingleAsync(b => b.Id == s.S.Batch)).Quantity;
            ledger = await db.InventoryLedgerEntries.CountAsync(a => a.TenantId == s.S.Tenant);
            outbox = await db.OutboxEvents.CountAsync(a => a.TenantId == s.S.Tenant);
        }
        await using (var db = fixture.Context(new Fault(kind)))
        {
            var e = await Assert.ThrowsAnyAsync<Exception>(() => PosInventoryPostingPostgresTests.Service(db, s)
                .CreateSaleAsync(s.S.Tenant, s.S.Actor, s.Request()));
            var pg = e as PostgresException ?? e.InnerException as PostgresException;
            Assert.NotNull(pg); Assert.Equal("23514", pg.SqlState);
        }
        await using var verify = fixture.Context();
        Assert.False(await verify.PosTransactions.AnyAsync(t => t.TenantId == s.S.Tenant));
        Assert.False(await verify.Set<PosPricingSnapshot>().AnyAsync(t => t.TenantId == s.S.Tenant));
        Assert.Equal(ledger, await verify.InventoryLedgerEntries.CountAsync(t => t.TenantId == s.S.Tenant));
        Assert.Equal(outbox, await verify.OutboxEvents.CountAsync(t => t.TenantId == s.S.Tenant));
        Assert.Equal(before, (await verify.ProductStocks.SingleAsync(b => b.Id == s.S.Batch)).Quantity);
    }

    [ErpPostgresFact]
    public async Task Tenant_location_and_live_pricing_permission_are_enforced_for_reads()
    {
        var (s, id) = await Sell(); var other = await PosInventoryPostingPostgresTests.Seed(fixture);
        await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(other).SnapshotAsync(id, default));
        await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(s, s.Scope with { LocationIds = [] }).SnapshotAsync(id, default));
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s, s.Scope with { LocationIds = [] })
            .ChannelsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), s.S.Location, default));
        await using (var db = fixture.Context())
        { (await db.Users.SingleAsync(u => u.Id == s.S.Actor)).SetPermissions(new() { ["pricing.view"] = false }); await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).SnapshotAsync(id, default));
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).ChannelsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), null, default));
    }

    [ErpPostgresFact]
    public async Task Report_counts_committed_archives_and_marks_legacy_missing_context_explicitly()
    {
        var (s, _) = await Sell(fenced: false); Guid legacy;
        await using (var db = fixture.Context())
        {
            var row = new PosTransaction { TenantId = s.S.Tenant, StoreId = s.S.Location, ReceiptNumber = "legacy-" + Guid.NewGuid() };
            legacy = row.Id; db.Add(row); await db.SaveChangesAsync();
        }
        var history = Store(s);
        var report = await history.ChannelsAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), null, default);
        Assert.Equal(2, report.Sales); Assert.Equal(1, report.Archived); Assert.Equal(1, report.LegacyWithoutArchive);
        Assert.Equal(0, report.MissingRequiredArchive); Assert.Equal("pos-online", Assert.Single(report.Channels).Channel);
        Assert.Equal(0, (await history.ChannelsAsync(DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2), null, default)).Sales);
        await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => history.SnapshotAsync(legacy, default));
        await Assert.ThrowsAsync<ArgumentException>(() => history.ChannelsAsync(DateTimeOffset.UtcNow.AddDays(-32), DateTimeOffset.UtcNow, null, default));
    }

    [ErpPostgresFact]
    public async Task Migration_preserves_old_W4_source_snapshot_and_does_not_backfill()
    {
        await using var db = fixture.Context();
        Assert.Equal(fixture.LegacySaleSnapshot, await db.Database.SqlQuery<string>($"SELECT public.erp_w4_pos_snapshot({fixture.LegacySaleId})::text AS \"Value\"").SingleAsync());
        Assert.False((await db.PosTransactions.SingleAsync(t => t.Id == fixture.LegacySaleId)).PricingArchiveRequired);
        Assert.False(await db.Set<PosPricingSnapshot>().AnyAsync(a => a.TransactionId == fixture.LegacySaleId));
        Assert.Throws<NotSupportedException>(() => _ = new ShelfGuard.Infrastructure.Migrations.AddPosPricingArchive().DownOperations);
    }
    [ErpPostgresTheory]
    [InlineData("price")][InlineData("delete")][InlineData("append")]
    public async Task Audited_sale_lines_cannot_be_changed_or_extended_after_commit_without_ERP_fencing(string operation)
    {
        var (s, id) = await Sell(fenced: false);
        await using var db = fixture.Context();
        var sql = operation switch {
            "price" => $"UPDATE pos_transaction_items SET \"PriceFinal\"=1 WHERE \"TransactionId\"='{id}'",
            "delete" => $"DELETE FROM pos_transaction_items WHERE \"TransactionId\"='{id}'",
            _ => $"INSERT INTO pos_transaction_items (\"Id\",\"TransactionId\",\"ProductId\",\"Quantity\",\"PriceRetail\",\"PriceFinal\",\"DiscountAmount\") SELECT gen_random_uuid(),\"TransactionId\",\"ProductId\",1,1,1,0 FROM pos_transaction_items WHERE \"TransactionId\"='{id}' LIMIT 1"
        };
        var e = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.True(e.SqlState is "P0001" or "23514");
    }

    [ErpPostgresTheory]
    [InlineData(true)][InlineData(false)]
    public async Task Composite_keys_reject_a_foreign_tenant_store_or_sale(bool foreignStore)
    {
        var a = await PosInventoryPostingPostgresTests.Seed(fixture, fenced: false);
        var b = await PosInventoryPostingPostgresTests.Seed(fixture, fenced: false);
        await using var db = fixture.Context();
        var other = new PosTransaction { TenantId = b.S.Tenant, StoreId = b.S.Location, ReceiptNumber = Guid.NewGuid().ToString() };
        db.Add(other); await db.SaveChangesAsync();
        var sale = new PosTransaction { TenantId = a.S.Tenant, StoreId = foreignStore ? b.S.Location : a.S.Location,
            Id = foreignStore ? Guid.NewGuid() : other.Id, PricingArchiveRequired = true, ReceiptNumber = Guid.NewGuid().ToString() };
        var snapshot = new PosPricingCapture(DateTime.UtcNow, "pos-online", false, false).Complete(sale, [], 0, 0, null);
        if (foreignStore) db.Add(sale);
        db.Add(snapshot);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23503", Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [ErpPostgresFact]
    public async Task Diagnostic_pages_preserve_denominators_and_do_not_return_receipt_or_member_data()
    {
        var s = await PosInventoryPostingPostgresTests.Seed(fixture);
        for (var i = 0; i < 26; i++)
        {
            await using var db = fixture.Context();
            var result = await PosInventoryPostingPostgresTests.Service(db, s).CreateSaleAsync(s.S.Tenant, s.S.Actor, s.Request(.01m));
            Assert.Null(result.Error);
        }
        var history = Store(s); var from = DateTimeOffset.UtcNow.AddDays(-1); var until = DateTimeOffset.UtcNow.AddDays(1);
        var first = await history.DiagnosticsAsync(from, until, null, 1, default);
        var last = await history.DiagnosticsAsync(from, until, null, 2, default);
        Assert.Equal(26, first.ArchivedInPeriod); Assert.Equal(25, first.Evaluated); Assert.True(first.HasMore);
        Assert.Equal(1, last.Evaluated); Assert.False(last.HasMore);
        Assert.Equal(25, first.Outcomes.Sum(o => o.Count)); Assert.Equal(1, last.Outcomes.Sum(o => o.Count));
        Assert.All(first.Outcomes, o => Assert.Equal("matched", o.Outcome));
        Assert.Equal(0, (await history.DiagnosticsAsync(from.AddDays(5), until.AddDays(5), null, 1, default)).Evaluated);
        await Assert.ThrowsAsync<ArgumentException>(() => history.DiagnosticsAsync(from, until, null, 0, default));
        var json = System.Text.Json.JsonSerializer.Serialize(first);
        Assert.DoesNotContain(s.S.Actor.ToString(), json); Assert.DoesNotContain(s.Barcode, json);
        Assert.DoesNotContain("Payload", json);
    }

    [ErpPostgresFact]
    public async Task Diagnostic_limit_and_intentional_input_fault_are_not_reported_as_matches()
    {
        var large = await PosInventoryPostingPostgresTests.Seed(fixture, fenced: false);
        await using (var db = fixture.Context())
        {
            var request = large.Request() with { Items = Enumerable.Range(0, 101).Select(_ => new ShelfGuard.Application.Features.Pos.Dtos.SaleItemRequest(large.Barcode, .01m)).ToArray() };
            Assert.Null((await PosInventoryPostingPostgresTests.Service(db, large, enabled: false).CreateSaleAsync(large.S.Tenant, large.S.Actor, request)).Error);
        }
        var from = DateTimeOffset.UtcNow.AddDays(-1); var until = DateTimeOffset.UtcNow.AddDays(1);
        Assert.Equal("limit", Assert.Single((await Store(large).DiagnosticsAsync(from, until, null, 1, default)).Outcomes).Outcome);
        var mismatch = await PosInventoryPostingPostgresTests.Seed(fixture);
        await using (var db = fixture.Context(new Fault("input")))
            Assert.Null((await PosInventoryPostingPostgresTests.Service(db, mismatch).CreateSaleAsync(mismatch.S.Tenant, mismatch.S.Actor, mismatch.Request())).Error);
        Assert.Equal("mismatch", Assert.Single((await Store(mismatch).DiagnosticsAsync(from, until, null, 1, default)).Outcomes).Outcome);
    }

    [ErpPostgresFact]
    public async Task Diagnostics_enforce_live_pricing_deny_and_location_scope()
    {
        var (s, _) = await Sell(); var from = DateTimeOffset.UtcNow.AddDays(-1); var until = DateTimeOffset.UtcNow.AddDays(1);
        var hidden = await Store(s, s.Scope with { LocationIds = [] }).DiagnosticsAsync(from, until, null, 1, default);
        Assert.Equal(0, hidden.ArchivedInPeriod);
        await using (var db = fixture.Context())
        { (await db.Users.SingleAsync(u => u.Id == s.S.Actor)).SetPermissions(new() { ["pricing.view"] = false }); await db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => Store(s).DiagnosticsAsync(from, until, null, 1, default));
    }

}

