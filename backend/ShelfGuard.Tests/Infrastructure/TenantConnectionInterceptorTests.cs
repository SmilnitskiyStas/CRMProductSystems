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
    public void BuildSetSql_accepts_all_valid_roles(string role)
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, role);

        Assert.NotNull(sql);
        Assert.Contains($"SET app.role = '{role}'", sql);
        Assert.Contains($"SET app.tenant_id = '{NullUuid}'", sql);
    }
}
