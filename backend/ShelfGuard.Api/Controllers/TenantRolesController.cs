using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.TenantRoles;
using ShelfGuard.Application.Features.TenantRoles.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// CRUD for custom TenantRole capability templates (ADR-020, TASK-345/346) and the capability
/// catalog that backs the "assign role" UI. Deliberately AtLeastEnterpriseAdmin-only on every
/// action, with NO capability bypass (unlike the 7 controllers RoleOrCapabilityRequirement
/// widens) — a capability-only user must never be able to create/edit a template or grant
/// itself/others more access than the template intends (anti-escalation, ADR-020 point 6).
/// </summary>
[ApiController]
[Route("api/tenant-roles")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
public sealed class TenantRolesController : ControllerBase
{
    private readonly ITenantRoleService _tenantRoles;

    public TenantRolesController(ITenantRoleService tenantRoles) => _tenantRoles = tenantRoles;

    /// <summary>Backend source-of-truth capability catalog grouped by specialty — not tenant-scoped.</summary>
    [HttpGet("capabilities")]
    [ProducesResponseType(typeof(IReadOnlyList<TenantRoleCapabilityGroupDto>), StatusCodes.Status200OK)]
    public IActionResult GetCapabilities() => Ok(_tenantRoles.GetCapabilityCatalog());

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TenantRoleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var roles = await _tenantRoles.GetAllAsync(tenantId.Value, includeInactive, ct);
        return Ok(roles);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TenantRoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (role, error) = await _tenantRoles.GetByIdAsync(tenantId.Value, id, ct);
        return role is null ? NotFound(new { error }) : Ok(role);
    }

    [HttpPost]
    [ProducesResponseType(typeof(TenantRoleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateTenantRoleRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (role, error) = await _tenantRoles.CreateAsync(tenantId.Value, GetActingUserId(), request, ct);
        if (role is null) return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = role.Id }, role);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TenantRoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantRoleRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (role, error) = await _tenantRoles.UpdateAsync(tenantId.Value, id, request, ct);
        if (role is null)
            return error == "Template not found." ? NotFound(new { error }) : BadRequest(new { error });

        return Ok(role);
    }

    /// <summary>Archives the template (soft delete — never a hard delete).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (success, error) = await _tenantRoles.DeactivateAsync(tenantId.Value, id, ct);
        return success ? NoContent() : NotFound(new { error });
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }

    private Guid? GetActingUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
