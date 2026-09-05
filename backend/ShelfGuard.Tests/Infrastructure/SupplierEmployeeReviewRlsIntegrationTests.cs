using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-695 (Phase 8). <c>supplier_employee_reviews</c> carries the ADR-033 split RLS: the BUYER
/// writes (<c>tenant_isolation</c> on <c>ClientTenantId</c>) and the SUPPLIER only reads
/// (<c>supplier_read</c>, FOR SELECT on <c>SupplierTenantId</c>) — the same direction as
/// <c>marketplace_order_receipts</c>, the opposite of <c>marketplace_order_item_batches</c>.
/// Getting it wrong fails silently: an over-tight <c>supplier_read</c> hides its own team's
/// ratings from a manager; an over-loose <c>tenant_isolation</c> lets a supplier fabricate
/// glowing ratings of itself. Proved here against a REAL Postgres under a genuine
/// <c>rls_audit_test_role</c> (NOSUPERUSER NOBYPASSRLS) session, never InMemory.
///
/// The triad audit (<c>AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass</c>
/// in <see cref="RlsCrossTenantIntegrationTests"/>) picks the new table up automatically; the
/// extra <c>supplier_read</c> policy is additive and does not disturb it. Same harness /
/// collection / soft-skip conventions as <see cref="MarketplaceOrderItemBatchRlsIntegrationTests"/>.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class SupplierEmployeeReviewRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private bool _dbAvailable;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    private readonly string _run = Guid.NewGuid().ToString("N");

    public SupplierEmployeeReviewRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
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
                $"Skipping supplier-employee-review RLS tests — no reachable Postgres: {_fixture.UnavailableReason}");
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
                $"Skipping supplier-employee-review RLS tests — no reachable Postgres: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    // ── 1. buyer writes + reads ──────────────────────────────────────────────

    [Fact]
    public async Task ClientSession_CanInsertAndReadItsOwnRatings()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedAsync();
        try
        {
            await using var client = await OpenSessionAsync(f.ClientTenantId);

            var inserted = await ExecAsync(client, InsertReviewSql(f), expectSuccess: true);
            Assert.Equal(1, inserted);

            var own = await ScalarAsync(client,
                $"SELECT count(*) FROM supplier_employee_reviews WHERE \"OrderId\" = '{f.OrderId:D}';");
            Assert.Equal(1L, own);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    // ── 2 + 3. supplier reads, supplier cannot write ─────────────────────────

    [Fact]
    public async Task SupplierSession_CanSelectRatings_ButCannotWriteThem()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedAsync();
        try
        {
            await using (var client = await OpenSessionAsync(f.ClientTenantId))
                await ExecAsync(client, InsertReviewSql(f), expectSuccess: true);

            await using var supplier = await OpenSessionAsync(f.SupplierTenantId);

            // (2) supplier_read — this is what the team-performance rollup reads.
            var visible = await ScalarAsync(supplier,
                $"SELECT count(*) FROM supplier_employee_reviews WHERE \"OrderId\" = '{f.OrderId:D}';");
            Assert.Equal(1L, visible);

            // (3a) INSERT — no policy can admit a supplier-authored row:
            //   • attributed to the real buyer  → WITH CHECK wants ClientTenantId = app.tenant_id
            //     (the supplier), which it is not;
            //   • attributed to ITSELF as buyer → the extra `ClientTenantId <> SupplierTenantId`
            //     guard rejects it, closing the supplier-self-rating vector.
            // supplier_read is FOR SELECT only, so it never helps a write. Both → 42501.
            Assert.Equal("42501", await ExpectPostgresErrorAsync(supplier, InsertReviewSql(f)));
            Assert.Equal("42501", await ExpectPostgresErrorAsync(
                supplier, InsertReviewSql(f, forgeAsSupplierAuthored: true)));

            // (3b) UPDATE / DELETE — RLS reports "no visible row" for these verbs by matching
            // nothing, so the proof is zero rows affected plus an unchanged row.
            var updated = await ExecAsync(supplier,
                $"UPDATE supplier_employee_reviews SET \"Rating\" = 1 WHERE \"OrderId\" = '{f.OrderId:D}';",
                expectSuccess: true);
            Assert.Equal(0, updated);

            var deleted = await ExecAsync(supplier,
                $"DELETE FROM supplier_employee_reviews WHERE \"OrderId\" = '{f.OrderId:D}';",
                expectSuccess: true);
            Assert.Equal(0, deleted);

            var stillOriginal = await ScalarAsync(supplier,
                $"SELECT count(*) FROM supplier_employee_reviews WHERE \"OrderId\" = '{f.OrderId:D}' AND \"Rating\" = 5;");
            Assert.Equal(1L, stillOriginal);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    // ── 4. everyone else sees nothing ───────────────────────────────────────

    [Fact]
    public async Task UnrelatedTenant_AndResetSession_SeeNothing()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedAsync();
        try
        {
            await using (var client = await OpenSessionAsync(f.ClientTenantId))
                await ExecAsync(client, InsertReviewSql(f), expectSuccess: true);

            await using (var other = await OpenSessionAsync(f.OtherTenantId))
            {
                Assert.Equal(0L, await ScalarAsync(other,
                    "SELECT count(*) FROM supplier_employee_reviews;"));
                Assert.Equal(0L, await ScalarAsync(other,
                    $"SELECT count(*) FROM supplier_employee_reviews WHERE \"OrderId\" = '{f.OrderId:D}';"));
            }

            await using var db = NewContext();
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
            await db.Database.ExecuteSqlRawAsync("RESET app.tenant_id; RESET app.role;");
            var onReset = await ScalarAsync(db, "SELECT count(*) FROM supplier_employee_reviews;");
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
    public async Task Table_HasSplitPolicies_ClientWrite_SupplierReadOnly()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var cmd = new NpgsqlCommand(
            @"SELECT policyname, cmd, qual, coalesce(with_check, '') FROM pg_policies
              WHERE schemaname = 'public' AND tablename = 'supplier_employee_reviews'
              ORDER BY policyname;", connection);

        var policies = new Dictionary<string, (string Cmd, string Qual, string WithCheck)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                policies[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2), reader.GetString(3));
        }

        Assert.True(policies.ContainsKey("tenant_isolation"));
        Assert.True(policies.ContainsKey("provider_bypass"));
        Assert.True(policies.ContainsKey("worker_bypass"));
        Assert.True(policies.ContainsKey("supplier_read"));

        // The write policy is keyed on the BUYER …
        var isolation = policies["tenant_isolation"];
        Assert.Equal("ALL", isolation.Cmd);
        Assert.Contains("ClientTenantId", isolation.Qual);
        Assert.Contains("NULLIF", isolation.Qual);
        Assert.DoesNotContain("IS NULL", isolation.Qual);           // fail-closed
        Assert.Contains("ClientTenantId", isolation.WithCheck);     // and it really is a write gate

        // … and the SUPPLIER gets read only, never a WITH CHECK.
        var supplierRead = policies["supplier_read"];
        Assert.Equal("SELECT", supplierRead.Cmd);
        Assert.Contains("SupplierTenantId", supplierRead.Qual);
        Assert.Contains("NULLIF", supplierRead.Qual);
        Assert.Equal(string.Empty, supplierRead.WithCheck);

        var forced = await ScalarAsync(db,
            "SELECT count(*) FROM pg_class WHERE relname = 'supplier_employee_reviews' " +
            "AND relrowsecurity AND relforcerowsecurity;");
        Assert.Equal(1L, forced);
    }

    // ── seed / session / cleanup ────────────────────────────────────────────

    private sealed record Fixture(
        Guid SupplierTenantId,
        Guid ClientTenantId,
        Guid OtherTenantId,
        Guid OrderId,
        Guid ManagerUserId,
        Guid BuyerUserId)
    {
        public Guid[] AllTenantIds => [SupplierTenantId, ClientTenantId, OtherTenantId];
    }

    private async Task<Fixture> SeedAsync()
    {
        await using var db = NewContext();

        var supplierTenant = Tenant.Create($"EmpReview Supplier {_run}", $"emprev-sup-{_run}");
        supplierTenant.UpdateBusinessType("supplier");
        var clientTenant = Tenant.Create($"EmpReview Client {_run}", $"emprev-cli-{_run}");
        var otherTenant = Tenant.Create($"EmpReview Other {_run}", $"emprev-oth-{_run}");
        db.Tenants.AddRange(supplierTenant, clientTenant, otherTenant);

        var agreement = new SupplierAgreement
        {
            SupplierTenantId = supplierTenant.Id,
            ClientTenantId = clientTenant.Id,
            Status = SupplierAgreementStatus.Active,
            ContractNumber = $"ДС-P8-{_run[..8]}",
        };
        db.SupplierAgreements.Add(agreement);

        var manager = User.Create(supplierTenant.Id, $"mgr-{_run}@s.com", "Петро Менеджер", "h", "supplier_admin");
        var buyer = User.Create(clientTenant.Id, $"buyer-{_run}@c.com", "Олена Замовниця", "h", "store_manager");
        db.Users.AddRange(manager, buyer);

        var order = new MarketplaceOrder
        {
            OrderNumber = $"MP-P8-{_run[..8]}",
            AgreementId = agreement.Id,
            SupplierTenantId = supplierTenant.Id,
            ClientTenantId = clientTenant.Id,
            Status = MarketplaceOrderStatus.Delivered,
            TotalAmount = 100m,
            ConfirmedByUserId = manager.Id,
            ConfirmedByUserName = "Петро Менеджер",
        };
        db.MarketplaceOrders.Add(order);

        await db.SaveChangesAsync();

        return new Fixture(
            supplierTenant.Id, clientTenant.Id, otherTenant.Id, order.Id, manager.Id, buyer.Id);
    }

    private static string InsertReviewSql(Fixture f, bool forgeAsSupplierAuthored = false)
    {
        // The supplier's forged attempt names ITSELF as the client author — the only shape that
        // could even try to satisfy a WITH CHECK, and it must still be refused (supplier_read is
        // SELECT-only, tenant_isolation's WITH CHECK wants ClientTenantId = session = supplier).
        var clientTenantId = forgeAsSupplierAuthored ? f.SupplierTenantId : f.ClientTenantId;
        return
            "INSERT INTO supplier_employee_reviews " +
            "(\"Id\", \"SupplierTenantId\", \"ClientTenantId\", \"SupplierUserId\", \"SupplierUserName\", " +
            " \"RatedByUserId\", \"Rating\", \"Source\", \"OrderId\", \"CreatedAt\", \"UpdatedAt\") VALUES " +
            $"(gen_random_uuid(), '{f.SupplierTenantId:D}', '{clientTenantId:D}', '{f.ManagerUserId:D}', " +
            $"'Петро Менеджер', '{f.BuyerUserId:D}', 5, 'order', '{f.OrderId:D}', now(), now());";
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
            $"DELETE FROM supplier_employee_reviews WHERE \"SupplierTenantId\" = ANY({f.AllTenantIds}) OR \"ClientTenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM marketplace_orders WHERE \"SupplierTenantId\" = ANY({f.AllTenantIds}) OR \"ClientTenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_agreements WHERE \"SupplierTenantId\" = ANY({f.AllTenantIds}) OR \"ClientTenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM users WHERE \"TenantId\" = ANY({f.AllTenantIds})");
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
