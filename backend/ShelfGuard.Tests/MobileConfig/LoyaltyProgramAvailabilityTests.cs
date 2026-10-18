using NSubstitute;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

public sealed class LoyaltyProgramAvailabilityTests
{
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Availability_uses_bonus_setting_with_module_prerequisite(
        bool hasModule, bool? bonusEnabled, bool expected)
    {
        var tenant = Tenant.Create("Test", "test");
        if (hasModule) tenant.UpdateModules(["loyalty"]);
        var tenants = Substitute.For<ITenantRepository>();
        tenants.GetByIdAsync(tenant.Id, default).Returns(tenant);
        var loyalty = Substitute.For<ILoyaltyRepository>();
        loyalty.GetSettingsAsync(tenant.Id, default).Returns(bonusEnabled.HasValue
            ? new LoyaltyProgramSettings { TenantId = tenant.Id, IsEnabled = bonusEnabled.Value }
            : null);
        var scope = Substitute.For<ITenantSessionOverride>();
        scope.ExecuteAsync(tenant.Id, Arg.Any<Func<Task<LoyaltyProgramSettings?>>>(), default)
            .Returns(ci => ci.Arg<Func<Task<LoyaltyProgramSettings?>>>()());
        var service = new LoyaltyProgramAvailability(tenants, loyalty, scope);

        Assert.Equal(expected, await service.IsEnabledAsync(tenant.Id));
        if (!hasModule)
            await scope.DidNotReceive().ExecuteAsync(
                tenant.Id, Arg.Any<Func<Task<LoyaltyProgramSettings?>>>(), default);
    }

    [Fact]
    public async Task Unknown_tenant_is_unavailable()
    {
        var service = new LoyaltyProgramAvailability(Substitute.For<ITenantRepository>(),
            Substitute.For<ILoyaltyRepository>(), Substitute.For<ITenantSessionOverride>());
        Assert.False(await service.IsEnabledAsync(Guid.NewGuid()));
    }
}
