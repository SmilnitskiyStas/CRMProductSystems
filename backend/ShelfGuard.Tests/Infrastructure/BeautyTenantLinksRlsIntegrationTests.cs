using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Infrastructure.Data;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// 20261105090000_AddBeautyTenantLinksRls: <c>beauty_tenant_links</c> (Beauty control plane, ADR-018) has a
/// TenantId and now carries the standard triad. Proved against a REAL Postgres under the genuine
/// <c>rls_audit_test_role</c> (NOSUPERUSER NOBYPASSRLS), never InMemory. The generic triad audit in
/// <see cref="RlsCrossTenantIntegrationTests"/> picks the table up automatically (no profile exception).
/// Legit provider access (ProviderOnly endpoints run with app.role = 'provider') must keep working.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class BeautyTenantLinksRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private bool _dbAvailable;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    private readonly string _run = Guid.NewGuid().ToString("N");
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public BeautyTenantLinksRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!_fixture.DbAvailable)
        {
            _output.WriteLine($"Skipping beauty_tenant_links RLS tests - no reachable Postgres: {_fixture.UnavailableReason}");
            return;
        }

        _dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).EnableDynamicJson().Build();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dataSource)
            .IgnoreManyServiceProvidersWarning()
            .Options;
        _dbAvailable = true;

        // Seed as the (superuser) fixture connection, which is not subject to RLS.
        await using var db = NewContext();
        await db.Database.ExecuteSqlRawAsync(InsertSql(_tenantA, $"rls-a-{_run}"));
        await db.Database.ExecuteSqlRawAsync(InsertSql(_tenantB, $"rls-b-{_run}"));
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is null) return;
        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM beauty_tenant_links WHERE \"TenantId\" = ANY({new[] { _tenantA, _tenantB }})");
        }
        await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task TenantSession_SeesOnlyOwnLink_NotOtherTenants()
    {
        if (!_dbAvailable) return;

        await using var a = await OpenSessionAsync(_tenantA, "enterprise_admin");
        Assert.Equal(1L, await ScalarAsync(a, $"SELECT count(*) FROM beauty_tenant_links WHERE \"BeautySlug\" IN ('rls-a-{_run}','rls-b-{_run}');"));
        Assert.Equal(1L, await ScalarAsync(a, $"SELECT count(*) FROM beauty_tenant_links WHERE \"BeautySlug\" = 'rls-a-{_run}';"));
        Assert.Equal(0L, await ScalarAsync(a, $"SELECT count(*) FROM beauty_tenant_links WHERE \"BeautySlug\" = 'rls-b-{_run}';"));
    }

    [Fact]
    public async Task TenantSession_CannotWriteOtherTenantsLink()
    {
        if (!_dbAvailable) return;

        await using var a = await OpenSessionAsync(_tenantA, "enterprise_admin");

        // UPDATE/DELETE of another tenant's row: invisible -> zero rows affected.
        Assert.Equal(0, await ExecAsync(a, $"UPDATE beauty_tenant_links SET \"BeautyStatus\" = 'suspended' WHERE \"BeautySlug\" = 'rls-b-{_run}';"));
        Assert.Equal(0, await ExecAsync(a, $"DELETE FROM beauty_tenant_links WHERE \"BeautySlug\" = 'rls-b-{_run}';"));

        // INSERT for another tenant: WITH CHECK violation (42501).
        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => ExecAsync(a, InsertSql(Guid.NewGuid(), $"rls-forged-{_run}")));
        Assert.Equal("42501", ex.SqlState);

        await using var verify = NewContext();
        Assert.Equal("active", await StatusAsync(verify, $"rls-b-{_run}"));
    }

    [Fact]
    public async Task ProviderSession_ReadsAndWritesAcrossTenants()
    {
        if (!_dbAvailable) return;

        // Mirrors a ProviderOnly request: provider JWT has no tenant_id claim (null-uuid) and app.role = 'provider'.
        await using var p = await OpenSessionAsync(Guid.Empty, "provider");
        Assert.Equal(2L, await ScalarAsync(p, $"SELECT count(*) FROM beauty_tenant_links WHERE \"BeautySlug\" IN ('rls-a-{_run}','rls-b-{_run}');"));

        // Sync/Suspend path: update a link of any tenant.
        Assert.Equal(1, await ExecAsync(p, $"UPDATE beauty_tenant_links SET \"BeautyStatus\" = 'suspended' WHERE \"BeautySlug\" = 'rls-b-{_run}';"));

        // Create path: insert a link for a tenant that is not the session's tenant.
        var newTenant = Guid.NewGuid();
        try
        {
            Assert.Equal(1, await ExecAsync(p, InsertSql(newTenant, $"rls-new-{_run}")));
        }
        finally
        {
            await using var db = NewContext();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM beauty_tenant_links WHERE \"TenantId\" = {newTenant}");
        }
    }

    [Fact]
    public async Task WorkerSession_SeesAllLinks()
    {
        if (!_dbAvailable) return;

        await using var w = await OpenSessionAsync(Guid.Empty, "worker");
        Assert.Equal(2L, await ScalarAsync(w, $"SELECT count(*) FROM beauty_tenant_links WHERE \"BeautySlug\" IN ('rls-a-{_run}','rls-b-{_run}');"));
    }

    [Fact]
    public async Task ResetSession_AndNonProviderRole_SeeNothing()
    {
        if (!_dbAvailable) return;

        // Inbound HMAC path / any unauthenticated connection: tenant and role reset -> fail-closed.
        await using (var db = NewContext())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
            await db.Database.ExecuteSqlRawAsync("RESET app.tenant_id; RESET app.role;");
            Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM beauty_tenant_links;"));
            await db.Database.ExecuteSqlRawAsync("RESET ROLE;");
        }

        // provider_admin is not 'provider': no cross-tenant bypass (ProviderOnly = 'provider' only).
        await using var pa = await OpenSessionAsync(Guid.Empty, "provider_admin");
        Assert.Equal(0L, await ScalarAsync(pa, "SELECT count(*) FROM beauty_tenant_links;"));
    }

    [Fact]
    public async Task Table_IsForceRls_WithTriadPolicies()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.OpenConnectionAsync();
        Assert.Equal(1L, await ScalarAsync(db,
            "SELECT count(*) FROM pg_class WHERE relname = 'beauty_tenant_links' AND relrowsecurity AND relforcerowsecurity;"));

        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT policyname, qual FROM pg_policies WHERE schemaname='public' AND tablename='beauty_tenant_links';";
        var policies = new Dictionary<string, string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync()) policies[reader.GetString(0)] = reader.GetString(1);

        Assert.Equal(["provider_bypass", "tenant_isolation", "worker_bypass"], policies.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("NULLIF", policies["tenant_isolation"]);
        Assert.DoesNotContain("IS NULL", policies["tenant_isolation"]);
    }

    private static string InsertSql(Guid tenantId, string slug) =>
        "INSERT INTO beauty_tenant_links (\"Id\", \"TenantId\", \"BeautySlug\", \"BeautyStatus\", \"LastSyncedAt\") " +
        $"VALUES (gen_random_uuid(), '{tenantId:D}', '{slug}', 'active', now());";

    private async Task<RlsSession> OpenSessionAsync(Guid tenantId, string role)
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"SET app.tenant_id = '{tenantId:D}'; SET app.role = '{role}'; RESET app.consumer_account_id;");
#pragma warning restore EF1002
        return new RlsSession(db);
    }

    private static async Task<int> ExecAsync(RlsSession session, string sql)
    {
        await using var cmd = session.Db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(AppDbContext db, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static Task<long> ScalarAsync(RlsSession session, string sql) => ScalarAsync(session.Db, sql);

    private static async Task<string?> StatusAsync(AppDbContext db, string slug)
    {
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SELECT \"BeautyStatus\" FROM beauty_tenant_links WHERE \"BeautySlug\" = '{slug}';";
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private AppDbContext NewContext() => new(_options!);

    private sealed class RlsSession(AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;

        public async ValueTask DisposeAsync()
        {
            try { await Db.Database.ExecuteSqlRawAsync("RESET ROLE;"); }
            catch { /* best-effort cleanup only */ }
            await Db.DisposeAsync();
        }
    }
}
