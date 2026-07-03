using ShelfGuard.Domain.Entities;
using Xunit;

namespace ShelfGuard.Tests.Domain;

public sealed class TenantTests
{
    [Theory]
    [InlineData("retail", new[] { "inventory", "procurement", "pos" })]
    [InlineData("auto_service", new[] { "auto_service", "procurement" })]
    [InlineData("restaurant", new[] { "inventory", "pos", "production" })]
    [InlineData("warehouse", new[] { "inventory", "procurement" })]
    [InlineData("supplier", new[] { "marketplace_supplier" })]
    public void DefaultModulesForBusinessType_KnownTypes_ReturnsExpectedSet(string businessType, string[] expected)
    {
        var modules = Tenant.DefaultModulesForBusinessType(businessType);

        Assert.Equal(expected, modules);
    }

    [Fact]
    public void DefaultModulesForBusinessType_UnknownType_FallsBackToInventory()
    {
        var modules = Tenant.DefaultModulesForBusinessType("not_a_real_type");

        Assert.Equal(new[] { "inventory" }, modules);
    }

    [Fact]
    public void HasModule_ModuleEnabled_ReturnsTrue()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.UpdateModules(["inventory", "pos"]);

        Assert.True(tenant.HasModule("inventory"));
        Assert.True(tenant.HasModule("POS")); // case-insensitive
    }

    [Fact]
    public void HasModule_ModuleNotEnabled_ReturnsFalse()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.UpdateModules(["inventory"]);

        Assert.False(tenant.HasModule("auto_service"));
    }

    [Fact]
    public void HasModule_NoModulesSet_ReturnsFalse()
    {
        var tenant = Tenant.Create("Acme", "acme");

        Assert.False(tenant.HasModule("inventory"));
    }

    [Fact]
    public void UpdateBusinessType_UnknownValue_ReturnsError()
    {
        var tenant = Tenant.Create("Acme", "acme");

        var error = tenant.UpdateBusinessType("spaceship_repair");

        Assert.NotNull(error);
        Assert.Equal("retail", tenant.BusinessType); // unchanged
    }

    [Fact]
    public void UpdateBusinessType_ValidValue_Succeeds()
    {
        var tenant = Tenant.Create("Acme", "acme");

        var error = tenant.UpdateBusinessType("auto_service");

        Assert.Null(error);
        Assert.Equal("auto_service", tenant.BusinessType);
    }
}
