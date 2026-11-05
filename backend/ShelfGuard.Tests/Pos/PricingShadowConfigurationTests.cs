using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Features.Pricing;
using Xunit;

namespace ShelfGuard.Tests.Pos;
public sealed class PricingShadowConfigurationTests
{
    [Theory][InlineData("bool")][InlineData("scope")]
    public void Invalid_observer_configuration_disables_it_and_records_error_without_breaking_DI(string kind)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Pricing:Shadow:Enabled"] = kind == "bool" ? "invalid" : "true",
            ["Pricing:Shadow:Scopes:0:TenantId"] = "invalid"
        }).Build();
        var services = new ServiceCollection(); ShelfGuard.Infrastructure.DependencyInjection.AddInfrastructure(services, config);
        using var provider = services.BuildServiceProvider(); var observer = provider.GetRequiredService<PricingShadow>();
        Assert.False(observer.IsEnabled(Guid.NewGuid(), Guid.NewGuid())); Assert.Equal(1, observer.Counts.Errors);
    }
    [Theory][InlineData(false, false)][InlineData(true, false)][InlineData(true, true)]
    public void Actual_DI_factory_requires_both_opt_in_and_an_exact_tenant_location_scope(bool enabled, bool scope)
    {
        var tenant = Guid.NewGuid(); var location = Guid.NewGuid();
        var values = new Dictionary<string, string?> { ["Pricing:Shadow:Enabled"] = enabled.ToString() };
        if (scope) { values["Pricing:Shadow:Scopes:0:TenantId"] = tenant.ToString(); values["Pricing:Shadow:Scopes:0:LocationId"] = location.ToString(); }
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection(); ShelfGuard.Infrastructure.DependencyInjection.AddInfrastructure(services, config);
        using var provider = services.BuildServiceProvider(); var observer = provider.GetRequiredService<IPricingShadow>();
        Assert.Equal(enabled && scope, observer.IsEnabled(tenant, location));
        Assert.False(observer.IsEnabled(Guid.NewGuid(), location)); Assert.False(observer.IsEnabled(tenant, Guid.NewGuid()));
    }
}
