using System.Security.Claims;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Backend enforcement for supplier staff fine-grained permissions (TASK-306, ADR-016
/// supplier_roles). Mirrors <see cref="LegalEntityAuthorization"/>'s "permissions" claim read.
///
/// Found during the Block 8 pre-launch audit (TASK-359): <c>SupplierCabinetController</c> was
/// gated only by role — <c>AppPolicies.SupplierCabinet</c> = <c>RequireRole(supplier_admin)</c>.
/// Every invited staff member got that same role regardless of the <c>SupplierRole</c>/
/// <c>Permissions</c> assigned at invite time (<c>SupplierCabinetService.InviteStaffAsync</c>
/// resolves the role into <c>User.Permissions</c>, which IS correctly baked into the JWT
/// "permissions" claim by the existing generic <c>AuthService</c>/<c>JwtService</c> pipeline —
/// only the read side was missing). The <c>ShelfGuard.Domain.Constants.SupplierPermissions</c>
/// keys only ever drove frontend nav visibility (<c>Sidebar.tsx</c>), never a server-side
/// check — the same class of gap ADR-020 documented and fixed for tenant roles ("the blocking
/// discovery"), left unaddressed on the supplier side until now.
/// </summary>
public static class SupplierPermissionAuthorization
{
    /// <summary>
    /// True when the caller may perform an action gated by <paramref name="permission"/> (one
    /// of the <c>SupplierPermissions</c> keys). A caller with no "permissions" claim at all —
    /// <c>User.Permissions</c> is null/empty, the default for staff invited without an explicit
    /// <c>SupplierRoleId</c>, and for the tenant's original owner-admin — is unrestricted,
    /// matching <c>SupplierCabinetService.InviteStaffAsync</c>'s documented "no role → full
    /// access" behavior.
    /// </summary>
    public static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        var claim = user.Claims.FirstOrDefault(c => c.Type == "permissions")?.Value;
        return string.IsNullOrEmpty(claim) || claim.Split(',').Contains(permission);
    }
}
