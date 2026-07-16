using System.Security.Claims;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

/// <summary>
/// TASK-359 (Block 8 audit): SupplierCabinetController was gated only by role
/// (AppPolicies.SupplierCabinet = RequireRole(supplier_admin)) — SupplierPermissions
/// (from SupplierRole.Permissions, TASK-306) only ever drove frontend nav visibility,
/// never a server-side check. SupplierPermissionAuthorization.HasPermission is the fix.
/// </summary>
public sealed class SupplierPermissionAuthorizationTests
{
    [Fact]
    public void HasPermission_true_when_no_permissions_claim_at_all()
        // No SupplierRoleId assigned at invite time → User.Permissions is null →
        // JwtService never adds a "permissions" claim → unrestricted (matches
        // SupplierCabinetService.InviteStaffAsync's documented "no role → full access").
        => Assert.True(SupplierPermissionAuthorization.HasPermission(
            MakeUser(), SupplierPermissions.StaffManagement));

    [Fact]
    public void HasPermission_true_when_claim_contains_the_requested_key()
        => Assert.True(SupplierPermissionAuthorization.HasPermission(
            MakeUser("catalog_management,task_board"), SupplierPermissions.CatalogManagement));

    [Fact]
    public void HasPermission_false_when_claim_present_but_missing_the_requested_key()
        => Assert.False(SupplierPermissionAuthorization.HasPermission(
            MakeUser("task_board"), SupplierPermissions.StaffManagement));

    [Fact]
    public void HasPermission_false_when_claim_is_a_single_unrelated_key()
        => Assert.False(SupplierPermissionAuthorization.HasPermission(
            MakeUser("client_reviews"), SupplierPermissions.ProfileManagement));

    private static ClaimsPrincipal MakeUser(string? permissionsClaim = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, AppRoles.SupplierAdmin) };
        if (permissionsClaim is not null)
            claims.Add(new Claim("permissions", permissionsClaim));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
