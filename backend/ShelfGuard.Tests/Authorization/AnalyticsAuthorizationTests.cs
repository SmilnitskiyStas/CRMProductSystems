using System.Security.Claims;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

/// <summary>
/// TASK-480: AnalyticsAuthorization.CanViewMargin — network_manager+
/// (AppPolicies.AtLeastNetworkManagerRoles) OR the analytics.view_margin TenantRole capability.
/// Mirrors MarketingAnalyticsAuthorizationTests' CanExportPii fact set exactly (same MakeUser
/// helper, same shape), shifted up one role tier: store_manager is a "false, no capability" case
/// here — it clears AnalyticsController's own class-level store_manager+ floor but not this
/// narrower margin check — where store_manager was a "true" case for CanExportPii's
/// store_manager+ floor.
/// </summary>
public sealed class AnalyticsAuthorizationTests
{
    [Fact]
    public void CanViewMargin_true_for_network_manager_role()
        => Assert.True(AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.NetworkManager)));

    [Fact]
    public void CanViewMargin_true_for_enterprise_admin_role()
        => Assert.True(AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.EnterpriseAdmin)));

    [Fact]
    public void CanViewMargin_true_for_provider_role()
        => Assert.True(AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.Provider)));

    // ── The important negative cases: store_manager clears the base controller floor
    // (AnalyticsViewOrCapability / CanViewAnalyticsRoles) but NOT this narrower, one-tier-higher
    // margin check — pins that "can view analytics at all" and "can view margin within it" are
    // deliberately different thresholds. ─────────────────────────────────────────────────────

    [Fact]
    public void CanViewMargin_false_for_store_manager_with_no_capability()
        => Assert.False(AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.StoreManager)));

    [Fact]
    public void CanViewMargin_false_for_cashier_with_no_capability()
        => Assert.False(AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.Cashier)));

    [Fact]
    public void CanViewMargin_false_for_merchandiser_with_no_capability()
        => Assert.False(AnalyticsAuthorization.CanViewMargin(MakeUser(AppRoles.Merchandiser)));

    // ── Capability escape hatch: widens who can see margin below the network_manager+ floor,
    // same as every other role-OR-capability check in the codebase. ────────────────────────────

    [Fact]
    public void CanViewMargin_true_for_store_manager_via_capability_claim()
        => Assert.True(AnalyticsAuthorization.CanViewMargin(
            MakeUser(AppRoles.StoreManager, capabilitiesClaim: "analytics.view_margin")));

    [Fact]
    public void CanViewMargin_true_via_capability_claim_even_at_the_lowest_role_rank()
        => Assert.True(AnalyticsAuthorization.CanViewMargin(
            MakeUser(AppRoles.Staff, capabilitiesClaim: "analytics.view_margin")));

    [Fact]
    public void CanViewMargin_false_when_capability_claim_has_unrelated_keys_only()
        => Assert.False(AnalyticsAuthorization.CanViewMargin(
            MakeUser(AppRoles.Cashier, capabilitiesClaim: "analytics.view,orders.manage")));

    private static ClaimsPrincipal MakeUser(string role, string? capabilitiesClaim = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (capabilitiesClaim is not null)
            claims.Add(new Claim("capabilities", capabilitiesClaim));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
