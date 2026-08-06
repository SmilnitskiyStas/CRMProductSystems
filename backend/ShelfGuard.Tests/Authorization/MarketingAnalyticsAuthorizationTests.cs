using System.Security.Claims;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

/// <summary>
/// TASK-406: MarketingAnalyticsAuthorization.CanExportPii — store_manager+ (the default floor,
/// same role array as AppPolicies.AtLeastStoreManagerRoles) OR the
/// marketing_analytics.export_pii TenantRole capability. Mirrors LegalEntityAuthorizationTests'
/// shape exactly (same imperative-check pattern, same MakeUser helper).
/// </summary>
public sealed class MarketingAnalyticsAuthorizationTests
{
    [Fact]
    public void CanExportPii_true_for_store_manager_role()
        => Assert.True(MarketingAnalyticsAuthorization.CanExportPii(MakeUser(AppRoles.StoreManager)));

    [Fact]
    public void CanExportPii_true_for_network_manager_role()
        => Assert.True(MarketingAnalyticsAuthorization.CanExportPii(MakeUser(AppRoles.NetworkManager)));

    [Fact]
    public void CanExportPii_true_for_enterprise_admin_role()
        => Assert.True(MarketingAnalyticsAuthorization.CanExportPii(MakeUser(AppRoles.EnterpriseAdmin)));

    [Fact]
    public void CanExportPii_true_for_provider_role()
        => Assert.True(MarketingAnalyticsAuthorization.CanExportPii(MakeUser(AppRoles.Provider)));

    [Fact]
    public void CanExportPii_false_for_cashier_with_no_capability()
        => Assert.False(MarketingAnalyticsAuthorization.CanExportPii(MakeUser(AppRoles.Cashier)));

    [Fact]
    public void CanExportPii_false_for_merchandiser_with_no_capability()
        => Assert.False(MarketingAnalyticsAuthorization.CanExportPii(MakeUser(AppRoles.Merchandiser)));

    [Fact]
    public void CanExportPii_true_via_capability_claim_even_at_the_lowest_role_rank()
        => Assert.True(MarketingAnalyticsAuthorization.CanExportPii(
            MakeUser(AppRoles.Staff, capabilitiesClaim: "marketing_analytics.export_pii")));

    [Fact]
    public void CanExportPii_false_when_capability_claim_has_unrelated_keys_only()
        => Assert.False(MarketingAnalyticsAuthorization.CanExportPii(
            MakeUser(AppRoles.Cashier, capabilitiesClaim: "analytics.view,orders.manage")));

    // ── CanImportSegments (TASK-477, security review TASK-474 finding B) ───────────────────────
    // Deliberately role-ONLY (AtLeastStoreManagerRoles, matching CanExportPii's own floor) with NO
    // capability escape hatch — see MarketingAnalyticsAuthorization.CanImportSegments's doc
    // comment for the reasoning. The two "false ... capability" tests below are the important
    // negative cases: they pin that this permission has no capability-based bypass at all, unlike
    // CanExportPii.

    [Fact]
    public void CanImportSegments_true_for_store_manager_role()
        => Assert.True(MarketingAnalyticsAuthorization.CanImportSegments(MakeUser(AppRoles.StoreManager)));

    [Fact]
    public void CanImportSegments_true_for_network_manager_role()
        => Assert.True(MarketingAnalyticsAuthorization.CanImportSegments(MakeUser(AppRoles.NetworkManager)));

    [Fact]
    public void CanImportSegments_true_for_enterprise_admin_role()
        => Assert.True(MarketingAnalyticsAuthorization.CanImportSegments(MakeUser(AppRoles.EnterpriseAdmin)));

    [Fact]
    public void CanImportSegments_true_for_provider_role()
        => Assert.True(MarketingAnalyticsAuthorization.CanImportSegments(MakeUser(AppRoles.Provider)));

    [Fact]
    public void CanImportSegments_false_for_cashier_with_no_capability()
        => Assert.False(MarketingAnalyticsAuthorization.CanImportSegments(MakeUser(AppRoles.Cashier)));

    [Fact]
    public void CanImportSegments_false_for_merchandiser_with_no_capability()
        => Assert.False(MarketingAnalyticsAuthorization.CanImportSegments(MakeUser(AppRoles.Merchandiser)));

    [Fact]
    public void CanImportSegments_false_even_with_the_marketing_analytics_view_capability_claim()
        => Assert.False(MarketingAnalyticsAuthorization.CanImportSegments(
            MakeUser(AppRoles.Staff, capabilitiesClaim: "marketing_analytics.view")));

    [Fact]
    public void CanImportSegments_false_even_with_the_export_pii_capability_claim_a_different_permission()
        => Assert.False(MarketingAnalyticsAuthorization.CanImportSegments(
            MakeUser(AppRoles.Cashier, capabilitiesClaim: "marketing_analytics.export_pii")));

    private static ClaimsPrincipal MakeUser(string role, string? capabilitiesClaim = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (capabilitiesClaim is not null)
            claims.Add(new Claim("capabilities", capabilitiesClaim));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
