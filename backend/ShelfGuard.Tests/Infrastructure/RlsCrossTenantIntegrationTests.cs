using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Block 2 pre-launch DB audit (2026-07-14): a real-Postgres cross-tenant leak check that
/// exercises actual RLS policy enforcement, not just repository/service C# guards (see
/// <c>Users.UserServiceCrossTenantTests</c> for that layer, from Block 1). This repo has no
/// WebApplicationFactory / HTTP integration-test harness yet (tracked TODO from TASK-351), and
/// none of the existing <c>ShelfGuard.Tests</c> suite talks to a real database — everything else
/// uses EF InMemory or NSubstitute mocks, which cannot exercise PostgreSQL RLS at all (RLS is
/// evaluated by the Postgres engine, InMemory has no concept of it). So this test opens a real
/// connection to the local dev Postgres (docker-compose, port 5435) instead.
///
/// Simulates the exact attack the audit asked for: a caller authenticated as tenant A tries to
/// read tenant B's row by putting tenant B's id directly in the WHERE clause (as if a controller
/// had — bug notwithstanding — passed an attacker-supplied tenant/store id straight into a query).
/// RLS must return zero rows regardless of what the query asks for, because Postgres ANDs every
/// query's WHERE clause with the row-security policy.
///
/// The connecting DB user must not be a BYPASSRLS superuser (the dev docker-compose default `crm`
/// role is: superuser, RLS never applies to it) or the test would pass for the wrong reason. Since
/// the local dev role usually has CREATEROLE, the fixture creates a throwaway NOSUPERUSER
/// NOBYPASSRLS role for the duration of the connection via `SET ROLE` (assumed by a superuser,
/// so no separate login/password needed) rather than requiring the shelfguard_app prod role to
/// pre-exist locally.
///
/// Skips (soft-pass, does not fail the build) when no reachable Postgres is configured — this
/// project's CI (`.github/workflows/ci.yml`) has no Postgres service, so `dotnet test` in CI never
/// exercises this class; it is meant to run locally against `docker compose up -d postgres`.
/// Override the target via env var SHELFGUARD_TEST_DB_CONNECTION if needed.
/// </summary>
public sealed class RlsCrossTenantIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private NpgsqlConnection? _connection;
    private bool _dbAvailable;

    public RlsCrossTenantIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SHELFGUARD_TEST_DB_CONNECTION") ?? DefaultConnectionString;

        try
        {
            _connection = new NpgsqlConnection(connectionString);
            await _connection.OpenAsync();
            _dbAvailable = true;

            // Throwaway non-superuser role for this test run only — the fixture's connection
            // stays superuser so it can SET ROLE at will and clean up afterwards; the actual
            // RLS-under-test statements run with `SET ROLE` active, which drops BYPASSRLS for
            // the remainder of the session (RESET ROLE restores it).
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
                $"Skipping RLS integration tests — no reachable Postgres at '{connectionString}': {ex.Message}");
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
    /// Direct reproduction of the P0 found 2026-07-14: with app.tenant_id/app.role fully RESET
    /// (the state of every unauthenticated connection, and of any raw session before it sets
    /// those session vars), a non-superuser role must see ZERO rows on product_stock — not
    /// every row, which is what the fail-open policy used to return before
    /// 20260714180000_FixFailOpenTenantIsolationOnReset. Unlike the forged-filter tests above
    /// (which kept a valid app.tenant_id set and only forged the WHERE clause), this test
    /// exercises the RESET-state scenario specifically, since that's the one the forged-filter
    /// tests structurally couldn't catch.
    /// </summary>
    [Fact]
    public async Task ProductStock_FullyResetSession_ReturnsZeroRows_NotEveryRow()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        try
        {
            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync("RESET app.tenant_id; RESET app.role;");

            var visible = await ScalarAsync("SELECT count(*) FROM product_stock;");

            Assert.True(
                visible == 0,
                $"Expected 0 rows with app.tenant_id/app.role RESET, got {visible} — " +
                "tenant_isolation's fail-open branch may have regressed.");
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
        }
    }

    [Fact]
    public async Task Customers_CrossTenantForgedFilter_ReturnsZeroRows()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // customers.TenantId has a FK to tenants — create two throwaway tenant rows first.
        await ExecAsync(
            "INSERT INTO tenants (\"Id\", \"Name\", \"Slug\") VALUES " +
            "(@a, 'RLS Audit Tenant A', @slugA), (@b, 'RLS Audit Tenant B', @slugB);",
            ("a", tenantA), ("b", tenantB),
            ("slugA", $"rls-audit-a-{tenantA:N}"), ("slugB", $"rls-audit-b-{tenantB:N}"));

        await ExecAsync(
            "INSERT INTO customers (\"Id\", \"TenantId\", \"Name\", \"Tags\") VALUES " +
            "(gen_random_uuid(), @a, 'RLS-AUDIT tenant A customer', ARRAY[]::text[]), " +
            "(gen_random_uuid(), @b, 'RLS-AUDIT tenant B customer', ARRAY[]::text[]);",
            ("a", tenantA), ("b", tenantB));

        try
        {
            await ExecAsync("SET ROLE rls_audit_test_role;");
            // PostgreSQL's SET does not accept bind parameters — inline the value directly.
            // Safe here: tenantA is a server-generated Guid, not attacker input.
            await ExecAsync($"SET app.tenant_id = '{tenantA:D}'; SET app.role = 'store_manager';");

            // Own data must still be visible (sanity check RLS isn't accidentally blocking everything).
            var ownCount = await ScalarAsync(
                "SELECT count(*) FROM customers WHERE \"TenantId\" = @a;", ("a", tenantA));
            Assert.Equal(1L, ownCount);

            // Forged filter for tenant B's id, while connected as tenant A — RLS must still win.
            var leaked = await ScalarAsync(
                "SELECT count(*) FROM customers WHERE \"TenantId\" = @b;", ("b", tenantB));
            Assert.Equal(0L, leaked);

            // Even an unfiltered SELECT must not return tenant B's row.
            var totalVisible = await ScalarAsync("SELECT count(*) FROM customers;");
            Assert.Equal(1L, totalVisible);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await ExecAsync("DELETE FROM customers WHERE \"TenantId\" IN (@a, @b);", ("a", tenantA), ("b", tenantB));
            await ExecAsync("DELETE FROM tenants WHERE \"Id\" IN (@a, @b);", ("a", tenantA), ("b", tenantB));
        }
    }

    [Fact]
    public async Task NotificationQueue_CrossTenantForgedFilter_ReturnsZeroRows()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await ExecAsync(
            "INSERT INTO notification_queue (\"Id\", \"TenantId\", \"Channel\", \"Status\", \"RetryCount\", \"CreatedAt\") VALUES " +
            "(gen_random_uuid(), @a, 'telegram', 'pending', 0, now()), " +
            "(gen_random_uuid(), @b, 'telegram', 'pending', 0, now());",
            ("a", tenantA), ("b", tenantB));

        try
        {
            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync($"SET app.tenant_id = '{tenantA:D}'; SET app.role = 'store_manager';");

            var ownCount = await ScalarAsync(
                "SELECT count(*) FROM notification_queue WHERE \"TenantId\" = @a;", ("a", tenantA));
            Assert.Equal(1L, ownCount);

            var leaked = await ScalarAsync(
                "SELECT count(*) FROM notification_queue WHERE \"TenantId\" = @b;", ("b", tenantB));
            Assert.Equal(0L, leaked);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await ExecAsync(
                "DELETE FROM notification_queue WHERE \"TenantId\" IN (@a, @b);", ("a", tenantA), ("b", tenantB));
        }
    }

    /// <summary>
    /// Regression guard for the exact class of gap this audit found: two tables
    /// (customers, schedule_shifts, work_schedules, support_tickets, ticket_comments,
    /// chat_sessions — fixed in 20260714100000_FixMissingRlsGuardsAndProviderBypass) had their
    /// tenant policy named something other than the literal string 'tenant_isolation', so the
    /// 2026-06-29 bulk NULLIF-guard fix (which matched on that literal policy name) silently
    /// skipped them. Asserts, against whatever is actually applied to the DB right now, that
    /// every table with FORCE ROW LEVEL SECURITY has a policy literally named 'tenant_isolation'
    /// with a NULLIF guard, plus provider_bypass and worker_bypass — mirrors the manual
    /// pg_policies audit query run for this task so the gap can't quietly reappear.
    /// </summary>
    [Fact]
    public async Task AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        const string auditSql = @"
            WITH pol AS (
                SELECT tablename, policyname, qual
                FROM pg_policies
                WHERE schemaname = 'public'
            ),
            tables AS (
                SELECT c.relname AS tablename
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relkind = 'r'
                  AND c.relrowsecurity = true AND c.relforcerowsecurity = true
            )
            SELECT t.tablename
            FROM tables t
            LEFT JOIN pol p ON p.tablename = t.tablename
            GROUP BY t.tablename
            HAVING NOT (
                bool_or(p.policyname = 'tenant_isolation' AND p.qual ILIKE '%NULLIF%')
                AND bool_or(p.policyname = 'provider_bypass')
                AND bool_or(p.policyname = 'worker_bypass')
            )
            ORDER BY t.tablename;";

        await using var cmd = new NpgsqlCommand(auditSql, _connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        var offendingTables = new List<string>();
        while (await reader.ReadAsync())
            offendingTables.Add(reader.GetString(0));

        Assert.True(
            offendingTables.Count == 0,
            "FORCE RLS table(s) missing tenant_isolation(+NULLIF)/provider_bypass/worker_bypass: " +
            string.Join(", ", offendingTables));
    }

    /// <summary>
    /// Regression guard for the P0 found 2026-07-14 (Block 2 audit follow-up): the 2026-06-29
    /// bulk NULLIF-guard fixes prefixed `tenant_isolation` policies with
    /// `NULLIF(current_setting('app.tenant_id', true), '') IS NULL OR ...` — fail-OPEN, not
    /// fail-closed. When app.tenant_id is unset (RESET state — every unauthenticated
    /// connection, or any raw non-superuser session before it explicitly sets the session var),
    /// that branch evaluates true for every row, returning ALL tenants' data instead of zero.
    /// Reproduced live against a real NOSUPERUSER/NOBYPASSRLS role: `product_stock` returned
    /// every row (not zero) before the fix (20260714180000_FixFailOpenTenantIsolationOnReset).
    /// Fixed on 57 of the 60 affected tables; `users`/`refresh_tokens` intentionally keep the
    /// fail-open branch (load-bearing for login/token-refresh lookups that must find a record
    /// before the caller's tenant is known — see that migration's comment). `notification_settings`
    /// was originally grouped with those two on the same "pre-auth lookup" assumption, but
    /// TASK-360 (Block 9 audit, 2026-07-15) found and live-reproduced that this table has no
    /// actual pre-auth access path (both its reads/writes are behind `[Authorize]`, resolved
    /// from an already-validated JWT) — the fail-open branch was a real cross-tenant leak, not a
    /// necessary one, and was removed by
    /// 20260715120000_FixNotificationSettingsRlsFailOpen. This test asserts no OTHER table's
    /// tenant_isolation policy still has the dangerous prefix, so it can't quietly reappear
    /// (e.g. via a future bulk fix that reintroduces the pattern, as happened once already).
    /// `password_reset_tokens` (TASK-455, `AddPasswordResetTokens`) briefly joined the exception
    /// list as a third case, same shape/reason as `refresh_tokens`, but the table — and the
    /// link-based reset flow it backed — was dropped entirely by TASK-464 in favor of a
    /// temporary-password design that needs no pre-auth token lookup at all. Back to two
    /// documented exceptions.
    /// </summary>
    [Fact]
    public async Task TenantIsolationPolicies_HaveNoFailOpenBranch_ExceptDocumentedPreAuthLookups()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var allowedFailOpen = new HashSet<string> { "users", "refresh_tokens" };

        const string sql = @"
            SELECT tablename, qual FROM pg_policies
            WHERE schemaname = 'public' AND policyname = 'tenant_isolation'
              AND qual ILIKE '%IS NULL%OR%';";

        await using var cmd = new NpgsqlCommand(sql, _connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        var offending = new List<string>();
        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);
            if (!allowedFailOpen.Contains(table))
                offending.Add(table);
        }

        Assert.True(
            offending.Count == 0,
            "tenant_isolation polic(ies) with a fail-open IS-NULL-OR branch outside the " +
            "documented pre-auth-lookup exceptions (users/refresh_tokens): " +
            string.Join(", ", offending));
    }

    /// <summary>
    /// Direct regression test for the P0 fixed by
    /// 20260715120000_FixNotificationSettingsRlsFailOpen (TASK-360, Block 9 audit): with
    /// app.tenant_id/app.role fully RESET, a non-superuser role must see zero
    /// notification_settings rows — before the fix this table's tenant_isolation policy had a
    /// session-level fail-open branch that returned every tenant's rows in that state, live-
    /// reproduced during the audit by seeding rows for two different tenants' users and reading
    /// both back under a RESET session.
    /// </summary>
    [Fact]
    public async Task NotificationSettings_FullyResetSession_ReturnsZeroRows_NotEveryTenant()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await ExecAsync(
            "INSERT INTO tenants (\"Id\", \"Name\", \"Slug\") VALUES " +
            "(@a, 'RLS Audit Tenant A', @slugA), (@b, 'RLS Audit Tenant B', @slugB);",
            ("a", tenantA), ("b", tenantB),
            ("slugA", $"rls-audit-ns-a-{tenantA:N}"), ("slugB", $"rls-audit-ns-b-{tenantB:N}"));
        await ExecAsync(
            "INSERT INTO users (\"Id\", \"TenantId\", \"Email\", \"PasswordHash\", \"FullName\", \"Role\", \"IsActive\") VALUES " +
            "(@ua, @a, @emailA, 'x', 'RLS Audit User A', 'store_manager', true), " +
            "(@ub, @b, @emailB, 'x', 'RLS Audit User B', 'store_manager', true);",
            ("ua", userA), ("a", tenantA), ("emailA", $"rls-audit-ns-a-{tenantA:N}@test.local"),
            ("ub", userB), ("b", tenantB), ("emailB", $"rls-audit-ns-b-{tenantB:N}@test.local"));
        await ExecAsync(
            "INSERT INTO notification_settings (\"Id\", \"UserId\", \"EventType\", \"Channel\", \"IsEnabled\") VALUES " +
            "(gen_random_uuid(), @ua, 'stock.expiry_warning', 'telegram', true), " +
            "(gen_random_uuid(), @ub, 'stock.expiry_critical', 'email', false);",
            ("ua", userA), ("ub", userB));

        try
        {
            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync("RESET app.tenant_id; RESET app.role;");

            var visible = await ScalarAsync("SELECT count(*) FROM notification_settings;");
            Assert.True(
                visible == 0,
                $"Expected 0 rows with app.tenant_id/app.role RESET, got {visible} — " +
                "notification_settings' tenant_isolation fail-open branch may have regressed.");
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await ExecAsync(
                "DELETE FROM notification_settings WHERE \"UserId\" IN (@ua, @ub);", ("ua", userA), ("ub", userB));
            await ExecAsync("DELETE FROM users WHERE \"Id\" IN (@ua, @ub);", ("ua", userA), ("ub", userB));
            await ExecAsync("DELETE FROM tenants WHERE \"Id\" IN (@a, @b);", ("a", tenantA), ("b", tenantB));
        }
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
}
