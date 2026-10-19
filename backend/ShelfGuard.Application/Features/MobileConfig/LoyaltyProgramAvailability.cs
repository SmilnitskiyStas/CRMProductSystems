using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>The bonus-program setting is the single activation switch. Legacy mobile
/// configuration flags must not override it; the licensed module remains a prerequisite.</summary>
public interface ILoyaltyProgramAvailability
{
    Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed class LoyaltyProgramAvailability(
    ITenantRepository tenants, ILoyaltyRepository loyalty, ITenantSessionOverride tenantScope)
    : ILoyaltyProgramAvailability
{
    public async Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive || !tenant.HasModule("loyalty"))
            return false;

        // This is a public availability check for the validated tenant, not access to
        // customer data. Consumer sessions need the same RLS scope as network discovery.
        var settings = await tenantScope.ExecuteAsync(
            tenantId, () => loyalty.GetSettingsAsync(tenantId, ct), ct);
        return settings?.IsEnabled ?? true;
    }
}
