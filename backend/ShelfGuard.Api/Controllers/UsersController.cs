using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Application.Features.Users.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// User management (within a tenant). Store managers see only their store's users.
/// Enterprise admins manage all users in their tenant.
/// ADR-020 (TASK-346): no class-level policy — every action below carries its own explicit
/// policy. Invite/Update/Deactivate additionally admit a "users.manage" TenantRole capability
/// holder past the coarse `[Authorize(Policy=...)]` gate regardless of role rank;
/// UpdatePermissions/GrantTemporaryPermission/RevokeTemporaryPermission/AssignTenantRole are
/// deliberately left role-gated only (anti-escalation — a capability-only user must never be
/// able to grant itself or others more access than the template intends).
/// TASK-347 (security review of the above): that coarse gate has no notion of RoleRank, so a
/// capability holder who cleared it could still act on/assign roles above their own station —
/// <see cref="IUserService.InviteAsync"/>/<see cref="IUserService.UpdateAsync"/>/
/// <see cref="IUserService.DeactivateAsync"/> now each re-check RoleRank server-side
/// (acting user vs. target's current role, and vs. any newly-requested role) regardless of
/// which policy path let the caller in, closing that gap for both the new capability path and
/// a pre-existing store_manager self-escalation hole in Update.
/// TASK-352 (Block 1 pre-launch audit, user decision): Invite/Deactivate previously sat behind
/// a narrower <c>AtLeastEnterpriseAdmin</c> floor than Update's <c>AtLeastStoreManager</c> —
/// tighter than v1-spec.md §3.2, which grants staff management to network_manager and
/// store_manager too. All three actions now share <see cref="AppPolicies.StoreManagerOrUsersManage"/>;
/// the RoleRank checks above are unaffected and still gate what a network_manager/store_manager
/// can actually do to a specific target (cannot touch a same-or-higher-ranked user, cannot
/// assign a role above their own).
/// </summary>
[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    /// <summary>
    /// Returns all users for the current tenant. <c>storeIds</c> is a repeated query param
    /// (header store selector, TASK-517) — omitted/empty means "all stores" for an
    /// enterprise_admin/network_manager-exempt caller. TASK-519 (security fix): for an acting
    /// caller whose own role is store-bound (network_manager, store_manager, merchandiser,
    /// storekeeper, cashier, staff), the effective filter is always clamped to their own
    /// <c>user_locations</c> assignment regardless of what <c>storeIds</c> they request — "all
    /// stores" means "my own stores" for them, never the whole tenant.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] Guid[]? storeIds, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var actingUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var result = await _users.GetAllAsync(tenantId.Value, storeIds, actingUserId, ct);
        return Ok(result);
    }

    /// <summary>Returns a single user by id.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (user, error) = await _users.GetByIdAsync(tenantId.Value, id, ct);
        if (user is null) return NotFound(new { error });
        return Ok(user);
    }

    /// <summary>Creates a new user (invite). Store manager and above, or a "users.manage" TenantRole capability holder.</summary>
    [HttpPost("invite")]
    [Authorize(Policy = AppPolicies.StoreManagerOrUsersManage)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Invite(
        [FromBody] InviteUserRequest request, CancellationToken ct)
    {
        var tenantId    = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var actingUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // Pass inviter's display name for audit trail
        var inviterName = User.FindFirst("full_name")?.Value
                       ?? User.FindFirst(ClaimTypes.Name)?.Value
                       ?? User.FindFirst(ClaimTypes.Email)?.Value
                       ?? "Unknown";

        var (user, error) = await _users.InviteAsync(tenantId.Value, actingUserId, request, inviterName, ct);
        if (user is null)
            return error!.Contains("do not have permission") ? Forbid() : BadRequest(new { error });
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    /// <summary>Updates a user's profile, role, and store assignment.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.StoreManagerOrUsersManage)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var actingUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var (user, error) = await _users.UpdateAsync(tenantId.Value, actingUserId, id, request, ct);
        if (user is null)
            return error!.Contains("not found")            ? NotFound(new { error })
                 : error.Contains("do not have permission") ? Forbid()
                 : BadRequest(new { error });
        return Ok(user);
    }

    /// <summary>
    /// Updates per-user page-access overrides.
    /// Editor must have a higher role rank than the target user.
    /// Send empty Overrides dict {} to clear all overrides (revert to role defaults).
    /// </summary>
    [HttpPut("{id:guid}/permissions")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePermissions(
        Guid id,
        [FromBody] UpdatePermissionsRequest request,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var editorId   = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var editorRole = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        var (user, error) = await _users.UpdatePermissionsAsync(
            tenantId.Value, editorId, editorRole, id, request, ct);

        if (user is null)
            return error!.Contains("not found")      ? NotFound(new { error })
                 : error.Contains("not have permission") ? Forbid()
                 : BadRequest(new { error });

        return Ok(user);
    }

    /// <summary>Deactivates a user (soft delete). Store manager and above, or a "users.manage" TenantRole capability holder.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.StoreManagerOrUsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var actingUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var error = await _users.DeactivateAsync(tenantId.Value, actingUserId, id, ct);
        if (error is null) return NoContent();
        return error.Contains("do not have permission") ? Forbid() : NotFound(new { error });
    }

    /// <summary>
    /// Grants a temporary, self-expiring page-access override to a user (ADR-019).
    /// Acting user must outrank the target (same rule as PUT .../permissions); no self-grant;
    /// expiresAt must be in the future and at most 90 days out.
    /// </summary>
    [HttpPost("{id:guid}/permission-grants")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(PermissionGrantDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GrantTemporaryPermission(
        Guid id,
        [FromBody] GrantTemporaryPermissionRequest request,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var actingUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var (grant, error) = await _users.GrantTemporaryPermissionAsync(
            tenantId.Value, actingUserId, id, request.PermissionKey, request.ExpiresAt, ct);

        if (grant is null)
            return error!.Contains("not found")          ? NotFound(new { error })
                 : error.Contains("do not have permission") ? Forbid()
                 : BadRequest(new { error });

        return CreatedAtAction(nameof(GetActivePermissionGrants), new { id }, grant);
    }

    /// <summary>Returns active (non-revoked, non-expired) temporary permission grants for a user.</summary>
    [HttpGet("{id:guid}/permission-grants")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionGrantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActivePermissionGrants(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (grants, error) = await _users.GetActivePermissionGrantsAsync(tenantId.Value, id, ct);
        return grants is null ? NotFound(new { error }) : Ok(grants);
    }

    /// <summary>
    /// Early-revokes a temporary permission grant. Allowed for the original granter
    /// (revoking their own decision) or any user whose role outranks the grant recipient's.
    /// </summary>
    [HttpDelete("{id:guid}/permission-grants/{grantId:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeTemporaryPermission(Guid id, Guid grantId, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var actingUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var error = await _users.RevokeTemporaryPermissionAsync(tenantId.Value, actingUserId, grantId, ct);

        if (error is null) return NoContent();
        return error.Contains("not found")             ? NotFound(new { error })
             : error.Contains("do not have permission") ? Forbid()
             : error.Contains("cannot revoke")           ? Forbid()
             : BadRequest(new { error });
    }

    /// <summary>Returns the activity log for a specific user (last 50 entries by default).</summary>
    [HttpGet("{id:guid}/activity")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActivity(
        Guid id,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        if (limit is < 1 or > 200) limit = 50;

        var (logs, error) = await _users.GetActivityAsync(tenantId.Value, id, limit, ct);
        return error is null ? Ok(logs) : NotFound(new { error });
    }

    /// <summary>
    /// Assigns (or clears, when TenantRoleId is null) a custom capability-template role to a
    /// user (ADR-020, TASK-346). AtLeastEnterpriseAdmin-only, STRICTLY no capability bypass —
    /// otherwise a "users.manage" capability holder could grant themselves or others a
    /// higher-privilege template (anti-escalation).
    /// </summary>
    [HttpPost("{id:guid}/tenant-role")]
    [Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignTenantRole(
        Guid id, [FromBody] AssignTenantRoleRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (success, error) = await _users.AssignTenantRoleAsync(tenantId.Value, id, request.TenantRoleId, ct);

        if (success) return NoContent();
        return error == "Cannot assign an archived TenantRole."
            ? BadRequest(new { error })
            : NotFound(new { error });
    }

    /// <summary>
    /// Full-replace of a user's store-scoped location assignments (TASK-392b, Feature 2 Stage 1
    /// plumbing — enforcement RLS is Stage 3, not wired yet). AtLeastEnterpriseAdmin-only,
    /// STRICTLY no capability bypass — same anti-escalation posture as AssignTenantRole: this
    /// determines which locations' real business data a whole role will see once Stage 3 lands,
    /// so a "users.manage" capability holder must never be able to grant this to themselves or
    /// others.
    /// </summary>
    [HttpPut("{id:guid}/locations")]
    [Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
    [ProducesResponseType(typeof(UserLocationsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateLocations(
        Guid id, [FromBody] UpdateUserLocationsRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var actingUserId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var (success, error) = await _users.SetLocationsAsync(
            tenantId.Value, id, request.LocationIds, actingUserId, ct);

        if (!success)
            return error!.Contains("not found") ? NotFound(new { error }) : BadRequest(new { error });

        var (locationIds, _) = await _users.GetLocationsAsync(tenantId.Value, id, ct);
        return Ok(new UserLocationsDto(locationIds ?? []));
    }

    /// <summary>Returns the current store-scoped location assignment list for a user (TASK-392b).</summary>
    [HttpGet("{id:guid}/locations")]
    [Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
    [ProducesResponseType(typeof(UserLocationsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLocations(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (locationIds, error) = await _users.GetLocationsAsync(tenantId.Value, id, ct);
        if (locationIds is null) return NotFound(new { error });
        return Ok(new UserLocationsDto(locationIds));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
