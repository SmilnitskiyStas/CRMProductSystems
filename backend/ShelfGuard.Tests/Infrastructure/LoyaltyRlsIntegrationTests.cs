using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-404 (Loyalty Фаза 0): live-Postgres regression coverage for the new
/// <c>consumer_self_access</c> RLS policy on loyalty_memberships/loyalty_ledger_entries —
/// the first identity-based (not role-based) policy in this repository. Same connection/
/// skip/cleanup pattern as <see cref="RlsCrossTenantIntegrationTests"/> (real dev Postgres,
/// throwaway NOSUPERUSER NOBYPASSRLS role via SET ROLE, soft-skip when unreachable).
///
/// The scenario under test is structurally different from every other RLS check in this
/// repo: a consumer session never sets app.tenant_id at all (by design — see
/// TenantConnectionInterceptor remarks), so tenant_isolation alone would show it zero
/// rows everywhere. consumer_self_access is a second PERMISSIVE policy that ORs in
/// visibility keyed on app.consumer_account_id instead, letting one consumer read its own
/// rows across every tenant it holds a membership in — while a normal staff session (which
/// never sets app.consumer_account_id) must still see only its own tenant's rows exactly
/// as before, unaffected by this policy's mere presence.
///
/// TASK-553: part of the <c>TENANT_ISOLATION_TESTS</c> collection — <c>rls_audit_test_role</c> is
/// created once for the whole collection by <see cref="RlsAuditRoleFixture"/>, not by this class.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class LoyaltyRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private NpgsqlConnection? _connection;
    private bool _dbAvailable;

    public LoyaltyRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!_fixture.DbAvailable)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping Loyalty RLS integration tests — no reachable Postgres at '{_fixture.ConnectionString}': {_fixture.UnavailableReason}");
            return;
        }

        try
        {
            _connection = new NpgsqlConnection(_fixture.ConnectionString);
            await _connection.OpenAsync();
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping Loyalty RLS integration tests — no reachable Postgres at '{_fixture.ConnectionString}': {ex.Message}");
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

    [Fact]
    public async Task ConsumerSelfAccess_ReadsOwnMembershipsAcrossTenants_ButNotAnotherConsumers()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var consumer1 = Guid.NewGuid();
        var consumer2 = Guid.NewGuid();

        await SeedTenantsAndConsumersAsync(tenantA, tenantB, consumer1, consumer2);
        try
        {
            // consumer1 has a card in BOTH tenants; consumer2 only in tenant A.
            await ExecAsync(
                "INSERT INTO loyalty_memberships (\"Id\", \"TenantId\", \"ConsumerAccountId\", \"TotpSecret\") VALUES " +
                "(gen_random_uuid(), @a, @c1, 'SECRET1'), " +
                "(gen_random_uuid(), @b, @c1, 'SECRET2'), " +
                "(gen_random_uuid(), @a, @c2, 'SECRET3');",
                ("a", tenantA), ("b", tenantB), ("c1", consumer1), ("c2", consumer2));

            await ExecAsync("SET ROLE rls_audit_test_role;");
            // Consumer session shape: no app.tenant_id at all, only role+consumer_account_id.
            await ExecAsync("RESET app.tenant_id;");
            await ExecAsync($"SET app.role = 'consumer'; SET app.consumer_account_id = '{consumer1:D}';");

            var visibleCount = await ScalarAsync("SELECT count(*) FROM loyalty_memberships;");
            Assert.True(
                visibleCount == 2,
                $"Expected consumer1 to see exactly its own 2 memberships (across 2 tenants), got {visibleCount}.");

            var otherConsumerLeak = await ScalarAsync(
                "SELECT count(*) FROM loyalty_memberships WHERE \"ConsumerAccountId\" = @c2;", ("c2", consumer2));
            Assert.True(otherConsumerLeak == 0, "consumer1's session must not see consumer2's membership row.");
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await CleanupAsync(tenantA, tenantB, consumer1, consumer2);
        }
    }

    [Fact]
    public async Task ConsumerSelfAccess_LedgerEntries_ScopedThroughMembershipExistsSubquery()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var consumer1 = Guid.NewGuid();
        var consumer2 = Guid.NewGuid();

        await SeedTenantsAndConsumersAsync(tenantA, tenantB, consumer1, consumer2);

        var membership1 = Guid.NewGuid();
        var membership2 = Guid.NewGuid();
        try
        {
            await ExecAsync(
                "INSERT INTO loyalty_memberships (\"Id\", \"TenantId\", \"ConsumerAccountId\", \"TotpSecret\") VALUES " +
                "(@m1, @a, @c1, 'SECRET1'), (@m2, @a, @c2, 'SECRET2');",
                ("m1", membership1), ("a", tenantA), ("c1", consumer1), ("m2", membership2), ("c2", consumer2));

            // consumer1's membership gets 2 ledger rows, consumer2's gets 1.
            await ExecAsync(
                "INSERT INTO loyalty_ledger_entries (\"Id\", \"TenantId\", \"MembershipId\", \"EntryType\", \"Amount\", \"BalanceAfter\") VALUES " +
                "(gen_random_uuid(), @a, @m1, 'accrual', 10.00, 10.00), " +
                "(gen_random_uuid(), @a, @m1, 'redemption', -5.00, 5.00), " +
                "(gen_random_uuid(), @a, @m2, 'accrual', 20.00, 20.00);",
                ("a", tenantA), ("m1", membership1), ("m2", membership2));

            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync("RESET app.tenant_id;");
            await ExecAsync($"SET app.role = 'consumer'; SET app.consumer_account_id = '{consumer1:D}';");

            var visibleCount = await ScalarAsync("SELECT count(*) FROM loyalty_ledger_entries;");
            Assert.True(
                visibleCount == 2,
                $"Expected consumer1 to see exactly its own 2 ledger entries via the membership EXISTS subquery, got {visibleCount}.");
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await ExecAsync(
                "DELETE FROM loyalty_ledger_entries WHERE \"MembershipId\" IN (@m1, @m2);",
                ("m1", membership1), ("m2", membership2));
            await CleanupAsync(tenantA, tenantB, consumer1, consumer2);
        }
    }

    [Fact]
    public async Task StaffSession_StillOnlySeesOwnTenantMemberships_ConsumerSelfAccessDoesNotWiden()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var consumer1 = Guid.NewGuid();
        var consumer2 = Guid.NewGuid();

        await SeedTenantsAndConsumersAsync(tenantA, tenantB, consumer1, consumer2);
        try
        {
            // 2 rows in tenant A (one per consumer), 1 row in tenant B.
            await ExecAsync(
                "INSERT INTO loyalty_memberships (\"Id\", \"TenantId\", \"ConsumerAccountId\", \"TotpSecret\") VALUES " +
                "(gen_random_uuid(), @a, @c1, 'SECRET1'), " +
                "(gen_random_uuid(), @a, @c2, 'SECRET2'), " +
                "(gen_random_uuid(), @b, @c1, 'SECRET3');",
                ("a", tenantA), ("b", tenantB), ("c1", consumer1), ("c2", consumer2));

            await ExecAsync("SET ROLE rls_audit_test_role;");
            // Ordinary staff session shape: tenant_id + role set, consumer_account_id never
            // supplied by a staff JWT — TenantConnectionInterceptor falls back to the null
            // UUID, exactly as simulated by RESET here.
            await ExecAsync($"SET app.tenant_id = '{tenantA:D}'; SET app.role = 'store_manager';");
            await ExecAsync("RESET app.consumer_account_id;");

            var visibleCount = await ScalarAsync("SELECT count(*) FROM loyalty_memberships;");
            Assert.True(
                visibleCount == 2,
                $"Expected tenant A staff to see exactly tenant A's 2 memberships, got {visibleCount}.");

            var crossTenantLeak = await ScalarAsync(
                "SELECT count(*) FROM loyalty_memberships WHERE \"TenantId\" = @b;", ("b", tenantB));
            Assert.True(crossTenantLeak == 0, "Tenant A staff must not see tenant B's membership row.");
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await CleanupAsync(tenantA, tenantB, consumer1, consumer2);
        }
    }

    [Fact]
    public async Task LoyaltyMemberships_FullyResetSession_ReturnsZeroRows_NotEveryRow()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        // SeedTenantsAndConsumersAsync always seeds a pair of each — reuse both generated
        // ids here too (even though only tenantA/consumer1 get a membership row) so
        // CleanupAsync's matching pair-delete removes everything this helper created.
        // An earlier version of this test only deleted tenantA/consumer1 in its own
        // finally block, leaking one throwaway tenant + consumer_account row per run.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var consumer1 = Guid.NewGuid();
        var consumer2 = Guid.NewGuid();

        await SeedTenantsAndConsumersAsync(tenantA, tenantB, consumer1, consumer2);
        try
        {
            await ExecAsync(
                "INSERT INTO loyalty_memberships (\"Id\", \"TenantId\", \"ConsumerAccountId\", \"TotpSecret\") VALUES " +
                "(gen_random_uuid(), @a, @c1, 'SECRET1');",
                ("a", tenantA), ("c1", consumer1));

            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync("RESET app.tenant_id; RESET app.role; RESET app.consumer_account_id;");

            var visible = await ScalarAsync("SELECT count(*) FROM loyalty_memberships;");
            Assert.True(
                visible == 0,
                $"Expected 0 rows with every session var RESET, got {visible} — " +
                "tenant_isolation and/or consumer_self_access may have a fail-open branch.");
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await CleanupAsync(tenantA, tenantB, consumer1, consumer2);
        }
    }

    private async Task SeedTenantsAndConsumersAsync(Guid tenantA, Guid tenantB, Guid consumer1, Guid consumer2)
    {
        await ExecAsync(
            "INSERT INTO tenants (\"Id\", \"Name\", \"Slug\") VALUES " +
            "(@a, 'Loyalty RLS Audit Tenant A', @slugA), (@b, 'Loyalty RLS Audit Tenant B', @slugB);",
            ("a", tenantA), ("b", tenantB),
            ("slugA", $"loyalty-rls-audit-a-{tenantA:N}"), ("slugB", $"loyalty-rls-audit-b-{tenantB:N}"));
        await ExecAsync(
            "INSERT INTO consumer_accounts (\"Id\", \"Phone\", \"PasswordHash\", \"FullName\") VALUES " +
            "(@c1, @phone1, 'x', 'RLS Audit Consumer 1'), (@c2, @phone2, 'x', 'RLS Audit Consumer 2');",
            ("c1", consumer1), ("phone1", $"+380{consumer1:N}".Substring(0, 13)),
            ("c2", consumer2), ("phone2", $"+380{consumer2:N}".Substring(0, 13)));
    }

    private async Task CleanupAsync(Guid tenantA, Guid tenantB, Guid consumer1, Guid consumer2)
    {
        await ExecAsync(
            "DELETE FROM loyalty_memberships WHERE \"TenantId\" IN (@a, @b);", ("a", tenantA), ("b", tenantB));
        await ExecAsync(
            "DELETE FROM consumer_accounts WHERE \"Id\" IN (@c1, @c2);", ("c1", consumer1), ("c2", consumer2));
        await ExecAsync("DELETE FROM tenants WHERE \"Id\" IN (@a, @b);", ("a", tenantA), ("b", tenantB));
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
