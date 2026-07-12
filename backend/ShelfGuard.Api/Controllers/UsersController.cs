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
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    /// <summary>Returns all users for the current tenant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _users.GetAllAsync(tenantId.Value, ct);
        return Ok(result);
    }

    /// <summary>Returns a single user by id.</summary>
    [HttpGet("{id:guid}")]
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

    /// <summary>Creates a new user (invite). Enterprise admin only.</summary>
    [HttpPost("invite")]
    [Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invite(
        [FromBody] InviteUserRequest request, CancellationToken ct)
    {
        var tenantId    = ResolveTenantId();
        if (tenantId is null) return Forbid();

        // Pass inviter's display name for audit trail
        var inviterName = User.FindFirst("full_name")?.Value
                       ?? User.FindFirst(ClaimTypes.Name)?.Value
                       ?? User.FindFirst(ClaimTypes.Email)?.Value
                       ?? "Unknown";

        var (user, error) = await _users.InviteAsync(tenantId.Value, request, inviterName, ct);
        if (user is null) return BadRequest(new { error });
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    /// <summary>Updates a user's profile, role, and store assignment.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (user, error) = await _users.UpdateAsync(tenantId.Value, id, request, ct);
        if (user is null)
            return error!.Contains("not found") ? NotFound(new { error }) : BadRequest(new { error });
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

    /// <summary>Deactivates a user (soft delete). Enterprise admin only.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var error = await _users.DeactivateAsync(tenantId.Value, id, ct);
        return error is null ? NoContent() : NotFound(new { error });
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

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
