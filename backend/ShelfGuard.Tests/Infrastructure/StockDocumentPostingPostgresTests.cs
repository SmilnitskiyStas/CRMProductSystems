using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Receipts.Dtos;
using ShelfGuard.Application.Features.Movements;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.Stock.Dtos;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using ShelfGuard.Application.Features.Transfers;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Migrations;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

public sealed class PostingPostgresTheoryAttribute : TheoryAttribute
{
    public PostingPostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHELFGUARD_POSTING_TEST_DB")))
            Skip = "Requires an explicit disposable erm_posting_test_* PostgreSQL database.";
    }
}

public sealed class PostingPostgresFactAttribute : FactAttribute
{
    public PostingPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHELFGUARD_POSTING_TEST_DB")))
            Skip = "Requires an explicit disposable erm_posting_test_* PostgreSQL database.";
    }
}

[CollectionDefinition("Stock document posting", DisableParallelization = true)]
public sealed class StockDocumentPostingCollection { }

/// <summary>
/// Tests real PostgreSQL transactions with two independent request scopes. Both operations reach
/// SaveChanges before either writes; the selected first writer commits before the stale writer.
/// This fixture never falls back to the application/development database or starts the API.
/// </summary>
[Collection("Stock document posting")]
public sealed class StockDocumentPostingPostgresTests : IAsyncLifetime
{
    private readonly string _connection = Environment.GetEnvironmentVariable("SHELFGUARD_POSTING_TEST_DB") ?? "";

    private AppDbContext Context(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestPostgres.DataSource(_connection))
            .AddInterceptors(interceptors)
            .IgnoreManyServiceProvidersWarning().Options);

    public async Task InitializeAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(_connection);
        if (connection.Database?.StartsWith("erm_posting_test_", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Posting tests require a disposable erm_posting_test_* database.");
        await using var db = Context();
        // Production migrations install this extension before the catalog's trigram indexes.
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm");
        await db.Database.EnsureCreatedAsync();
        // EnsureCreated does not execute the custom collation migration used by real repositories.
        var collation = new AddUkrainianNameCollation();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(collation.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [PostingPostgresTheory]
    [InlineData("receipt", "post", "post")]
    [InlineData("receipt", "post", "cancel")]
    [InlineData("receipt", "cancel", "post")]
    [InlineData("receipt", "cancel", "cancel")]
    [InlineData("transfer", "post", "post")]
    [InlineData("transfer", "post", "cancel")]
    [InlineData("transfer", "cancel", "post")]
    [InlineData("transfer", "cancel", "cancel")]
    public async Task Concurrent_transitions_commit_one_complete_result_and_discard_the_loser(
        string kind, string firstAction, string secondAction)
    {
        var seed = await SeedAsync(kind);
        var gate = new SaveGate();
        await using var first = Context(gate.Interceptor(first: true));
        await using var second = Context(gate.Interceptor(first: false));

        var outcomes = await Task.WhenAll(
            AttemptAsync(first, () => ExecuteAsync(first, seed, kind, firstAction), gate),
            AttemptAsync(second, () => ExecuteAsync(second, seed, kind, secondAction)));
        Assert.Equal(new[] { true, false }, outcomes);

        // A later save on the losing request cannot resurrect its staged inserts or updates.
        Assert.Empty(second.ChangeTracker.Entries());
        await second.SaveChangesAsync();
        await AssertResultAsync(seed, kind, posted: firstAction == "post");

        // A fresh retry sees the terminal state and has no additional inventory/outbox effect.
        await using var retry = Context();
        Assert.NotNull(await ExecuteAsync(retry, seed, kind, "post"));
        await AssertResultAsync(seed, kind, posted: firstAction == "post");
    }

    [PostingPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Receipt_edit_and_post_use_the_same_version_even_when_status_remains_draft(bool postFirst)
    {
        var seed = await SeedAsync("receipt");
        var gate = new SaveGate();
        await using var first = Context(gate.Interceptor(first: true));
        await using var second = Context(gate.Interceptor(first: false));
        Task<string?> Edit(AppDbContext db) => EditReceiptAsync(db, seed);
        var outcomes = await Task.WhenAll(
            AttemptAsync(first, () => postFirst ? ExecuteAsync(first, seed, "receipt", "post") : Edit(first), gate),
            AttemptAsync(second, () => postFirst ? Edit(second) : ExecuteAsync(second, seed, "receipt", "post")));
        Assert.Equal(new[] { true, false }, outcomes);

        if (!postFirst)
        {
            await using var retry = Context();
            Assert.Null(await ExecuteAsync(retry, seed, "receipt", "post"));
        }
        await using var verify = Context();
        Assert.Equal(postFirst ? 5m : 7m,
            await verify.ProductStocks.Where(s => s.SourceId == seed.DocumentId).SumAsync(s => s.Quantity));
        Assert.Equal(postFirst ? (decimal?)null : 7m,
            (await verify.StockReceiptItems.SingleAsync(i => i.Id == seed.LineId)).QuantityReceived);
        Assert.Single(await verify.NotificationQueues.Where(n => n.TenantId == seed.TenantId).ToListAsync());
    }

    [PostingPostgresTheory]
    [InlineData("receipt")]
    [InlineData("transfer")]
    public async Task Failed_final_write_rolls_back_document_batches_movements_and_notification(string kind)
    {
        var seed = await SeedAsync(kind);
        // Fail inside PostgreSQL's save transaction, rather than before SaveChanges.
        await using (var setup = Context())
        {
            var table = kind == "receipt" ? "stock_receipts" : "stock_transfers";
            var suffix = seed.DocumentId.ToString("N");
            // The identifiers and UUID below are generated exclusively by this fixture.
            var sql = $"""
                CREATE FUNCTION erm_fail_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'synthetic posting failure'; END; $$;
                CREATE TRIGGER erm_fail_{suffix} BEFORE UPDATE ON {table}
                FOR EACH ROW WHEN (NEW."Id" = '{seed.DocumentId}'::uuid)
                EXECUTE FUNCTION erm_fail_{suffix}();
                """;
            await setup.Database.ExecuteSqlRawAsync(sql);
        }
        await using var db = Context();
        await Assert.ThrowsAsync<DbUpdateException>(() => ExecuteAsync(db, seed, kind, "post"));
        await using var verify = Context();
        var status = kind == "receipt"
            ? (await verify.StockReceipts.SingleAsync(r => r.Id == seed.DocumentId)).Status
            : (await verify.StockTransfers.SingleAsync(t => t.Id == seed.DocumentId)).Status;
        Assert.Equal(kind == "receipt" ? "draft" : "in_transit", status);
        Assert.Empty(await verify.ProductStocks.Where(s => s.SourceId == seed.DocumentId).ToListAsync());
        Assert.Equal(kind == "receipt" ? 0 : 1,
            await verify.StockMovements.CountAsync(m => m.ReferenceId == seed.DocumentId));
        Assert.Empty(await verify.NotificationQueues.Where(n => n.TenantId == seed.TenantId).ToListAsync());
    }

    [PostingPostgresTheory]
    [InlineData("receipt")]
    [InlineData("transfer")]
    public async Task Tenant_RLS_keeps_foreign_documents_unavailable_to_posting_and_cancellation(string kind)
    {
        var own = await SeedAsync(kind);
        var foreign = await SeedAsync(kind);
        var role = "erm_posting_app_" + Guid.NewGuid().ToString("N");
        var table = kind == "receipt" ? "stock_receipts" : "stock_transfers";
        await using (var setup = Context())
        {
            // All identifiers are generated here or selected from the two literal table names.
            var sql = $"""
                CREATE ROLE {role} NOSUPERUSER NOBYPASSRLS NOLOGIN;
                GRANT USAGE ON SCHEMA public TO {role};
                GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO {role};
                ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS erm_posting_tenant ON {table};
                CREATE POLICY erm_posting_tenant ON {table}
                USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """;
            await setup.Database.ExecuteSqlRawAsync(sql);
        }
        await using (var request = Context())
        {
            await request.Database.OpenConnectionAsync();
            var sql = $"SET ROLE {role}";
            await request.Database.ExecuteSqlRawAsync(sql);
            await request.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {own.TenantId.ToString()}, false)");
            foreach (var action in new[] { "post", "cancel" })
                Assert.Equal(kind == "receipt" ? "Receipt not found." : "Transfer not found.",
                    await ExecuteAsync(request, foreign, kind, action));
            Assert.Null(await ExecuteAsync(request, own, kind, "post"));
        }
        await AssertResultAsync(own, kind, posted: true);
        await using var verify = Context();
        Assert.Empty(await verify.ProductStocks.Where(s => s.SourceId == foreign.DocumentId).ToListAsync());
        Assert.Empty(await verify.NotificationQueues.Where(n => n.TenantId == foreign.TenantId).ToListAsync());
        var foreignStatus = kind == "receipt"
            ? (await verify.StockReceipts.SingleAsync(r => r.Id == foreign.DocumentId)).Status
            : (await verify.StockTransfers.SingleAsync(t => t.Id == foreign.DocumentId)).Status;
        Assert.Equal(kind == "receipt" ? "draft" : "in_transit", foreignStatus);
    }

    [PostingPostgresTheory]
    [InlineData(8)]
    [InlineData(2)]
    public async Task Manual_adjustment_is_recorded_once_and_reconciles_with_stock_history(int quantity)
    {
        var seed = await SeedAsync("receipt");
        var batchId = await ReceiveBatchAsync(seed);
        await using (var request = Context())
            Assert.Null(await AdjustAsync(request, seed, batchId, quantity));
        await using (var repeat = Context())
            Assert.Null(await AdjustAsync(repeat, seed, batchId, quantity));

        await using var verify = Context();
        var batch = await verify.ProductStocks.SingleAsync(s => s.Id == batchId);
        Assert.Equal(quantity, batch.Quantity);
        Assert.Equal(5m, batch.QuantityInitial);
        var movement = Assert.Single(await verify.StockMovements.Where(m => m.ProductStockId == batchId
            && m.ReferenceType == "stock_adjustment").ToListAsync());
        Assert.Equal(5m, movement.QuantityBefore);
        Assert.Equal(quantity, movement.QuantityAfter);
        Assert.Equal(Math.Abs(quantity - 5), movement.Quantity);
        Assert.Equal(seed.UserId, movement.PerformedBy);
        Assert.Equal(seed.TenantId, movement.TenantId);
        Assert.Equal(batchId, movement.ReferenceId);
        Assert.Equal(quantity > 5 ? batch.StoreId : (Guid?)null, movement.ToStoreId);
        Assert.Equal(quantity < 5 ? batch.StoreId : (Guid?)null, movement.FromStoreId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var history = await new MovementRepository(verify).GetStockHistoryAsync(seed.TenantId, seed.ProductId, null, today);
        Assert.Equal(quantity, history.CurrentStock);
        Assert.Equal(quantity, history.DailyChanges[today]);
    }

    [PostingPostgresFact]
    public async Task Concurrent_manual_adjustments_keep_one_delta_and_discard_losing_staged_rows()
    {
        var seed = await SeedAsync("receipt");
        var batchId = await ReceiveBatchAsync(seed);
        var gate = new SaveGate();
        await using var first = Context(gate.Interceptor(first: true));
        await using var second = Context(gate.Interceptor(first: false));
        Assert.Equal(new[] { true, false }, await Task.WhenAll(
            AttemptAsync(first, () => AdjustAsync(first, seed, batchId, 8), gate),
            AttemptAsync(second, () => AdjustAsync(second, seed, batchId, 9))));
        Assert.Empty(second.ChangeTracker.Entries());
        await second.SaveChangesAsync();
        await using var verify = Context();
        Assert.Equal(8m, (await verify.ProductStocks.SingleAsync(s => s.Id == batchId)).Quantity);
        var movement = Assert.Single(await verify.StockMovements.Where(m => m.ProductStockId == batchId
            && m.ReferenceType == "stock_adjustment").ToListAsync());
        Assert.Equal(5m, movement.QuantityBefore);
        Assert.Equal(8m, movement.QuantityAfter);
    }

    [PostingPostgresTheory]
    [InlineData("adjustment")]
    [InlineData("transfer_cancel")]
    public async Task Failed_movement_insert_rolls_back_its_stock_change_and_transfer_state(string operation)
    {
        var seed = await SeedAsync(operation == "adjustment" ? "receipt" : "transfer");
        var batchId = operation == "adjustment" ? await ReceiveBatchAsync(seed) : seed.SourceStockId;
        var referenceId = operation == "adjustment" ? batchId : seed.DocumentId;
        var referenceType = operation == "adjustment" ? "stock_adjustment" : "stock_transfer_cancellation";
        await using (var setup = Context())
        {
            // Generated UUID identifiers and the two literal reference types only.
            var suffix = Guid.NewGuid().ToString("N");
            var sql = $"""
                CREATE FUNCTION erm_reject_movement_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'synthetic movement failure'; END; $$;
                CREATE TRIGGER erm_reject_movement_{suffix} BEFORE INSERT ON stock_movements
                FOR EACH ROW WHEN (NEW."ReferenceId" = '{referenceId}'::uuid AND NEW."ReferenceType" = '{referenceType}')
                EXECUTE FUNCTION erm_reject_movement_{suffix}();
                """;
            await setup.Database.ExecuteSqlRawAsync(sql);
        }
        await using var request = Context();
        await Assert.ThrowsAsync<DbUpdateException>(() => operation == "adjustment"
            ? AdjustAsync(request, seed, batchId, 8) : ExecuteAsync(request, seed, "transfer", "cancel"));
        await using var verify = Context();
        Assert.Equal(operation == "adjustment" ? 5m : 0m,
            (await verify.ProductStocks.SingleAsync(s => s.Id == batchId)).Quantity);
        Assert.Empty(await verify.StockMovements.Where(m => m.ReferenceId == referenceId
            && m.ReferenceType == referenceType).ToListAsync());
        if (operation == "transfer_cancel")
            Assert.Equal("in_transit", (await verify.StockTransfers.SingleAsync(t => t.Id == seed.DocumentId)).Status);
    }

    [PostingPostgresFact]
    public async Task Transfer_cancellation_restores_history_without_erasing_the_original_outbound_movement()
    {
        var seed = await SeedAsync("transfer");
        await using (var request = Context())
            Assert.Null(await ExecuteAsync(request, seed, "transfer", "cancel"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var timestamps = Context())
        {
            var batch = await timestamps.ProductStocks.SingleAsync(s => s.Id == seed.SourceStockId);
            timestamps.Entry(batch).Property(s => s.AddedAt).CurrentValue = today.AddDays(-5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            foreach (var movement in await timestamps.StockMovements.Where(m => m.ReferenceId == seed.DocumentId).ToListAsync())
                timestamps.Entry(movement).Property(m => m.CreatedAt).CurrentValue =
                    today.AddDays(movement.ReferenceType == "stock_transfer_cancellation" ? -1 : -3)
                        .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            await timestamps.SaveChangesAsync();
        }
        await using var verify = Context();
        var history = await new MovementService(new MovementRepository(verify))
            .GetStockHistoryAsync(seed.TenantId, seed.ProductId, null, today.AddDays(-6), today);
        Assert.Equal(new decimal[] { 0, 5, 5, 0, 0, 5, 5 }, history.Points.Select(p => p.Stock));
        var movements = await verify.StockMovements.Where(m => m.ReferenceId == seed.DocumentId).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, m => m.QuantityBefore == 5 && m.QuantityAfter == 0);
        var reversal = Assert.Single(movements, m => m.ReferenceType == "stock_transfer_cancellation");
        Assert.Equal(seed.UserId, reversal.PerformedBy);
        Assert.Equal(0m, reversal.QuantityBefore);
        Assert.Equal(5m, reversal.QuantityAfter);
    }

    [PostingPostgresFact]
    public async Task Manual_adjustment_cannot_change_a_foreign_tenants_batch_under_RLS()
    {
        var own = await SeedAsync("receipt");
        var foreign = await SeedAsync("receipt");
        var ownBatch = await ReceiveBatchAsync(own);
        var foreignBatch = await ReceiveBatchAsync(foreign);
        var role = "erm_adjust_app_" + Guid.NewGuid().ToString("N");
        await using (var setup = Context())
        {
            // The role identifier is generated locally from a UUID, never from request input.
            var sql = $"""
                CREATE ROLE {role} NOSUPERUSER NOBYPASSRLS NOLOGIN;
                GRANT USAGE ON SCHEMA public TO {role};
                GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO {role};
                ALTER TABLE product_stock ENABLE ROW LEVEL SECURITY;
                ALTER TABLE product_stock FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS erm_stock_tenant ON product_stock;
                CREATE POLICY erm_stock_tenant ON product_stock
                USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """;
            await setup.Database.ExecuteSqlRawAsync(sql);
        }
        await using (var request = Context())
        {
            await request.Database.OpenConnectionAsync();
            var sql = $"SET ROLE {role}";
            await request.Database.ExecuteSqlRawAsync(sql);
            await request.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {own.TenantId.ToString()}, false)");
            Assert.Equal("Stock batch not found.", await AdjustAsync(request, own, foreignBatch, 8));
            Assert.Null(await AdjustAsync(request, own, ownBatch, 8));
        }
        await using var verify = Context();
        Assert.Equal(5m, (await verify.ProductStocks.SingleAsync(s => s.Id == foreignBatch)).Quantity);
        Assert.Empty(await verify.StockMovements.Where(m => m.ReferenceId == foreignBatch && m.ReferenceType == "stock_adjustment").ToListAsync());
        Assert.Equal(8m, (await verify.ProductStocks.SingleAsync(s => s.Id == ownBatch)).Quantity);
    }

    [PostingPostgresFact]
    public async Task Offline_catalog_snapshot_survives_database_catalog_edits_and_is_persisted_with_the_sale()
    {
        var seed = await SeedAsync("receipt");
        var batchId = await ReceiveBatchAsync(seed);
        var snapshots = new PosPriceSnapshots(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Jwt:Secret"] = "synthetic-price-snapshot-secret-0123456789" }).Build());
        Guid shiftId; Guid registerId; Guid cashierId; string barcode; string token;
        await using (var setup = Context())
        {
            var item = await setup.Items.SingleAsync(i => i.Id == seed.ProductId);
            item.PriceRetail = 100m; item.PricePurchase = 45m; item.VatRate = 7m; item.Name = "Original priced item";
            barcode = item.Barcodes[0];
            var store = (await setup.ProductStocks.SingleAsync(s => s.Id == batchId)).StoreId;
            var cashier = User.Create(seed.TenantId, Guid.NewGuid().ToString("N") + "@synthetic.example", "Cashier", "hash", "cashier");
            var register = new CashRegister { TenantId = seed.TenantId, StoreId = store, Number = 1, Name = "Synthetic" };
            var shift = new PosShift { TenantId = seed.TenantId, StoreId = store, CashierId = cashier.Id, RegisterId = register.Id };
            setup.AddRange(cashier, register, shift); await setup.SaveChangesAsync();
            shiftId = shift.Id; registerId = register.Id; cashierId = cashier.Id;
            var catalog = await new PosSyncService(new CatalogSyncRepository(setup), priceSnapshots: snapshots).GetCatalogAsync(null, null, 2000);
            token = catalog.Items.Single(i => i.Id == item.Id).PriceSnapshot!;
            item.PriceRetail = 200m; item.PricePurchase = 90m; item.VatRate = 20m; item.Name = "Changed"; item.Barcodes = ["changed-" + item.Id];
            await setup.SaveChangesAsync();
        }
        var request = new CreateSaleRequest(shiftId, [new SaleItemRequest(barcode, 1, token)], "Cash", 150m,
            ClientTransactionId: Guid.NewGuid(), OccurredAt: DateTime.UtcNow, ExpectedTotal: 100m);
        await using (var db = Context())
        {
            var registers = Substitute.For<ICashRegisterRepository>();
            registers.HadSessionAtAsync(registerId, cashierId, Arg.Any<DateTime>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
            var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<PosService>>();
            var service = new PosService(new PosRepository(db), new StockRepository(db), new ItemRepository(db),
                Substitute.For<IDiscountRepository>(), Substitute.For<IFiscalServiceFactory>(), Substitute.For<ILoyaltyRepository>(),
                Substitute.For<ICustomerRepository>(), logger, registers: registers, priceSnapshots: snapshots);
            var result = await service.CreateSalesBatchAsync(seed.TenantId, cashierId, new([request]), registerId: registerId);
            var outcome = Assert.Single(result.Result!.Results);
            var errors = string.Join("\n", logger.ReceivedCalls().SelectMany(c => c.GetArguments().OfType<Exception>()));
            Assert.True(outcome.Status == "created", $"{outcome.Error}\n{errors}");
        }
        await using var verify = Context();
        var transaction = await verify.PosTransactions.Include(t => t.Items).SingleAsync(t => t.ClientTransactionId == request.ClientTransactionId);
        var line = Assert.Single(transaction.Items);
        Assert.Equal(100m, line.LineTotal); Assert.Equal(100m, line.TaxableAmount); Assert.Equal(7m, line.VatRate);
        Assert.Equal(6.54m, transaction.TaxAmount); Assert.Equal(45m, line.UnitCost);
        Assert.Equal("Original priced item", line.ProductNameSnapshot); Assert.Equal(barcode, line.BarcodeSnapshot);
        Assert.Equal(200m, (await verify.Items.SingleAsync(i => i.Id == seed.ProductId)).PriceRetail);
        Assert.Equal(4m, (await verify.ProductStocks.SingleAsync(s => s.Id == batchId)).Quantity);
    }

    [PostingPostgresFact]
    public async Task Pricing_migration_adds_nullable_fields_without_recalculating_legacy_receipts()
    {
        var seed = await SeedAsync("receipt");
        await using var db = Context();
        var store = (await db.StockReceipts.SingleAsync(r => r.Id == seed.DocumentId)).DestinationStoreId;
        var legacy = new PosTransaction { TenantId = seed.TenantId, StoreId = store, CashierId = seed.UserId,
            ReceiptNumber = "legacy-" + Guid.NewGuid().ToString("N"), TotalAmount = 120m, TaxAmount = 17m,
            Items = [new PosTransactionItem { ProductId = seed.ProductId, Quantity = 1m, PriceRetail = 120m, PriceFinal = 120m }] };
        db.PosTransactions.Add(legacy); await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var migration = new AddPosHistoricalPricingSnapshots();
        foreach (var column in migration.UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>())
        {
            var sql = $"ALTER TABLE pos_transaction_items DROP COLUMN \"{column.Name}\"";
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        db.ChangeTracker.Clear();
        var saved = await db.PosTransactions.Include(t => t.Items).SingleAsync(t => t.Id == legacy.Id);
        Assert.Equal(120m, saved.TotalAmount); Assert.Equal(17m, saved.TaxAmount);
        var line = Assert.Single(saved.Items);
        Assert.Equal(120m, line.PriceRetail); Assert.Null(line.VatRate); Assert.Null(line.VatAmount);
        Assert.Null(line.UnitCost); Assert.Null(line.LineTotal); Assert.Null(line.ProductNameSnapshot);
        await transaction.RollbackAsync(); // keep the shared disposable fixture schema unchanged
    }

    [PostingPostgresFact]
    public async Task Iot_inbox_migration_enforces_tenant_scope_and_retains_worker_access()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE iot_message_receipts");
        var migration = new AddIotMessageReceipts();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var own = Guid.NewGuid(); var other = Guid.NewGuid();
        db.IotMessageReceipts.AddRange(
            new IotMessageReceipt { TenantId = own, DeviceId = Guid.NewGuid(), MessageKey = "own", PayloadHash = "hash", RecordedAt = DateTime.UtcNow },
            new IotMessageReceipt { TenantId = other, DeviceId = Guid.NewGuid(), MessageKey = "other", PayloadHash = "hash", RecordedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var role = "iot_test_" + Guid.NewGuid().ToString("N");
        // Identifiers are generated locally from a GUID, never from test input or credentials.
        var roleCommands = new[] { $"CREATE ROLE {role} NOLOGIN NOSUPERUSER NOBYPASSRLS",
            $"GRANT SELECT, INSERT ON iot_message_receipts TO {role}", $"SET LOCAL ROLE {role}" };
        foreach (var command in roleCommands) await db.Database.ExecuteSqlRawAsync(command);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL app.role = ''; SET LOCAL app.tenant_id = ''");
        Assert.Equal(0, await db.IotMessageReceipts.CountAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {own.ToString()}, true)");
        Assert.Equal(own, (await db.IotMessageReceipts.AsNoTracking().SingleAsync()).TenantId);
        await transaction.CreateSavepointAsync("foreign_insert");
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO iot_message_receipts (\"TenantId\",\"DeviceId\",\"MessageKey\",\"PayloadHash\",\"RecordedAt\") VALUES ({other},{Guid.NewGuid()},'bad','hash',NOW())"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, rejected.SqlState);
        await transaction.RollbackToSavepointAsync("foreign_insert");
        await db.Database.ExecuteSqlRawAsync("SET LOCAL app.role = 'worker'");
        Assert.Equal(2, await db.IotMessageReceipts.CountAsync());
        await transaction.RollbackAsync(); // rollback DDL and temporary role as well
    }

    private async Task<Guid> ReceiveBatchAsync(Seed seed)
    {
        await using var db = Context();
        Assert.Null(await ExecuteAsync(db, seed, "receipt", "post"));
        return (await db.ProductStocks.SingleAsync(s => s.SourceId == seed.DocumentId)).Id;
    }

    private static async Task<string?> AdjustAsync(AppDbContext db, Seed seed, Guid batchId, decimal quantity) =>
        (await new StockService(new StockRepository(db)).UpdateAsync(batchId, seed.UserId,
            new UpdateStockRequest(null, null, "original-batch", quantity, seed.ExpiryDate))).Error;

    private ReceiptService Receipts(AppDbContext db) =>
        new(new ReceiptRepository(db), Substitute.For<IActivityLogRepository>());

    private async Task<string?> ExecuteAsync(AppDbContext db, Seed seed, string kind, string action)
    {
        if (kind == "receipt")
        {
            var service = Receipts(db);
            return action == "post" ? (await service.ReceiveAsync(seed.DocumentId, seed.UserId)).Error
                : (await service.CancelAsync(seed.DocumentId)).Error;
        }
        var transfers = new TransferService(new TransferRepository(db), Substitute.For<IUserLocationRepository>());
        return action == "post"
            ? (await transfers.ConfirmAsync(seed.DocumentId, seed.UserId, seed.TenantId, "enterprise_admin")).Error
            : (await transfers.CancelAsync(seed.DocumentId, seed.UserId)).Error;
    }

    private async Task<string?> EditReceiptAsync(AppDbContext db, Seed seed) =>
        (await Receipts(db).UpdateItemsAsync(seed.DocumentId,
            new UpdateItemsRequest([new UpdateItemPayload(seed.LineId, 7m, null, null, null, "counted")]))).Error;

    private static async Task<bool> AttemptAsync(AppDbContext db, Func<Task<string?>> action, SaveGate? firstGate = null)
    {
        try
        {
            Assert.Null(await action());
            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            Assert.Empty(db.ChangeTracker.Entries());
            // Exercise the existing API error contract used by both document endpoints.
            var http = new DefaultHttpContext();
            http.Response.Body = new MemoryStream();
            var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
            Assert.True(await handler.TryHandleAsync(http, ex, CancellationToken.None));
            Assert.Equal(StatusCodes.Status409Conflict, http.Response.StatusCode);
            http.Response.Body.Position = 0;
            using var body = await JsonDocument.ParseAsync(http.Response.Body);
            Assert.Equal("concurrent_update", body.RootElement.GetProperty("code").GetString());
            return false;
        }
        finally
        {
            firstGate?.FirstFinished.TrySetResult();
        }
    }

    private async Task AssertResultAsync(Seed seed, string kind, bool posted)
    {
        await using var db = Context();
        var status = kind == "receipt"
            ? (await db.StockReceipts.SingleAsync(r => r.Id == seed.DocumentId)).Status
            : (await db.StockTransfers.SingleAsync(t => t.Id == seed.DocumentId)).Status;
        Assert.Equal(posted ? "received" : "cancelled", status);
        var destination = await db.ProductStocks.Where(s => s.SourceId == seed.DocumentId).ToListAsync();
        Assert.Equal(posted ? 1 : 0, destination.Count);
        if (posted)
        {
            var batch = Assert.Single(destination);
            Assert.Equal(5m, batch.Quantity);
            Assert.Equal(seed.ExpiryDate, batch.ExpiryDate);
            Assert.Equal("original-batch", batch.BatchNumber);
            Assert.Equal(seed.TenantId, batch.TenantId);
        }
        Assert.Equal(kind == "transfer" ? 2 : posted ? 1 : 0,
            await db.StockMovements.CountAsync(m => m.ReferenceId == seed.DocumentId));
        Assert.Equal(kind == "receipt" && posted ? 1 : 0,
            await db.NotificationQueues.CountAsync(n => n.TenantId == seed.TenantId));
        if (kind == "transfer")
        {
            Assert.Equal(posted ? 0m : 5m,
                (await db.ProductStocks.SingleAsync(s => s.Id == seed.SourceStockId)).Quantity);
            Assert.Equal(5m, await db.ProductStocks.Where(s => s.TenantId == seed.TenantId).SumAsync(s => s.Quantity));
        }
    }

    private async Task<Seed> SeedAsync(string kind)
    {
        var tenant = Tenant.Create("Posting fixture", Guid.NewGuid().ToString("N"));
        var product = new Item { TenantId = tenant.Id, Name = "Synthetic posting item", Barcodes = [Guid.NewGuid().ToString("N")] };
        var from = new Location { TenantId = tenant.Id, Name = "From" };
        var to = new Location { TenantId = tenant.Id, Name = "To" };
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), tenant.Id, product.Id, Guid.NewGuid(),
            Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
        await using var db = Context();
        db.AddRange(tenant, product, from, to);
        if (kind == "receipt")
        {
            db.StockReceipts.Add(new StockReceipt
            {
                Id = seed.DocumentId, TenantId = tenant.Id, DestinationStoreId = to.Id,
                Items = [new StockReceiptItem { Id = seed.LineId, ReceiptId = seed.DocumentId, ProductId = product.Id,
                    QuantityOrdered = 5m, ExpiryDate = seed.ExpiryDate, BatchNumber = "original-batch", PricePurchase = 10m }]
            });
        }
        else
        {
            db.ProductStocks.Add(new ProductStock { Id = seed.SourceStockId, TenantId = tenant.Id,
                ProductId = product.Id, StoreId = from.Id, Quantity = 0m, QuantityInitial = 5m,
                ExpiryDate = seed.ExpiryDate, BatchNumber = "original-batch", Status = "sold_out" });
            db.StockTransfers.Add(new StockTransfer { Id = seed.DocumentId, TenantId = tenant.Id,
                FromStoreId = from.Id, ToStoreId = to.Id, Status = "in_transit", TransferType = "store_to_store",
                Items = [new StockTransferItem { Id = seed.LineId, TransferId = seed.DocumentId,
                    ProductStockId = seed.SourceStockId, ProductId = product.Id, Quantity = 5m,
                    ExpiryDate = seed.ExpiryDate, BatchNumber = "original-batch" }] });
            db.StockMovements.Add(new StockMovement { TenantId = tenant.Id, MovementType = "transfer",
                ProductId = product.Id, ProductStockId = seed.SourceStockId, ReferenceId = seed.DocumentId,
                FromStoreId = from.Id, ToStoreId = to.Id, Quantity = 5m, QuantityBefore = 5m, QuantityAfter = 0m });
        }
        await db.SaveChangesAsync();
        return seed;
    }

    private sealed record Seed(Guid DocumentId, Guid LineId, Guid TenantId, Guid ProductId,
        Guid UserId, Guid SourceStockId, DateOnly ExpiryDate);

    private sealed class SaveGate
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IInterceptor Interceptor(bool first) => new GatedSave(this, first);

        private sealed class GatedSave(SaveGate gate, bool first) : SaveChangesInterceptor
        {
            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref gate._arrivals) == 2) gate._ready.TrySetResult();
                await gate._ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                if (!first) await gate.FirstFinished.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                return result;
            }
        }
    }
}

public sealed class StockDocumentConcurrencyModelTests
{
    [Fact]
    public void Iot_inbox_migration_is_additive_and_preserves_replay_keys_on_downgrade()
    {
        var migration = new AddIotMessageReceipts();
        Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>());
        var index = Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>());
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "TenantId", "DeviceId", "MessageKey" }, index.Columns);
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
    }

    [Fact]
    public void Pricing_migration_only_adds_nullable_columns_and_does_not_offer_a_destructive_downgrade()
    {
        var migration = new AddPosHistoricalPricingSnapshots();
        var columns = migration.UpOperations.Cast<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>().ToList();
        Assert.Equal(8, columns.Count);
        Assert.All(columns, c => { Assert.Equal("pos_transaction_items", c.Table); Assert.True(c.IsNullable); Assert.Null(c.DefaultValue); });
        Assert.Throws<NotSupportedException>(() => _ = migration.DownOperations);
    }

    [Fact]
    public void Concurrency_migration_has_no_schema_operations_and_snapshot_matches_the_runtime_model()
    {
        // No database connection is opened for the model/migration metadata assertions.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=not_opened").Options);
        var migration = new AddStockDocumentConcurrencyTokens();
        Assert.Empty(migration.UpOperations);
        Assert.Empty(migration.DownOperations);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("20261024180000_AddStockDocumentConcurrencyTokens", db.Database.GetMigrations());
        foreach (var type in new[] { typeof(StockReceipt), typeof(StockTransfer) })
        {
            var version = db.Model.FindEntityType(type)!.FindProperty("xmin")!;
            Assert.True(version.IsConcurrencyToken);
            Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        }
    }
}
