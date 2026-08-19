using NSubstitute;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-543 ЕТАП 18 stub — SubscriptionPlanFeatureGate reads Tenant.Plan and enforces nothing.
/// See ISubscriptionPlanFeatureGate remarks: this is a documented placeholder seam for a future
/// stage, not real behavior.
/// </summary>
public sealed class SubscriptionPlanFeatureGateTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly SubscriptionPlanFeatureGate _sut;

    public SubscriptionPlanFeatureGateTests()
    {
        _sut = new SubscriptionPlanFeatureGate(_tenants);
    }

    [Fact]
    public async Task Returns_the_tenants_current_plan()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.UpdatePlan("enterprise");
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var plan = await _sut.GetTenantPlanAsync(tenant.Id);

        Assert.Equal("enterprise", plan);
    }

    [Fact]
    public async Task Returns_null_when_tenant_does_not_exist()
    {
        var tenantId = Guid.NewGuid();
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns((Tenant?)null);

        var plan = await _sut.GetTenantPlanAsync(tenantId);

        Assert.Null(plan);
    }
}
