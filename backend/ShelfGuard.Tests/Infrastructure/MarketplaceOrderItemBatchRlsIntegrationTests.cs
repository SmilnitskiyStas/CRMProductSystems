using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Supplier-portal expansion — Phase 3 (plan `1-partitioned-book.md`, decision D4).
///
/// <c>marketplace_order_item_batches</c> is the only table in the marketplace feature area whose
/// split RLS points the OTHER way from ADR-033's receipts: the SUPPLIER writes
/// (<c>tenant_isolation</c> on <c>SupplierTenantId</c>) and the CLIENT only reads
/// (<c>client_read</c>, FOR SELECT on <c>ClientTenantId</c>). Getting that inversion wrong fails
/// silently in two directions — an over-tight <c>client_read</c> gives every buyer an empty,
/// unexplained receiving draft; an over-loose <c>tenant_isolation</c> lets the buyer rewrite the
/// supplier's shipment ledger — so it is proved here against a REAL Postgres under a genuine
/// <c>rls_audit_test_role</c> (NOSUPERUSER NOBYPASSRLS) session, never InMemory.
///
/// Four claims:
///   1. the supplier can INSERT + SELECT its own order's allocations;
///   2. the client can SELECT them (this is what prefills its receiving draft) …
///   3. … and can NOT INSERT / UPDATE / DELETE the supplier's rows (42501 on insert; zero rows
///      touched on update/delete, which is how Postgres RLS reports "no visible row" for those
///      verbs) — plus the one write it CAN make (a row naming itself as supplier) is proved
///      invisible to the real supplier, see
///      <see cref="ClientSelfAttributedRow_IsInvisibleToTheRealSupplier"/>;
///   4. an unrelated third supplier tenant sees nothing at all, and a fully-RESET session sees
///      zero rows (fail-closed).
///
/// The triad audit (<c>AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass</c>
/// in <see cref="RlsCrossTenantIntegrationTests"/>) picks the new table up automatically; the
/// extra <c>client_read</c> policy is additive and does not disturb it. Same harness / collection
/// / soft-skip conventions as <see cref="SupplierStockRlsIntegrationTests"/>.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class MarketplaceOrderItemBatchRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private bool _dbAvailable;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    private readonly string _run = Guid.NewGuid().ToString("N");

    public MarketplaceOrderItemBatchRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        if (!_fixture.DbAvailable)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping marketplace-order-item-batch RLS tests — no reachable Postgres: {_fixture.UnavailableReason}");
            return Task.CompletedTask;
        }

        try
        {
            _dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).EnableDynamicJson().Build();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_dataSource)
                .IgnoreManyServiceProvidersWarning()
                .Options;
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping marketplace-order-item-batch RLS tests — no reachable Postgres: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    // ── 1. supplier writes ───────────────────────────────────────────────────

    [Fact]
    public async Task SupplierSession_CanInsertAndReadItsOwnOrdersBatches()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedAsync();
        try
        {
            await using var supplier = await OpenSessionAsync(f.SupplierTenantId);

            var inserted = await ExecAsync(supplier, InsertBatchSql(f), expectSuccess: true);
            Assert.Equal(1, inserted);

            var own = await ScalarAsync(supplier,
                $"SELECT count(*) FROM marketplace_order_item_batches WHERE \"OrderId\" = '{f.OrderId:D}';");
            Assert.Equal(1L, own);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    // ── 2 + 3. client reads, client cannot write ─────────────────────────────

    [Fact]
    public async Task ClientSession_CanSelectSupplierBatches_ButCannotWriteThem()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedAsync();
        try
        {
            await using (var supplier = await OpenSessionAsync(f.SupplierTenantId))
                await ExecAsync(supplier, InsertBatchSql(f), expectSuccess: true);

            await using var client = await OpenSessionAsync(f.ClientTenantId);

            // (2) client_read — this is exactly what prefills the receiving draft.
            var visible = await ScalarAsync(client,
                $"SELECT count(*) FROM marketplace_order_item_batches WHERE \"OrderId\" = '{f.OrderId:D}';");
            Assert.Equal(1L, visible);

            // (3a) INSERT — tenant_isolation's WITH CHECK is keyed on SupplierTenantId, and
            // client_read is FOR SELECT only, so no policy can admit this row. 42501.
            var insertError = await ExpectPostgresErrorAsync(client, InsertBatchSql(f));
            Assert.Equal("42501", insertError);

            // (3b) UPDATE / DELETE — RLS reports "no visible row" for these verbs by simply
            // matching nothing, so the proof is zero rows affected plus an unchanged row.
            var updated = await ExecAsync(client,
                $"UPDATE marketplace_order_item_batches SET \"Qty\" = 999 WHERE \"OrderId\" = '{f.OrderId:D}';",
                expectSuccess: true);
            Assert.Equal(0, updated);

            var deleted = await ExecAsync(client,
                $"DELETE FROM marketplace_order_item_batches WHERE \"OrderId\" = '{f.OrderId:D}';",
                expectSuccess: true);
            Assert.Equal(0, deleted);

            var stillOriginal = await ScalarAsync(client,
                $"SELECT count(*) FROM marketplace_order_item_batches WHERE \"OrderId\" = '{f.OrderId:D}' AND \"Qty\" = 40;");
            Assert.Equal(1L, stillOriginal);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    /// <summary>
    /// Documents the one write a client session CAN make here, and proves it is inert.
    ///
    /// <c>tenant_isolation</c>'s WITH CHECK says "the row's SupplierTenantId must be me", so a
    /// client tenant can insert a row that names ITSELF as the supplier. That is not an
    /// escalation and is not worth restricting: such a row is invisible to the real supplier (its
    /// own policy filters on SupplierTenantId = supplier, which this row is not), so the
    /// supplier's shipment ledger cannot be polluted; and the only thing it can affect is the
    /// client's own receiving-draft prefill, whose ExpiryDate/BatchNumber the client already
    /// types by hand on every legacy order. Nothing in the application layer writes this table
    /// from a client session — <see cref="MarketplaceOrderReceiptService"/> only reads it.
    ///
    /// The test exists so that this stays a deliberate, understood property: if a future change
    /// makes such a row visible to the supplier, this fails.
    /// </summary>
    [Fact]
    public async Task ClientSelfAttributedRow_IsInvisibleToTheRealSupplier()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedAsync();
        try
        {
            await using (var supplier = await OpenSessionAsync(f.SupplierTenantId))
                await ExecAsync(supplier, InsertBatchSql(f), expectSuccess: true);

            await using (var client = await OpenSessionAsync(f.ClientTenantId))
            {
                var forged = await ExecAsync(
                    client, InsertBatchSql(f, forgeSupplierTenant: true), expectSuccess: true);
                Assert.Equal(1, forged);
            }

            await using var supplierAgain = await OpenSessionAsync(f.SupplierTenantId);
            var visibleToSupplier = await ScalarAsync(supplierAgain,
                $"SELECT count(*) FROM marketplace_order_item_batches WHERE \"OrderId\" = '{f.OrderId:D}';");

            // Only the supplier's own row — the client-attributed one is filtered out.
            Assert.Equal(1L, visibleToSupplier);
            Assert.Equal(1L, await ScalarAsync(supplierAgain,
                $"SELECT count(*) FROM marketplace_order_item_batches WHERE \"SupplierTenantId\" = '{f.SupplierTenantId:D}';"));
            Assert.Equal(0L, await ScalarAsync(supplierAgain,
                $"SELECT count(*) FROM marketplace_order_item_batches WHERE \"SupplierTenantId\" = '{f.ClientTenantId:D}';"));
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    // ── 4. everyone else sees nothing ────────────────────────────────────────

    [Fact]
    public async Task UnrelatedSupplierTenant_AndResetSession_SeeNothing()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedAsync();
        try
        {
            await using (var supplier = await OpenSessionAsync(f.SupplierTenantId))
                await ExecAsync(supplier, InsertBatchSql(f), expectSuccess: true);

            await using (var other = await OpenSessionAsync(f.OtherSupplierTenantId))
            {
                // Unfiltered, and with the real order id forged straight into the WHERE clause.
                Assert.Equal(0L, await ScalarAsync(other,
                    "SELECT count(*) FROM marketplace_order_item_batches;"));
                Assert.Equal(0L, await ScalarAsync(other,
                    $"SELECT count(*) FROM marketplace_order_item_batches WHERE \"OrderId\" = '{f.OrderId:D}';"));
            }

            await using var db = NewContext();
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
            await db.Database.ExecuteSqlRawAsync("RESET app.tenant_id; RESET app.role;");
            var onReset = await ScalarAsync(db, "SELECT count(*) FROM marketplace_order_item_batches;");
            await db.Database.ExecuteSqlRawAsync("RESET ROLE;");

            // Fail-closed: NULLIF-guarded policies with no IS-NULL-OR branch.
            Assert.Equal(0L, onReset);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    // ── policy shape (mirror of the ADR-033 receipt split assertions) ────────

    [Fact]
    public async Task Table_HasInvertedSplitPolicies_SupplierWrite_ClientReadOnly()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var cmd = new NpgsqlCommand(
            @"SELECT policyname, cmd, qual, coalesce(with_check, '') FROM pg_policies
              WHERE schemaname = 'public' AND tablename = 'marketplace_order_item_batches'
              ORDER BY policyname;", connection);

        var policies = new Dictionary<string, (string Cmd, string Qual, string WithCheck)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                policies[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2), reader.GetString(3));
        }

        // The audit test requires these three literal names; client_read is the extra one.
        Assert.True(policies.ContainsKey("tenant_isolation"));
        Assert.True(policies.ContainsKey("provider_bypass"));
        Assert.True(policies.ContainsKey("worker_bypass"));
        Assert.True(policies.ContainsKey("client_read"));

        // The inversion itself: the write policy is keyed on the SUPPLIER …
        var isolation = policies["tenant_isolation"];
        Assert.Equal("ALL", isolation.Cmd);
        Assert.Contains("SupplierTenantId", isolation.Qual);
        Assert.Contains("NULLIF", isolation.Qual);
        Assert.DoesNotContain("IS NULL", isolation.Qual);          // fail-closed
        Assert.Contains("SupplierTenantId", isolation.WithCheck);  // and it really is a write gate

        // … and the CLIENT gets read only, never a WITH CHECK.
        var clientRead = policies["client_read"];
        Assert.Equal("SELECT", clientRead.Cmd);
        Assert.Contains("ClientTenantId", clientRead.Qual);
        Assert.Contains("NULLIF", clientRead.Qual);
        Assert.Equal(string.Empty, clientRead.WithCheck);

        var forced = await ScalarAsync(db,
            "SELECT count(*) FROM pg_class WHERE relname = 'marketplace_order_item_batches' " +
            "AND relrowsecurity AND relforcerowsecurity;");
        Assert.Equal(1L, forced);
    }

    // ── seed / session / cleanup ─────────────────────────────────────────────

    private sealed record Fixture(
        Guid SupplierTenantId,
        Guid ClientTenantId,
        Guid OtherSupplierTenantId,
        Guid OrderId,
        Guid OrderItemId)
    {
        public Guid[] AllTenantIds => [SupplierTenantId, ClientTenantId, OtherSupplierTenantId];
    }

    /// <summary>
    /// One shipped order of (supplier → client) plus an unrelated third supplier tenant. Seeded
    /// on the plain <c>crm</c> connection (no rls_audit_test_role) — the point under test is the
    /// policies, not the seeding path.
    /// </summary>
    private async Task<Fixture> SeedAsync()
    {
        await using var db = NewContext();

        var supplierTenant = Tenant.Create($"Batch RLS Supplier {_run}", $"batch-rls-sup-{_run}");
        supplierTenant.UpdateBusinessType("supplier");
        var clientTenant = Tenant.Create($"Batch RLS Client {_run}", $"batch-rls-cli-{_run}");
        var otherSupplierTenant = Tenant.Create($"Batch RLS Other {_run}", $"batch-rls-oth-{_run}");
        otherSupplierTenant.UpdateBusinessType("supplier");
        db.Tenants.AddRange(supplierTenant, clientTenant, otherSupplierTenant);

        var agreement = new SupplierAgreement
        {
            SupplierTenantId = supplierTenant.Id,
            ClientTenantId = clientTenant.Id,
            Status = SupplierAgreementStatus.Active,
            ContractNumber = $"ДС-P3-{_run[..8]}",
        };
        db.SupplierAgreements.Add(agreement);

        var order = new MarketplaceOrder
        {
            OrderNumber = $"MP-P3-{_run[..8]}",
            AgreementId = agreement.Id,
            SupplierTenantId = supplierTenant.Id,
            ClientTenantId = clientTenant.Id,
            Status = MarketplaceOrderStatus.Shipped,
            TotalAmount = 100m,
        };
        var orderItem = new MarketplaceOrderItem
        {
            OrderId = order.Id,
            SupplierTenantId = supplierTenant.Id,
            ClientTenantId = clientTenant.Id,
            ItemName = "Молоко 2.5%",
            Unit = "шт",
            Price = 10m,
            Qty = 40m,
            LineTotal = 400m,
        };
        order.Items.Add(orderItem);
        db.MarketplaceOrders.Add(order);

        await db.SaveChangesAsync();

        return new Fixture(
            supplierTenant.Id, clientTenant.Id, otherSupplierTenant.Id, order.Id, orderItem.Id);
    }

    /// <summary>
    /// Raw INSERT rather than EF so the statement is issued verbatim on whichever session the
    /// test opened — no change-tracker or interceptor in the way.
    /// </summary>
    private static string InsertBatchSql(Fixture f, bool forgeSupplierTenant = false)
    {
        var supplierTenantId = forgeSupplierTenant ? f.ClientTenantId : f.SupplierTenantId;
        return
            "INSERT INTO marketplace_order_item_batches " +
            "(\"Id\", \"OrderItemId\", \"OrderId\", \"SupplierTenantId\", \"ClientTenantId\", " +
            " \"ExpiryDate\", \"BatchNumber\", \"Qty\", \"CreatedAt\") VALUES " +
            $"(gen_random_uuid(), '{f.OrderItemId:D}', '{f.OrderId:D}', '{supplierTenantId:D}', " +
            $"'{f.ClientTenantId:D}', DATE '2026-12-01', 'B-1', 40, now());";
    }

    private async Task<RlsSession> OpenSessionAsync(Guid tenantId)
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"SET app.tenant_id = '{tenantId:D}'; SET app.role = 'store_manager'; RESET app.consumer_account_id;");
#pragma warning restore EF1002
        return new RlsSession(db);
    }

    private static async Task<int> ExecAsync(RlsSession session, string sql, bool expectSuccess)
    {
        await using var cmd = session.Db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        try
        {
            return await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException) when (!expectSuccess)
        {
            return -1;
        }
    }

    /// <summary>Runs <paramref name="sql"/> expecting an RLS refusal; returns its SQLSTATE.</summary>
    private static async Task<string> ExpectPostgresErrorAsync(RlsSession session, string sql)
    {
        await using var cmd = session.Db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        return ex.SqlState;
    }

    private static async Task<long> ScalarAsync(AppDbContext db, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static Task<long> ScalarAsync(RlsSession session, string sql) => ScalarAsync(session.Db, sql);

    private async Task CleanupAsync(Fixture f)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM marketplace_order_item_batches WHERE \"SupplierTenantId\" = ANY({f.AllTenantIds}) OR \"ClientTenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM marketplace_order_items WHERE \"SupplierTenantId\" = ANY({f.AllTenantIds}) OR \"ClientTenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM marketplace_orders WHERE \"SupplierTenantId\" = ANY({f.AllTenantIds}) OR \"ClientTenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_agreements WHERE \"SupplierTenantId\" = ANY({f.AllTenantIds}) OR \"ClientTenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" = ANY({f.AllTenantIds})");
    }

    private AppDbContext NewContext() => new(_options!);

    private sealed class RlsSession : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public RlsSession(AppDbContext db) => Db = db;

        public async ValueTask DisposeAsync()
        {
            try { await Db.Database.ExecuteSqlRawAsync("RESET ROLE;"); }
            catch { /* best-effort cleanup only */ }
            await Db.DisposeAsync();
        }
    }
}
