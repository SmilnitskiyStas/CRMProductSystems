using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Managed-AI Phase 1 — tenant isolation, data layer (real Postgres).
///
/// The 6 AI advisors read the tenant's AI-slot config with exactly
/// <c>SELECT "Config" FROM integration_configs WHERE "Service" = 'ai_analyst' AND "IsEnabled"</c>
/// and NO <c>"TenantId"</c> filter of their own — RLS <c>tenant_isolation</c> is the only thing
/// scoping that read. Historically KI-036 "F5" was this exact query returning another tenant's
/// key after a leaked <c>app.role='provider'</c>. This test pins that a normal
/// <c>store_manager</c> session for tenant A can never see tenant B's <c>integration_configs</c>
/// row through that query, nor through a forged <c>"TenantId"</c> filter.
///
/// Soft-skips (no build failure) when no reachable Postgres — same harness as
/// <see cref="RlsCrossTenantIntegrationTests"/> (<c>TENANT_ISOLATION_TESTS</c> collection,
/// <see cref="RlsAuditRoleFixture"/>). CI has no Postgres service; runs locally against
/// <c>docker compose up -d postgres</c> (port 5435).
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class AiIntegrationConfigCrossTenantRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private NpgsqlConnection? _connection;
    private bool _dbAvailable;

    public AiIntegrationConfigCrossTenantRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
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
                $"Skipping — no reachable Postgres at '{_fixture.ConnectionString}': {_fixture.UnavailableReason}");
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
            _output.WriteLine($"Skipping — no reachable Postgres: {ex.Message}");
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
    public async Task ClaudeKeyLookup_AsTenantA_NeverReturnsTenantBsRow()
    {
        if (!_dbAvailable) return; // soft-skip, see class remarks

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await ExecAsync(
            "INSERT INTO tenants (\"Id\", \"Name\", \"Slug\") VALUES " +
            "(@a, 'RLS Audit AI Tenant A', @slugA), (@b, 'RLS Audit AI Tenant B', @slugB);",
            ("a", tenantA), ("b", tenantB),
            ("slugA", $"rls-audit-ai-a-{tenantA:N}"), ("slugB", $"rls-audit-ai-b-{tenantB:N}"));

        // Tenant A on claude, tenant B on openai — the AiClientFactory query matches either.
        await ExecAsync(
            "INSERT INTO integration_configs (\"Id\", \"TenantId\", \"Service\", \"Config\", \"IsEnabled\", \"CreatedAt\", \"UpdatedAt\") VALUES " +
            "(gen_random_uuid(), @a, 'ai_analyst', @cfgA::jsonb, true, now(), now()), " +
            "(gen_random_uuid(), @b, 'ai_analyst', @cfgB::jsonb, true, now(), now());",
            ("a", tenantA), ("b", tenantB),
            ("cfgA", "{\"provider\":\"claude\",\"api_key\":\"sk-ant-AAAA-tenant-a\",\"model\":\"claude-sonnet-4-6\"}"),
            ("cfgB", "{\"provider\":\"openai\",\"api_key\":\"sk-oai-BBBB-tenant-b\",\"model\":\"gpt-4o\"}"));

        try
        {
            await ExecAsync("SET ROLE rls_audit_test_role;");
            await ExecAsync($"SET app.tenant_id = '{tenantA:D}'; SET app.role = 'store_manager';");

            // The AiClientFactory's exact per-slot query — no TenantId filter, RLS is the only
            // scope. Must resolve to tenant A's row only, never tenant B's key.
            await using var cmd = new NpgsqlCommand(
                "SELECT \"Config\"::text FROM integration_configs WHERE \"Service\" = 'ai_analyst' AND \"IsEnabled\";",
                _connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            var configs = new List<string>();
            while (await reader.ReadAsync())
                configs.Add(reader.GetString(0));
            await reader.DisposeAsync();

            Assert.Single(configs);
            Assert.Contains("tenant-a", configs[0]);
            Assert.DoesNotContain("tenant-b", configs[0]);

            // Forged filter for tenant B's id, connected as tenant A — RLS still wins.
            var leaked = await ScalarAsync(
                "SELECT count(*) FROM integration_configs WHERE \"TenantId\" = @b;", ("b", tenantB));
            Assert.Equal(0L, leaked);
        }
        finally
        {
            await ExecAsync("RESET ROLE;");
            await ExecAsync("DELETE FROM integration_configs WHERE \"TenantId\" IN (@a, @b);", ("a", tenantA), ("b", tenantB));
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
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
