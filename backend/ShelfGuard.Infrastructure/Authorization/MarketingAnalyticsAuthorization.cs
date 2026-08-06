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

    /// <summary>
    /// TASK-477 (security review TASK-474, finding B): gates <c>PostCampaignController.Import</c>
    /// specifically — `docs/uployal/AUDIENCE_ANALYSIS.md` §32 explicitly calls for "окреме право
    /// на upload" (a separate upload-specific permission), distinct from the
    /// <c>marketing_analytics.view</c> floor every read-only report tab on that same controller
    /// shares today. Same imperative, in-body-check shape as <see cref="CanExportPii"/> (needed
    /// for the same reason: this narrows ONE action within an otherwise view-gated controller, so
    /// it can't be a blanket class-level `[Authorize]` policy).
    ///
    /// Deliberately role-ONLY (<see cref="AppPolicies.AtLeastStoreManagerRoles"/> — the same
    /// floor <see cref="CanExportPii"/> itself defaults to), with no capability-widening escape
    /// hatch. Import creates DB rows and — per finding A — is the single most resource-costly
    /// action on this controller; this mirrors <c>TenantRoleCapabilities.ReceiptsView</c>'s own
    /// documented precedent that a write-heavy action is deliberately EXCLUDED from the
    /// capability catalog rather than growing it for every new mutating endpoint (ADR-020 point
    /// 3), instead of copying <see cref="CanExportPii"/>'s role-OR-capability shape verbatim. A
    /// tenant that wants a sub-store_manager "marketing specialist" role to import segments can
    /// still do so via the ordinary role-assignment mechanism (grant store_manager); add a
    /// dedicated capability later if a real product need for a narrower grant shows up, rather
    /// than speculatively now.
    /// </summary>
    public static bool CanImportSegments(ClaimsPrincipal user) =>
        AppPolicies.AtLeastStoreManagerRoles.Any(user.IsInRole);
}
