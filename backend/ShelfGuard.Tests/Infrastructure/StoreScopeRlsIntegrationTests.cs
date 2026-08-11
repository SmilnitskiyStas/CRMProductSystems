using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-393 Stage 3 (Feature 2 — store-scoped data visibility, enforcement cutover):
/// live-Postgres regression tests for the RESTRICTIVE <c>store_scope</c> policy added by
/// <c>20260719193545_AddLocationStoreScopeRlsPolicies</c> on product_stock/daily_sales/
/// pos_shifts/pos_transactions/write_offs/discounts/stock_receipts/stock_movements/
/// stock_transfers.
///
/// Same real-Postgres approach as <see cref="RlsCrossTenantIntegrationTests"/> (this project
/// has no WebApplicationFactory/HTTP integration harness — see that class's remarks) —
/// connects to the local dev Postgres (docker-compose, port 5435), runs the actual RLS-under-
/// test statements through a throwaway NOSUPERUSER/NOBYPASSRLS role via <c>SET ROLE</c>, and
/// soft-skips (does not fail the build) when no reachable Postgres is configured. Not exercised
/// by CI today (no Postgres service in `.github/workflows/ci.yml`) — meant to run locally
/// against `docker compose up -d postgres`.
///
/// ⚠️ These tests exercise the migration directly against whatever schema state the local dev
/// DB is currently in. If <c>20260719193545_AddLocationStoreScopeRlsPolicies</c> has not been
/// applied yet (or was rolled back), every scoping test below will fail loudly — that is the
/// intended signal, not a flaky test.
/// </summary>
public sealed class StoreScopeRlsIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private NpgsqlConnection? _connection;
    private bool _dbAvailable;

    public StoreScopeRlsIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SHELFGUARD_TEST_DB_CONNECTION") ?? DefaultConnectionString;

        try
        {
            _connection = new NpgsqlConnection(connectionString);
            await _connection.OpenAsync();
            _dbAvailable = true;

            await ExecAsync(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'rls_audit_test_role') THEN
                        CREATE ROLE rls_audit_test_role NOSUPERUSER NOBYPASSRLS;
                    END IF;
                END $$;
                GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO rls_audit_test_role;
            ");
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping store_scope RLS integration tests — no reachable Postgres at '{connectionString}': {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
        {
            if (_connection.State == System.Data.ConnectionState.Open)
                await ExecAsync("RESET ROLE;");
            await _connection.DisposeAsync();
        }
    }

    /// <summary>
    /// Schema-completeness guard: every one of the 9 tables this migration targets must have a
    /// policy literally named 'store_scope' with permissive = 'RESTRICTIVE' — not PERMISSIVE.
    /// A PERMISSIVE store_scope would be silently defeated (OR'd away) by the existing
    /// permissive tenant_isolation policy instead of narrowing it, which is the exact mistake
    /// this test exists to catch if a future edit ever drops the AS RESTRICTIVE keyword.
    /// </summary>
    [Fact]
    public async Task AllNineScopedTables_HaveRestrictiveStoreScopePolicy()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var expectedTables = new[]
        {
            "product_stock", "daily_sales", "pos_shifts", "pos_transactions",
            "write_offs", "discounts", "stock_receipts", "stock_movements", "stock_transfers",
        };

        const string sql = @"
            SELECT tablename, permissive FROM pg_policies WHERE policyname = 'store_scope';";

        var found = new Dictionary<string, string>();
        await using (var cmd = new NpgsqlCommand(sql, _connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                found[reader.GetString(0)] = reader.GetString(1);
        }

        foreach (var table in expectedTables)
        {
            Assert.True(found.ContainsKey(table), $"Missing store_scope policy on '{table}'.");
            Assert.Equal("RESTRICTIVE", found[table]);
        }
        Assert.Equal(expectedTables.Length, found.Count);
    }

    /// <summary>
    /// enterprise_admin gets ZERO rows in user_locations by design (Stage 1) — must still see
    /// every location's stock via the app.role bypass branch, not the EXISTS branch.
    /// </summary>
    [Fact]
    public async Task EnterpriseAdmin_SeesAllLocationsStock_RegardlessOfUserLocations()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var f = await SeedScenarioAsync();
        try
        {
            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync(
                $"SET app.tenant_id = '{f.TenantId:D}'; SET app.role = 'enterprise_admin'; " +
                $"SET app.user_id = '{f.AdminUserId:D}';");

            var visible = await ScalarAsync(
                "SELECT count(*) FROM product_stock WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            Assert.Equal(2L, visible);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await CleanupAsync(f);
        }
    }

    /// <summary>
    /// A store_manager assigned to exactly one location must see only that location's stock —
    /// the other location's row (same tenant) must not be visible.
    /// </summary>
    [Fact]
    public async Task ScopedRole_WithOneLocationAssignment_SeesOnlyThatLocationsStock()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var f = await SeedScenarioAsync();
        try
        {
            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync(
                $"SET app.tenant_id = '{f.TenantId:D}'; SET app.role = 'store_manager'; " +
                $"SET app.user_id = '{f.ManagerXUserId:D}';");

            var visible = await ScalarAsync(
                "SELECT count(*) FROM product_stock WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            Assert.Equal(1L, visible);

            var onlyLocation = await ScalarGuidAsync(
                "SELECT \"LocationId\" FROM product_stock WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            Assert.Equal(f.LocationX, onlyLocation);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await CleanupAsync(f);
        }
    }

    /// <summary>
    /// Fail-closed, not a bug: a scoped-rank user with zero user_locations rows sees zero rows,
    /// confirmed acceptable end-state by the product owner (see the rollout checklist —
    /// this is exactly the state Stage 2's backfill must eliminate before Stage 3 ships to prod).
    /// </summary>
    [Fact]
    public async Task ScopedRole_WithNoLocationAssignments_SeesZeroRows()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var f = await SeedScenarioAsync();
        try
        {
            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync(
                $"SET app.tenant_id = '{f.TenantId:D}'; SET app.role = 'store_manager'; " +
                $"SET app.user_id = '{f.ManagerNoneUserId:D}';");

            var visible = await ScalarAsync(
                "SELECT count(*) FROM product_stock WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            Assert.Equal(0L, visible);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await CleanupAsync(f);
        }
    }

    /// <summary>
    /// Two-sided table (stock_transfers has FromLocationId/ToLocationId, both NOT NULL): a user
    /// assigned to location X must see a transfer if X is EITHER side, and must not see a
    /// transfer where neither side is X.
    /// </summary>
    [Fact]
    public async Task TwoSidedTable_StockTransfers_MatchesEitherFromOrToLocation()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var f = await SeedScenarioAsync();
        try
        {
            await ExecAsync(
                "INSERT INTO stock_transfers (\"Id\", \"TenantId\", \"FromLocationId\", \"ToLocationId\") VALUES " +
                "(gen_random_uuid(), @t, @x, @y), " +   // X -> Y: visible (From matches)
                "(gen_random_uuid(), @t, @y, @x), " +   // Y -> X: visible (To matches)
                "(gen_random_uuid(), @t, @y, @z);",     // Y -> Z: NOT visible (neither side is X)
                ("t", f.TenantId), ("x", f.LocationX), ("y", f.LocationY), ("z", f.LocationZ));

            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync(
                $"SET app.tenant_id = '{f.TenantId:D}'; SET app.role = 'store_manager'; " +
                $"SET app.user_id = '{f.ManagerXUserId:D}';");

            var visible = await ScalarAsync(
                "SELECT count(*) FROM stock_transfers WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            Assert.Equal(2L, visible);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await ExecAsync("DELETE FROM stock_transfers WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            await CleanupAsync(f);
        }
    }

    /// <summary>
    /// 'provider'/'worker' come directly from the brief. 'provider_admin' is a deliberate
    /// addition made during implementation (not literally in the brief's SQL shape) to avoid
    /// regressing the already-established provider_admin/provider parity from
    /// 20260714150000_ExpandProviderBypassToProviderAdmin, which already grants provider_admin
    /// full bypass on all 9 of these tables via the pre-existing permissive provider_bypass
    /// policy — omitting it here would have silently revoked that on this specific set of
    /// tables the moment this migration applied. All three must see every row with ZERO
    /// user_locations rows for the acting user, proving the bypass branch (not the EXISTS
    /// branch) is what's firing.
    /// </summary>
    [Theory]
    [InlineData("provider")]
    [InlineData("provider_admin")]
    [InlineData("worker")]
    public async Task BypassRoles_SeeAllLocationsStock_EvenWithoutAnyUserLocationsRow(string role)
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var f = await SeedScenarioAsync();
        try
        {
            await ExecAsync("SET ROLE rls_audit_test_role;");
            // Deliberately use the unassigned manager's id — zero user_locations rows for it —
            // to prove the bypass branch, not a coincidental EXISTS match, is what's firing.
            await ExecAsync(
                $"SET app.tenant_id = '{f.TenantId:D}'; SET app.role = '{role}'; " +
                $"SET app.user_id = '{f.ManagerNoneUserId:D}';");

            var visible = await ScalarAsync(
                "SELECT count(*) FROM product_stock WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            Assert.Equal(2L, visible);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await CleanupAsync(f);
        }
    }

    /// <summary>
    /// Sanity check that store_scope ANDs on top of tenant_isolation rather than replacing it:
    /// a correctly-assigned user querying under the WRONG app.tenant_id must still see zero rows
    /// of the real tenant's data — the EXISTS branch matching UserId+LocationId is not, by
    /// itself, sufficient to leak across tenants (user_locations' own tenant_isolation policy is
    /// what closes that gap, transitively, inside the EXISTS subquery).
    /// </summary>
    [Fact]
    public async Task ScopedRole_CorrectLocationButWrongTenantContext_SeesZeroRows()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var f = await SeedScenarioAsync();
        try
        {
            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync(
                "SET app.tenant_id = '99999999-9999-9999-9999-999999999999'; " +
                $"SET app.role = 'store_manager'; SET app.user_id = '{f.ManagerXUserId:D}';");

            var visible = await ScalarAsync(
                "SELECT count(*) FROM product_stock WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            Assert.Equal(0L, visible);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await CleanupAsync(f);
        }
    }

    /// <summary>
    /// TASK-508/KI-033 (ADR-028): 'marketing_analytics_bypass' is a fifth bypass value added to
    /// pos_transactions' store_scope policy only (20260811110212_
    /// AddMarketingAnalyticsBypassToPosTransactionsStoreScope) — no real JWT role claim can ever
    /// produce it (see TenantConnectionInterceptorTests' companion assertion), it is set only by
    /// AnalyticsRlsOverride's own hardcoded SET LOCAL string. This test sets it directly, the
    /// same way the existing bypass-role tests above do, to prove a pos_transactions row that a
    /// user_locations-scoped session cannot see becomes visible under the bypass — without
    /// touching product_stock or any of the other 8 store_scope-governed tables, which
    /// deliberately do NOT get this bypass value.
    /// </summary>
    [Fact]
    public async Task MarketingAnalyticsBypassRole_SeesAllLocationsPosTransactions_EvenWithoutAnyUserLocationsRow()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var f = await SeedScenarioAsync();
        try
        {
            await ExecAsync(
                "INSERT INTO pos_transactions (\"Id\", \"TenantId\", \"LocationId\", \"ReceiptNumber\", \"TotalAmount\", \"TaxAmount\", \"Status\", \"CreatedAt\") VALUES " +
                "(gen_random_uuid(), @t, @x, 'RLS-BYPASS-X', 10, 0, 'completed', now()), " +
                "(gen_random_uuid(), @t, @y, 'RLS-BYPASS-Y', 20, 0, 'completed', now());",
                ("t", f.TenantId), ("x", f.LocationX), ("y", f.LocationY));

            await ExecAsync("SET ROLE rls_audit_test_role;");
            // Deliberately use the unassigned manager's id — zero user_locations rows for it —
            // to prove the bypass branch, not a coincidental EXISTS match, is what's firing.
            await ExecAsync(
                $"SET app.tenant_id = '{f.TenantId:D}'; SET app.role = 'marketing_analytics_bypass'; " +
                $"SET app.user_id = '{f.ManagerNoneUserId:D}';");

            var visible = await ScalarAsync(
                "SELECT count(*) FROM pos_transactions WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            Assert.Equal(2L, visible);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await ExecAsync("DELETE FROM pos_transactions WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            await CleanupAsync(f);
        }
    }

    /// <summary>
    /// Same fixture/manager as above, but WITHOUT the bypass role set — a plain store_manager
    /// scoped only to LocationX must still see zero of the two seeded pos_transactions rows
    /// (LocationX/LocationY are not among their user_locations rows), confirming
    /// 'marketing_analytics_bypass' — not some accidental widening of store_scope itself — is
    /// what made the row visible above.
    /// </summary>
    [Fact]
    public async Task ScopedRole_WithoutBypass_CannotSeePosTransactions_OutsideOwnLocation()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var f = await SeedScenarioAsync();
        try
        {
            await ExecAsync(
                "INSERT INTO pos_transactions (\"Id\", \"TenantId\", \"LocationId\", \"ReceiptNumber\", \"TotalAmount\", \"TaxAmount\", \"Status\", \"CreatedAt\") VALUES " +
                "(gen_random_uuid(), @t, @y, 'RLS-NOBYPASS-Y', 20, 0, 'completed', now());",
                ("t", f.TenantId), ("y", f.LocationY));

            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync(
                $"SET app.tenant_id = '{f.TenantId:D}'; SET app.role = 'store_manager'; " +
                $"SET app.user_id = '{f.ManagerXUserId:D}';");

            var visible = await ScalarAsync(
                "SELECT count(*) FROM pos_transactions WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            Assert.Equal(0L, visible);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await ExecAsync("DELETE FROM pos_transactions WHERE \"TenantId\" = @t;", ("t", f.TenantId));
            await CleanupAsync(f);
        }
    }

    // ── Shared fixture setup ────────────────────────────────────────────────────

    private sealed record Scenario(
        Guid TenantId, Guid LocationX, Guid LocationY, Guid LocationZ, Guid ItemId,
        Guid AdminUserId, Guid ManagerXUserId, Guid ManagerNoneUserId);

    /// <summary>
    /// One tenant, 3 locations (X/Y/Z), 1 item, 2 product_stock rows (X and Y), 3 users
    /// (enterprise_admin with no assignment, store_manager assigned to X only, store_manager
    /// with no assignment at all). Runs as the fixture's superuser connection (RLS doesn't
    /// apply yet — SET ROLE happens after this, per test).
    /// </summary>
    private async Task<Scenario> SeedScenarioAsync()
    {
        var tenantId = Guid.NewGuid();
        var locX = Guid.NewGuid();
        var locY = Guid.NewGuid();
        var locZ = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var adminUserId = Guid.NewGuid();
        var mgrXUserId = Guid.NewGuid();
        var mgrNoneUserId = Guid.NewGuid();

        await ExecAsync(
            "INSERT INTO tenants (\"Id\", \"Name\", \"Slug\") VALUES (@t, 'Store Scope Test Tenant', @slug);",
            ("t", tenantId), ("slug", $"store-scope-test-{tenantId:N}"));

        await ExecAsync(
            "INSERT INTO locations (\"Id\", \"TenantId\", \"Name\", \"Type\") VALUES " +
            "(@x, @t, 'Store X', 'shop'), (@y, @t, 'Store Y', 'shop'), (@z, @t, 'Store Z', 'shop');",
            ("x", locX), ("y", locY), ("z", locZ), ("t", tenantId));

        await ExecAsync(
            "INSERT INTO items (\"Id\", \"TenantId\", \"Name\", \"MinStock\", \"MaxStock\", \"SafetyBuffer\") " +
            "VALUES (@i, @t, 'Store Scope Test Item', 0, 0, 0);",
            ("i", itemId), ("t", tenantId));

        await ExecAsync(
            "INSERT INTO users (\"Id\", \"TenantId\", \"Email\", \"PasswordHash\", \"FullName\", \"Role\", \"IsActive\") VALUES " +
            "(@admin, @t, @adminEmail, 'x', 'Test Admin', 'enterprise_admin', true), " +
            "(@mgrx, @t, @mgrxEmail, 'x', 'Test MgrX', 'store_manager', true), " +
            "(@mgrnone, @t, @mgrnoneEmail, 'x', 'Test MgrNone', 'store_manager', true);",
            ("admin", adminUserId), ("mgrx", mgrXUserId), ("mgrnone", mgrNoneUserId), ("t", tenantId),
            ("adminEmail", $"store-scope-admin-{tenantId:N}@test.local"),
            ("mgrxEmail", $"store-scope-mgrx-{tenantId:N}@test.local"),
            ("mgrnoneEmail", $"store-scope-mgrnone-{tenantId:N}@test.local"));

        // Only ManagerX gets a user_locations row (Store X). enterprise_admin and
        // ManagerNone deliberately get none.
        await ExecAsync(
            "INSERT INTO user_locations (\"Id\", \"TenantId\", \"UserId\", \"LocationId\") VALUES " +
            "(gen_random_uuid(), @t, @mgrx, @x);",
            ("t", tenantId), ("mgrx", mgrXUserId), ("x", locX));

        await ExecAsync(
            "INSERT INTO product_stock (\"Id\", \"TenantId\", \"ProductId\", \"LocationId\", \"Quantity\", \"QuantityInitial\", \"ExpiryDate\") VALUES " +
            "(gen_random_uuid(), @t, @item, @x, 10, 10, '2026-12-31'), " +
            "(gen_random_uuid(), @t, @item, @y, 20, 20, '2026-12-31');",
            ("t", tenantId), ("item", itemId), ("x", locX), ("y", locY));

        return new Scenario(tenantId, locX, locY, locZ, itemId, adminUserId, mgrXUserId, mgrNoneUserId);
    }

    private async Task CleanupAsync(Scenario f)
    {
        // FK-safe order: children before parents. Runs on the fixture's superuser connection
        // (already RESET ROLE by the caller), so RLS never blocks the cleanup itself.
        await ExecAsync("DELETE FROM product_stock WHERE \"TenantId\" = @t;", ("t", f.TenantId));
        await ExecAsync("DELETE FROM user_locations WHERE \"TenantId\" = @t;", ("t", f.TenantId));
        await ExecAsync("DELETE FROM users WHERE \"TenantId\" = @t;", ("t", f.TenantId));
        await ExecAsync("DELETE FROM items WHERE \"TenantId\" = @t;", ("t", f.TenantId));
        await ExecAsync("DELETE FROM locations WHERE \"TenantId\" = @t;", ("t", f.TenantId));
        await ExecAsync("DELETE FROM tenants WHERE \"Id\" = @t;", ("t", f.TenantId));
    }

    private async Task ExecAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, _connection);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, _connection);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private async Task<Guid> ScalarGuidAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, _connection);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync();
        return (Guid)result!;
    }
}
