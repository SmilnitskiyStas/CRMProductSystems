using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Api.Infrastructure;
using Xunit;

namespace ShelfGuard.Tests;

public class RoleControllerFeatureProviderTests
{
    private static List<Type> AllControllers() =>
        typeof(AuthController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t) && t.Name.EndsWith("Controller"))
            .ToList();

    private static HashSet<string> Names(string? role)
    {
        var feature = new ControllerFeature();
        foreach (var t in AllControllers()) feature.Controllers.Add(t.GetTypeInfo());
        new RoleControllerFeatureProvider(role).PopulateFeature(Array.Empty<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPart>(), feature);
        return feature.Controllers.Select(t => t.Name).ToHashSet();
    }

    [Fact]
    public void EveryController_IsClassifiedIntoExactlyOneGroup()
    {
        foreach (var t in AllControllers())
        {
            var roles = new[] { "core", "consumer", "provider" }
                .Where(r => RoleControllerFeatureProvider.IsAllowed(r, t.Name)).ToList();
            var group = RoleControllerFeatureProvider.Classify(t.Name);
            if (group == ControllerGroup.Shared)
                Assert.True(roles.Count == 3, $"{t.Name} is shared but not mapped in every role");
            else
                Assert.True(roles.Count == 1, $"Controller {t.Name} is not classified into exactly one group");
        }
    }

    [Theory]
    [InlineData("ConsumerAuthController", ControllerGroup.Consumer)]
    [InlineData("MobileConfigController", ControllerGroup.Consumer)]
    [InlineData("MobileBlocksController", ControllerGroup.Core)]
    [InlineData("SupplierCabinetController", ControllerGroup.Provider)]
    [InlineData("SupplierCabinetInventoryController", ControllerGroup.Provider)]
    [InlineData("MarketplaceController", ControllerGroup.Provider)]
    [InlineData("PosController", ControllerGroup.Core)]
    [InlineData("AuthController", ControllerGroup.Shared)]
    [InlineData("GeoController", ControllerGroup.Shared)]
    public void Classify_KnownControllers(string name, ControllerGroup expected) =>
        Assert.Equal(expected, RoleControllerFeatureProvider.Classify(name));

    [Fact]
    public void ConsumerRole_KeepsConsumerAndShared_DropsOthers()
    {
        var n = Names("consumer");
        Assert.Contains(nameof(ConsumerAuthController), n);
        Assert.Contains(nameof(AuthController), n);
        Assert.DoesNotContain(nameof(PosController), n);
        Assert.DoesNotContain(nameof(SupplierCabinetController), n);
    }

    [Fact]
    public void CoreRole_KeepsCoreAndShared_DropsOthers()
    {
        var n = Names("core");
        Assert.Contains(nameof(PosController), n);
        Assert.Contains(nameof(GeoController), n);
        Assert.DoesNotContain(nameof(ConsumerAuthController), n);
        Assert.DoesNotContain(nameof(SupplierCabinetController), n);
    }

    [Fact]
    public void ProviderRole_KeepsProviderAndShared_DropsOthers()
    {
        var n = Names("provider");
        Assert.Contains(nameof(SupplierCabinetController), n);
        Assert.Contains(nameof(AuthController), n);
        Assert.DoesNotContain(nameof(PosController), n);
        Assert.DoesNotContain(nameof(ConsumerAuthController), n);
    }

    [Theory]
    [InlineData("all")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    public void AllOrUnknownRole_KeepsEverything(string? role) =>
        Assert.Equal(AllControllers().Count, Names(role).Count);
}
