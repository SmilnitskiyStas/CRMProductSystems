using System.Security.Claims;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

/// <summary>
/// TASK-346: LegalEntityAuthorization.CanManage grew a third OR branch (TenantRole
/// "legal_entities.manage" capability) alongside the two that already existed
/// (AtLeastEnterpriseAdmin role, User.Permissions "legal_entities.manage" override).
/// </summary>
public sealed class LegalEntityAuthorizationTests
{
    [Fact]
    public void CanManage_true_for_enterprise_admin_role()
        => Assert.True(LegalEntityAuthorization.CanManage(MakeUser(AppRoles.EnterpriseAdmin)));

    [Fact]
    public void CanManage_true_for_provider_role()
        => Assert.True(LegalEntityAuthorization.CanManage(MakeUser(AppRoles.Provider)));

    [Fact]
    public void CanManage_false_for_store_manager_with_no_override_and_no_capability()
        => Assert.False(LegalEntityAuthorization.CanManage(MakeUser(AppRoles.StoreManager)));

    [Fact]
    public void CanManage_true_via_legacy_permissions_claim_override()
        => Assert.True(LegalEntityAuthorization.CanManage(
            MakeUser(AppRoles.StoreManager, permissionsClaim: "legal_entities.manage")));

    [Fact]
    public void CanManage_true_via_tenant_role_capability_claim()
        => Assert.True(LegalEntityAuthorization.CanManage(
            MakeUser(AppRoles.StoreManager, capabilitiesClaim: "legal_entities.manage")));

    [Fact]
    public void CanManage_false_when_capability_claim_has_unrelated_keys_only()
        => Assert.False(LegalEntityAuthorization.CanManage(
            MakeUser(AppRoles.StoreManager, capabilitiesClaim: "analytics.view,orders.manage")));

    [Fact]
    public void CanManage_true_via_capability_claim_even_at_the_lowest_role_rank()
        => Assert.True(LegalEntityAuthorization.CanManage(
            MakeUser(AppRoles.Staff, capabilitiesClaim: "legal_entities.manage")));

    private static ClaimsPrincipal MakeUser(
        string role, string? permissionsClaim = null, string? capabilitiesClaim = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (permissionsClaim is not null)
            claims.Add(new Claim("permissions", permissionsClaim));
        if (capabilitiesClaim is not null)
            claims.Add(new Claim("capabilities", capabilitiesClaim));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
