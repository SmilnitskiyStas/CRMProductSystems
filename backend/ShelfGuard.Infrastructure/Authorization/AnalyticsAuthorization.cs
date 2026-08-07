using System.Security.Claims;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// TASK-480 (interactive analytics + margin plan): gates purchase-cost/margin figures on
/// <c>AnalyticsController</c>'s category-breakdown, losses-by-product, and product sales-trend
/// endpoints (TASK-481/482). Same imperative in-body-check shape as
/// <see cref="MarketingAnalyticsAuthorization.CanExportPii"/>, for the same reason: the decision
/// narrows PART of an otherwise-successful response — the controller nulls out the DTO's
/// <c>MarginAmount</c>/<c>MarginPercent</c> fields when this returns false, it does not 403 the
/// request — so it cannot be expressed as a blanket per-action <c>[Authorize]</c> policy the way
/// the controller's own class-level gate is. The rest of the response (stock levels, units sold,
/// revenue, loss totals) stays available to every caller who already cleared the class-level
/// <c>AnalyticsViewOrCapability</c> policy, regardless of this check.
///
/// Floor is <b>network_manager+</b> (<see cref="AppPolicies.AtLeastNetworkManagerRoles"/>) — one
/// tier above the store_manager+ floor (<see cref="AppPolicies.CanViewAnalyticsRoles"/>) that
/// already gates the whole controller. Purchase cost (<c>Item.PricePurchase</c>, sourced from
/// supplier receipts) is a narrower sensitivity than the read-only stock/sales figures the base
/// floor already exposes: a leaked purchase price directly signals supplier pricing/margin
/// structure, and a store_manager (or anyone below them) who both sees margin AND handles the
/// register/stock has a concrete incentive to exploit that number at the point of sale — unlike
/// <see cref="MarketingAnalyticsAuthorization.CanExportPii"/>, whose default floor stays at
/// store_manager+ (same as its controller's own floor) because unmasked contact PII is not a
/// materially different sensitivity tier from the marketing data store_manager+ already sees.
///
/// Losses (<c>WriteOffItem.LossAmount</c>) are deliberately NOT gated by this check — that figure
/// is already shown in aggregate to every store_manager+ caller on <c>/analytics</c> today; a
/// per-product breakdown of the same already-visible number is not a new sensitivity, so
/// <c>losses/by-product</c> returns its amounts unconditionally regardless of
/// <see cref="CanViewMargin"/>.
/// </summary>
public static class AnalyticsAuthorization
{
    public static bool CanViewMargin(ClaimsPrincipal user)
    {
        if (AppPolicies.AtLeastNetworkManagerRoles.Any(user.IsInRole))
            return true;

        return TenantRoleAuthorization.HasCapability(user, TenantRoleCapabilities.AnalyticsViewMargin);
    }
}
