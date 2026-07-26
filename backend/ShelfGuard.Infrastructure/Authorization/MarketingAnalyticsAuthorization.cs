using System.Security.Claims;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// TASK-406: gates unmasked-PII marketing-analytics exports. store_manager (and above) can
/// always request the unmasked variant; any other role needs the
/// <c>marketing_analytics.export_pii</c> TenantRole capability (ADR-020). This mirrors
/// <see cref="LegalEntityAuthorization"/>'s shape exactly (imperative in-body check, not a
/// blanket per-action policy) for the same reason: the decision depends on a request field
/// (does THIS export ask to unmask?), not on the whole action — a role/capability holder can
/// still call the same endpoint for a masked export without holding this capability at all.
/// </summary>
public static class MarketingAnalyticsAuthorization
{
    public static bool CanExportPii(ClaimsPrincipal user)
    {
        if (AppPolicies.AtLeastStoreManagerRoles.Any(user.IsInRole))
            return true;

        return TenantRoleAuthorization.HasCapability(user, TenantRoleCapabilities.MarketingAnalyticsExportPii);
    }
}
