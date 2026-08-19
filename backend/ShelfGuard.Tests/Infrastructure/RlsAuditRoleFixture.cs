using Npgsql;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-553: shared xUnit collection fixture that creates the throwaway
/// <c>rls_audit_test_role</c> exactly ONCE for the whole <c>TENANT_ISOLATION_TESTS</c> collection,
/// instead of each RLS-bearing test class racing to create/grant it independently inside its own
/// <c>IAsyncLifetime.InitializeAsync</c>.
///
/// Root cause this replaces: Postgres roles are cluster-wide (not per-database, not per-test-run),
/// and xUnit runs different test classes' <c>IAsyncLifetime</c> fixtures in parallel by default
/// (each class with no explicit <c>[Collection]</c> gets its own implicit collection, and implicit
/// collections run concurrently). Before this fixture, 7 classes
/// (<see cref="RlsCrossTenantIntegrationTests"/>, <see cref="LoyaltyRlsIntegrationTests"/>,
/// <see cref="LoyaltyJoinRlsIntegrationTests"/>, <see cref="MobileConfigDraftServiceRlsIntegrationTests"/>,
/// <see cref="MobileConfigPublishedReadRlsIntegrationTests"/>,
/// <see cref="MobileThemeServiceRlsIntegrationTests"/>, <see cref="StoreScopeRlsIntegrationTests"/>)
/// each ran the identical non-atomic "check pg_roles, CREATE ROLE IF NOT EXISTS, GRANT ALL
/// PRIVILEGES" sequence against the same shared Postgres cluster. Two of these racing concurrently
/// threw either 23505 (duplicate key on pg_authid — the check-then-create TOCTOU race) or XX000
/// ("tuple concurrently updated" — concurrent catalog DDL from the GRANT). Every affected class's
/// broad <c>catch (Exception)</c> misattributed that as "Postgres unreachable" and silently
/// soft-skipped that class's entire assertion set for the run — reproduced 2/2 locally with a
/// different random subset of the 7 classes skipped each time (see
/// .claude/logs/tasks/553_2026-08-18_ci-postgres-for-rls-tests_devops-engineer.md and
/// .claude/logs/tasks/553_2026-08-18_tenant-isolation-suite-consolidation_backend-developer.md).
///
/// Fix shape: tag all 7 classes with <c>[Collection("TENANT_ISOLATION_TESTS")]</c>
/// (see <see cref="TenantIsolationTestsCollection"/> below) and let THIS fixture own role creation.
/// Two xUnit guarantees make this airtight, not just "less likely to race":
///   1. A collection fixture's <c>InitializeAsync</c> runs exactly ONCE per test run, before any
///      test in the collection executes — never once per test class or per test method.
///   2. Test classes that share one <c>[Collection]</c> never run in parallel with each other
///      (though the collection as a whole may still run in parallel with OTHER collections) — so
///      even the individual classes' own per-test-method connection setup in their
///      <c>InitializeAsync</c> is serialized relative to one another.
/// Together these mean the CREATE ROLE/GRANT DDL now runs from exactly one place, exactly once,
/// with nothing else touching pg_roles concurrently — structurally eliminating the race rather
/// than just narrowing the window (a lock/retry around the old duplicated logic would have been
/// the narrowing approach; this is the consolidation the task asked for instead).
/// </summary>
public sealed class RlsAuditRoleFixture : IAsyncLifetime
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    /// <summary>Resolved once for the whole collection — same override convention every RLS test
    /// class already used individually (<c>SHELFGUARD_TEST_DB_CONNECTION</c>).</summary>
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("SHELFGUARD_TEST_DB_CONNECTION") ?? DefaultConnectionString;

    /// <summary>True once <c>rls_audit_test_role</c> has been created/granted against a reachable
    /// Postgres. Every member class's own <c>InitializeAsync</c> should soft-skip immediately when
    /// this is false, exactly as each used to do independently on its own connection failure.</summary>
    public bool DbAvailable { get; private set; }

    /// <summary>Set when <see cref="DbAvailable"/> is false — the exception message from the one
    /// connection/DDL attempt this fixture made, for member classes' skip log lines.</summary>
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                @"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'rls_audit_test_role') THEN
                        CREATE ROLE rls_audit_test_role NOSUPERUSER NOBYPASSRLS;
                    END IF;
                END $$;
                GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO rls_audit_test_role;",
                connection);
            await cmd.ExecuteNonQueryAsync();

            DbAvailable = true;
        }
        catch (Exception ex)
        {
            DbAvailable = false;
            UnavailableReason = ex.Message;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Collection definition for <c>TENANT_ISOLATION_TESTS</c> — the single named, CI-gating RLS/
/// tenant-isolation suite TASK-553 requires. The name IS the suite: every class tagged
/// <c>[Collection("TENANT_ISOLATION_TESTS")]</c> below is, by construction, part of it and runs
/// serially relative to the others, sharing the one <see cref="RlsAuditRoleFixture"/> instance.
/// This class carries no logic — xUnit only needs the attribute pairing on a marker type.
/// </summary>
[CollectionDefinition("TENANT_ISOLATION_TESTS")]
public sealed class TenantIsolationTestsCollection : ICollectionFixture<RlsAuditRoleFixture>
{
}
