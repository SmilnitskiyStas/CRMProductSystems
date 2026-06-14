using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Admin;
using ShelfGuard.Application.Features.Admin.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// SaaS Admin Panel — tenant lifecycle management.
/// All endpoints require ProviderOnly policy (role = provider); no tenant scoping.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = AppPolicies.ProviderOnly)]
public sealed class AdminController : ControllerBase
{
    private readonly ITenantAdminService _admin;

    public AdminController(ITenantAdminService admin)
    {
        _admin = admin;
    }

    // ── List ─────────────────────────────────────────────────────────────────

    /// <summary>Returns all tenants with usage statistics.</summary>
    [HttpGet("tenants")]
    [ProducesResponseType(typeof(IReadOnlyList<TenantDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenants(CancellationToken ct)
    {
        var tenants = await _admin.GetAllTenantsAsync(ct);
        return Ok(tenants);
    }

    // ── Single tenant ────────────────────────────────────────────────────────

    /// <summary>Returns details and usage stats for a single tenant.</summary>
    [HttpGet("tenants/{id:guid}")]
    [ProducesResponseType(typeof(TenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenant(Guid id, CancellationToken ct)
    {
        var tenant = await _admin.GetTenantAsync(id, ct);
        return tenant is not null ? Ok(tenant) : NotFound();
    }

    // ── Create ───────────────────────────────────────────────────────────────

    /// <summary>Creates a new tenant and its first EnterpriseAdmin user.</summary>
    [HttpPost("tenants")]
    [ProducesResponseType(typeof(TenantDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTenant(
        [FromBody] CreateTenantRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });
        if (string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest(new { error = "Slug is required." });
        if (string.IsNullOrWhiteSpace(request.AdminEmail))
            return BadRequest(new { error = "AdminEmail is required." });
        if (string.IsNullOrWhiteSpace(request.AdminPassword))
            return BadRequest(new { error = "AdminPassword is required." });

        var (tenant, error) = await _admin.CreateTenantAsync(request, ct);

        if (error is not null)
        {
            return error.Contains("already taken", StringComparison.OrdinalIgnoreCase)
                ? Conflict(new { error })
                : BadRequest(new { error });
        }

        return CreatedAtAction(nameof(GetTenant), new { id = tenant!.Id }, tenant);
    }

    // ── Plan ─────────────────────────────────────────────────────────────────

    /// <summary>Changes the billing plan for a tenant.</summary>
    [HttpPatch("tenants/{id:guid}/plan")]
    [ProducesResponseType(typeof(TenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlan(
        Guid id,
        [FromBody] UpdatePlanRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Plan))
            return BadRequest(new { error = "Plan is required." });

        var (tenant, error) = await _admin.UpdatePlanAsync(id, request.Plan, ct);

        if (error is not null)
        {
            return error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(new { error })
                : BadRequest(new { error });
        }

        return Ok(tenant);
    }

    // ── Modules ──────────────────────────────────────────────────────────────

    /// <summary>Replaces the list of enabled modules for a tenant.</summary>
    [HttpPatch("tenants/{id:guid}/modules")]
    [ProducesResponseType(typeof(TenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateModules(
        Guid id,
        [FromBody] UpdateModulesRequest request,
        CancellationToken ct)
    {
        var modules = request.Modules ?? [];

        var (tenant, error) = await _admin.UpdateModulesAsync(id, modules, ct);

        if (error is not null)
        {
            return error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(new { error })
                : BadRequest(new { error });
        }

        return Ok(tenant);
    }

    // ── Activate / Deactivate ─────────────────────────────────────────────────

    /// <summary>Activates a previously deactivated tenant.</summary>
    [HttpPost("tenants/{id:guid}/activate")]
    [ProducesResponseType(typeof(TenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var (tenant, error) = await _admin.ActivateAsync(id, ct);
        return error is null ? Ok(tenant) : NotFound(new { error });
    }

    /// <summary>Deactivates a tenant (soft disable — data preserved).</summary>
    [HttpPost("tenants/{id:guid}/deactivate")]
    [ProducesResponseType(typeof(TenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var (tenant, error) = await _admin.DeactivateAsync(id, ct);
        return error is null ? Ok(tenant) : NotFound(new { error });
    }
}
