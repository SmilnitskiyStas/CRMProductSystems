using ShelfGuard.Infrastructure.Interceptors;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

public sealed class TenantConnectionInterceptorTests
{
    private const string NullUuid = "00000000-0000-0000-0000-000000000000";

    [Fact]
    public void BuildSetSql_sets_both_tenant_id_and_role_for_regular_user()
    {
        var tenantId = Guid.NewGuid();
        var sql = TenantConnectionInterceptor.BuildSetSql(tenantId.ToString(), "store_manager");

        Assert.NotNull(sql);
        Assert.Contains($"SET app.tenant_id = '{tenantId:D}'", sql);
        Assert.Contains("SET app.role = 'store_manager'", sql);
    }

    [Fact]
    public void BuildSetSql_sets_null_uuid_for_provider_user_to_prevent_pool_leakage()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, "provider");

        Assert.NotNull(sql);
        Assert.Contains($"SET app.tenant_id = '{NullUuid}'", sql);
        Assert.Contains("SET app.role = 'provider'", sql);
    }

    [Fact]
    public void BuildSetSql_always_sets_null_uuid_when_both_claims_absent()
    {
        // Must always SET app.tenant_id to clear any stale value from a pooled connection.
        var sql = TenantConnectionInterceptor.BuildSetSql(null, null);

        Assert.NotNull(sql);
        Assert.Contains($"SET app.tenant_id = '{NullUuid}'", sql);
        Assert.DoesNotContain("app.role", sql);
    }

    [Fact]
    public void BuildSetSql_sets_null_uuid_and_omits_unknown_role()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, "unknown_role");

        Assert.NotNull(sql);
        Assert.Contains($"SET app.tenant_id = '{NullUuid}'", sql);
        Assert.DoesNotContain("app.role", sql);
    }

    [Fact]
    public void BuildSetSql_rejects_injection_attempt_in_role()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, "admin'; DROP TABLE users;--");

        Assert.NotNull(sql);
        Assert.Contains($"SET app.tenant_id = '{NullUuid}'", sql);
        Assert.DoesNotContain("app.role", sql);
    }

    [Fact]
    public void BuildSetSql_uses_null_uuid_when_tenant_id_is_not_a_guid()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql("not-a-valid-uuid", "store_manager");

        Assert.NotNull(sql);
        Assert.Contains($"SET app.tenant_id = '{NullUuid}'", sql);
        Assert.Contains("SET app.role = 'store_manager'", sql);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("enterprise_admin")]
    [InlineData("network_manager")]
    [InlineData("store_manager")]
    [InlineData("merchandiser")]
    [InlineData("storekeeper")]
    [InlineData("cashier")]
    [InlineData("staff")]
    public void BuildSetSql_accepts_all_valid_roles(string role)
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, role);

        Assert.NotNull(sql);
        Assert.Contains($"SET app.role = '{role}'", sql);
        Assert.Contains($"SET app.tenant_id = '{NullUuid}'", sql);
    }

    // ── app.user_id (TASK-392b) ─────────────────────────────────────────────

    [Fact]
    public void BuildSetSql_sets_user_id_when_valid_guid_provided()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sql = TenantConnectionInterceptor.BuildSetSql(tenantId.ToString(), "store_manager", userId.ToString());

        Assert.NotNull(sql);
        Assert.Contains($"SET app.tenant_id = '{tenantId:D}'", sql);
        Assert.Contains("SET app.role = 'store_manager'", sql);
        Assert.Contains($"SET app.user_id = '{userId:D}'", sql);
    }

    [Fact]
    public void BuildSetSql_uses_null_uuid_when_user_id_omitted()
    {
        // Mirrors app.tenant_id's discipline — ALWAYS set, never left stale on a pooled
        // connection, even when the third argument isn't supplied at all.
        var sql = TenantConnectionInterceptor.BuildSetSql(Guid.NewGuid().ToString(), "store_manager");

        Assert.NotNull(sql);
        Assert.Contains($"SET app.user_id = '{NullUuid}'", sql);
    }

    [Fact]
    public void BuildSetSql_uses_null_uuid_when_user_id_is_not_a_guid()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, null, "not-a-valid-uuid");

        Assert.NotNull(sql);
        Assert.Contains($"SET app.user_id = '{NullUuid}'", sql);
    }

    [Fact]
    public void BuildSetSql_uses_null_uuid_when_user_id_claim_absent()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, null, null);

        Assert.NotNull(sql);
        Assert.Contains($"SET app.user_id = '{NullUuid}'", sql);
    }
}
