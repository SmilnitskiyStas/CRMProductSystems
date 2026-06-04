using ShelfGuard.Infrastructure.Interceptors;
using Xunit;

namespace ShelfGuard.Tests.Infrastructure;

public sealed class TenantConnectionInterceptorTests
{
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
    public void BuildSetSql_sets_only_role_for_provider_user()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, "provider");

        Assert.NotNull(sql);
        Assert.DoesNotContain("app.tenant_id", sql);
        Assert.Contains("SET app.role = 'provider'", sql);
    }

    [Fact]
    public void BuildSetSql_returns_null_when_both_claims_absent()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, null);
        Assert.Null(sql);
    }

    [Fact]
    public void BuildSetSql_omits_role_when_value_is_unknown()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, "unknown_role");
        Assert.Null(sql);
    }

    [Fact]
    public void BuildSetSql_rejects_injection_attempt_in_role()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql(null, "admin'; DROP TABLE users;--");
        Assert.Null(sql);
    }

    [Fact]
    public void BuildSetSql_omits_tenant_id_when_value_is_not_a_guid()
    {
        var sql = TenantConnectionInterceptor.BuildSetSql("not-a-valid-uuid", "store_manager");

        Assert.NotNull(sql);
        Assert.DoesNotContain("app.tenant_id", sql);
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
    }
}
