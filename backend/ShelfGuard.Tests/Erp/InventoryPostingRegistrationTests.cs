using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Infrastructure;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Erp;

public sealed class InventoryPostingRegistrationTests
{
    private static IConfiguration Config(Dictionary<string, string?>? values = null)
    {
        values ??= [];
        values["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=not_connected";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Default_registration_is_disabled_and_does_not_replace_the_existing_stock_service()
    {
        var services = new ServiceCollection().AddApplication().AddInfrastructure(Config());
        var gate = (IInventoryPostingGate)Assert.Single(services, s => s.ServiceType == typeof(IInventoryPostingGate)).ImplementationInstance!;
        Assert.False(gate.Allows(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(typeof(StockService), Assert.Single(services, s => s.ServiceType == typeof(IStockService)).ImplementationType);
        Assert.Equal(typeof(InventoryAdjustmentPostingService), Assert.Single(services, s => s.ServiceType == typeof(IInventoryAdjustmentPosting)).ImplementationType);
        Assert.Equal(typeof(InventoryAdjustmentCommandStore), Assert.Single(services, s => s.ServiceType == typeof(IInventoryAdjustmentStore)).ImplementationType);
        Assert.False(((ProductionPostingOptions)Assert.Single(services,s=>s.ServiceType==typeof(ProductionPostingOptions)).ImplementationInstance!).Enabled);
        Assert.Equal(typeof(ProductionPostingPreviewService),Assert.Single(services,s=>s.ServiceType==typeof(IProductionPostingPreview)).ImplementationType);
        Assert.Equal(typeof(ProductionPostingPreviewReader),Assert.Single(services,s=>s.ServiceType==typeof(IProductionPostingPreviewReader)).ImplementationType);
    }

    [Fact]
    public void Configuration_binds_only_explicit_tenant_location_pairs()
    {
        var tenant = Guid.NewGuid(); var location = Guid.NewGuid();
        var services = new ServiceCollection().AddInfrastructure(Config(new()
        {
            ["Erp:InventoryPosting:Enabled"] = "true",
            ["Erp:InventoryPosting:Scopes:0:TenantId"] = tenant.ToString(),
            ["Erp:InventoryPosting:Scopes:0:LocationId"] = location.ToString(),
        }));
        var gate = (IInventoryPostingGate)Assert.Single(services, s => s.ServiceType == typeof(IInventoryPostingGate)).ImplementationInstance!;
        Assert.True(gate.Allows(tenant, location));
        Assert.False(gate.Allows(Guid.NewGuid(), location)); Assert.False(gate.Allows(tenant, Guid.NewGuid()));
    }

    [Fact]
    public void Enabling_without_explicit_scopes_fails_closed() => Assert.Throws<ArgumentException>(() =>
        new ServiceCollection().AddInfrastructure(Config(new() { ["Erp:InventoryPosting:Enabled"] = "true" })));
}
