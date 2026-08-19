using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>See <see cref="ISubscriptionPlanFeatureGate"/> — a documented no-enforcement placeholder, not real behavior.</summary>
public sealed class SubscriptionPlanFeatureGate : ISubscriptionPlanFeatureGate
{
    private readonly ITenantRepository _tenants;

    public SubscriptionPlanFeatureGate(ITenantRepository tenants) => _tenants = tenants;

    public async Task<string?> GetTenantPlanAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        return tenant?.Plan;
    }
}
