using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Migrations;
using ShelfGuard.Tests.Infrastructure;
using Xunit;

namespace ShelfGuard.Tests.Erp;

public sealed class ErpPostgresFactAttribute : FactAttribute
{
    public ErpPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")))
            Skip = "Requires an explicit disposable erm_schema_test_* PostgreSQL database.";
    }
}
public sealed class ErpPostgresTheoryAttribute : TheoryAttribute
{
    public ErpPostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")))
            Skip = "Requires an explicit disposable erm_schema_test_* PostgreSQL database.";
    }
}

[CollectionDefinition("ERP schema acceptance", DisableParallelization = true)]
public sealed class ErpSchemaCollection : ICollectionFixture<ErpSchemaFixture> { }

public sealed class ErpSchemaFixture : IAsyncLifetime
{
    private readonly string _connection = Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB") ?? "";
    private bool _roleCreated;
    public string ReaderRole { get; } = "erp_test_" + Guid.NewGuid().ToString("N");
    public Guid LegacySaleId { get; private set; }
    public string LegacySaleSnapshot { get; private set; } = "";
    public Guid LegacyBatchId { get; private set; }
    public string LegacyFingerprint { get; private set; } = "";

    public AppDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(TestPostgres.DataSource(_connection)).AddInterceptors(interceptors).IgnoreManyServiceProvidersWarning().Options);

    public async Task InitializeAsync()
    {
        if (_connection.Length == 0) return;
        var connection = new NpgsqlConnectionStringBuilder(_connection);
        if (connection.Database?.StartsWith("erm_schema_test_", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("ERP schema acceptance can reset only a disposable erm_schema_test_* database.");
        await using var db = Context();
        await db.Database.OpenConnectionAsync();
        // This fixture owns the explicitly named disposable schema; application databases are forbidden above.
        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA public CASCADE; CREATE SCHEMA public; GRANT USAGE ON SCHEMA public TO PUBLIC; CREATE EXTENSION pg_trgm");
        var initializer = db.GetService<IModelRuntimeInitializer>();
        var previous = initializer.Initialize(new AddIotMessageReceipts().TargetModel, designTime: true);
        var operations = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, previous.GetRelationalModel());
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(operations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);

        // These migrations were imported from the expanded production branch after the original W1 target model.
        foreach (Migration migration in new Migration[] { new AddUkrainianNameCollation(), new AddProductionRequests(), new AddProductionRequestLineAutoReason(),
                     new AddAutoReplenishmentDrafts(), new AddReceiptOrderedFields() })
            foreach (var command in generator.Generate(migration.UpOperations, previous))
                await ExecuteGeneratedAsync(db, command.CommandText);

        // Seed the preceding schema directly: migration target models use property bags, not CLR entities.
        {
            var tenant = Tenant.Create("Synthetic ERP legacy", "erm-legacy");
            var item = new Item { TenantId = tenant.Id, Name = "Legacy unchanged", PricePurchase = 12.50m, PriceRetail = 27.99m };
            var location = new Location { TenantId = tenant.Id, Name = "Legacy store" };
            var batch = new ProductStock { TenantId = tenant.Id, ProductId = item.Id, StoreId = location.Id,
                Quantity = 4.25m, QuantityInitial = 5m, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), BatchNumber = "legacy-lot" };
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO tenants (\"Id\",\"Name\",\"Slug\") VALUES ({tenant.Id},{tenant.Name},{tenant.Slug})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO items (\"Id\",\"TenantId\",\"Name\",\"MinStock\",\"MaxStock\",\"SafetyBuffer\",\"PricePurchase\",\"PriceRetail\") VALUES ({item.Id},{tenant.Id},{item.Name},0,0,0,{item.PricePurchase},{item.PriceRetail})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO locations (\"Id\",\"TenantId\",\"Name\",\"Type\") VALUES ({location.Id},{tenant.Id},{location.Name},{location.Type})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO product_stock (\"Id\",\"TenantId\",\"ProductId\",\"LocationId\",\"Quantity\",\"QuantityInitial\",\"ExpiryDate\",\"BatchNumber\") VALUES ({batch.Id},{tenant.Id},{item.Id},{location.Id},{batch.Quantity},{batch.QuantityInitial},{batch.ExpiryDate},{batch.BatchNumber})");
            LegacyBatchId = batch.Id;
        }
        LegacyFingerprint = await FingerprintAsync(db);
        var history = db.GetService<IHistoryRepository>();
        await ExecuteGeneratedAsync(db, history.GetCreateIfNotExistsScript());
        await ExecuteGeneratedAsync(db, history.GetInsertScript(new HistoryRow("20261024210000_AddIotMessageReceipts", "8.0.11")));
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "erp-w2.sql"));
        await ExecuteGeneratedAsync(db, script);
        await ExecuteGeneratedAsync(db, script); // the exact delivered artifact must also be safe to run again
        Assert.Equal(LegacyFingerprint, await FingerprintAsync(db));

        // W4-B SQL-only operational fence registry is additive; no scope is activated by migration.
        var fence = new AddInventoryPostingScopeFence();
        foreach (var command in generator.Generate(fence.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        Assert.Equal(LegacyFingerprint, await FingerprintAsync(db));

        // Random role identifier is generated locally; never derives from credentials or external input.
        var receiving = new AddErpReceiptPosting();
        foreach (var command in generator.Generate(receiving.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        Assert.Equal(LegacyFingerprint, await FingerprintAsync(db));

        var transferPosting = new AddErpTransferPosting();
        foreach (var command in generator.Generate(transferPosting.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        Assert.Equal(LegacyFingerprint, await FingerprintAsync(db));
        var writeOffPosting = new AddErpWriteOffPosting();
        foreach (var command in generator.Generate(writeOffPosting.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        var posPosting = new AddErpPosSalePosting();
        foreach (var command in generator.Generate(posPosting.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        var posReturns = new AddErpPosReturnPosting();
        foreach (var command in generator.Generate(posReturns.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        var production = new AddErpProductionPosting();
        foreach (var command in generator.Generate(production.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        var iot = new AddErpIotPosting();
        foreach (var command in generator.Generate(iot.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        var integrated = new AddIntegratedInventoryReadiness();
        foreach (var command in generator.Generate(integrated.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        var pricing = new AddPlannedPricing();
        foreach (var command in generator.Generate(pricing.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        Assert.Equal(LegacyFingerprint, await FingerprintAsync(db));
        LegacySaleId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO pos_transactions (\"Id\",\"TenantId\",\"LocationId\",\"ReceiptNumber\",\"TotalAmount\",\"TaxAmount\",\"RetryCount\") SELECT {LegacySaleId},\"TenantId\",\"LocationId\",'legacy-pricing-before-archive',0,0,0 FROM product_stock WHERE \"Id\"={LegacyBatchId}");
        LegacySaleSnapshot = await db.Database.SqlQuery<string>($"SELECT public.erp_w4_pos_snapshot({LegacySaleId})::text AS \"Value\"").SingleAsync();
        var archive = new AddPosPricingArchive();
        foreach (var command in generator.Generate(archive.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        Assert.Equal(LegacySaleSnapshot, await db.Database.SqlQuery<string>($"SELECT public.erp_w4_pos_snapshot({LegacySaleId})::text AS \"Value\"").SingleAsync());
        var labels = new AddLabelTemplateLibrary();
        foreach (var command in generator.Generate(labels.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        Assert.Equal(LegacyFingerprint, await FingerprintAsync(db));
        var labelJobs = new AddLabelJobArchive();
        foreach (var command in generator.Generate(labelJobs.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        Assert.Equal(LegacyFingerprint, await FingerprintAsync(db));
        var printers = new AddLabelPrinterProfiles();
        foreach (var command in generator.Generate(printers.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        Assert.Equal(LegacyFingerprint, await FingerprintAsync(db));
        var categoryWork = new AddReceiptCategoryWork();
        foreach (var command in generator.Generate(categoryWork.UpOperations, previous))
            await ExecuteGeneratedAsync(db, command.CommandText);
        var roleCommands = new[] { $"CREATE ROLE {ReaderRole} NOLOGIN NOSUPERUSER NOBYPASSRLS",
            $"GRANT USAGE ON SCHEMA public TO {ReaderRole}",
            $"GRANT SELECT,INSERT,UPDATE,DELETE,TRUNCATE ON ALL TABLES IN SCHEMA public TO {ReaderRole}" };
        foreach (var command in roleCommands) await db.Database.ExecuteSqlRawAsync(command);
        _roleCreated = true;
    }

    public async Task<string> FingerprintAsync(AppDbContext db) => await db.Database.SqlQuery<string>(
        $"SELECT md5(jsonb_build_object('batch',to_jsonb(p),'item',to_jsonb(i),'location',to_jsonb(l),'tenant',to_jsonb(t))::text) AS \"Value\" FROM product_stock p JOIN items i ON i.\"Id\"=p.\"ProductId\" JOIN locations l ON l.\"Id\"=p.\"LocationId\" JOIN tenants t ON t.\"Id\"=p.\"TenantId\" WHERE p.\"Id\"={LegacyBatchId}").SingleAsync();

    private static async Task ExecuteGeneratedAsync(AppDbContext db, string sql)
    {
        // Generated DDL includes JSON literals and regex braces; it is not a composite format string.
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_roleCreated) return;
        await using var db = Context();
        var commands = new[] { $"DROP OWNED BY {ReaderRole}", $"DROP ROLE {ReaderRole}" };
        foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command);
    }
}
