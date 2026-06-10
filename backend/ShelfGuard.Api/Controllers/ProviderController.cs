using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Provider;
using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Super-admin panel for the platform owner (role = provider).
/// All endpoints require ProviderOnly policy — no tenant scoping.
/// </summary>
[ApiController]
[Route("api/provider")]
[Authorize(Policy = AppPolicies.ProviderOnly)]
public sealed class ProviderController : ControllerBase
{
    private readonly IProviderService _provider;

    public ProviderController(IProviderService provider)
    {
        _provider = provider;
    }

    // ── Tenants ─────────────────────────────────────────────────────────────

    /// <summary>Lists all tenants with user/store/expired-batch counts.</summary>
    [HttpGet("tenants")]
    [ProducesResponseType(typeof(IReadOnlyList<TenantSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenants(CancellationToken ct)
    {
        var tenants = await _provider.GetTenantsAsync(ct);
        return Ok(tenants);
    }

    /// <summary>Returns full details for a single tenant.</summary>
    [HttpGet("tenants/{id:guid}")]
    [ProducesResponseType(typeof(TenantDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenant(Guid id, CancellationToken ct)
    {
        var (tenant, error) = await _provider.GetTenantAsync(id, ct);
        return error is null ? Ok(tenant) : NotFound(new { error });
    }

    // ── Plan & modules ──────────────────────────────────────────────────────

    /// <summary>Changes the billing plan for a tenant.</summary>
    [HttpPut("tenants/{id:guid}/plan")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlan(
        Guid id,
        [FromBody] UpdatePlanRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Plan))
            return BadRequest(new { error = "Plan is required." });

        var error = await _provider.UpdatePlanAsync(id, request.Plan, ct);
        return error is null
            ? NoContent()
            : (error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(new { error })
                : BadRequest(new { error }));
    }

    /// <summary>Replaces the list of enabled modules for a tenant.</summary>
    [HttpPut("tenants/{id:guid}/modules")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateModules(
        Guid id,
        [FromBody] UpdateModulesRequest request,
        CancellationToken ct)
    {
        var modules = request.Modules ?? [];

        var error = await _provider.UpdateModulesAsync(id, modules, ct);
        return error is null
            ? NoContent()
            : (error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(new { error })
                : BadRequest(new { error }));
    }

    // ── Impersonation ───────────────────────────────────────────────────────

    /// <summary>
    /// Starts a 60-minute impersonation session for the given tenant.
    /// Returns a short-lived JWT scoped to that tenant (role = enterprise_admin).
    /// Logs the event in activity_logs for audit trail.
    /// </summary>
    [HttpPost("tenants/{id:guid}/impersonate")]
    [ProducesResponseType(typeof(ImpersonateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Impersonate(Guid id, CancellationToken ct)
    {
        var providerId    = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var providerEmail = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

        if (!Guid.TryParse(providerId, out var providerGuid))
            return Unauthorized();

        var (response, error) = await _provider.ImpersonateAsync(providerGuid, providerEmail, id, ct);
        return error is null
            ? Ok(response)
            : (error.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(new { error })
                : BadRequest(new { error }));
    }

    /// <summary>
    /// Ends an impersonation session (client-side signal).
    /// The server returns 204 — the client discards the impersonation token.
    /// </summary>
    [HttpDelete("tenants/{id:guid}/impersonate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult EndImpersonate(Guid id) => NoContent();

    // ── Health & observability ──────────────────────────────────────────────

    /// <summary>Returns platform-wide health metrics.</summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(ProviderHealthDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var health = await _provider.GetHealthAsync(ct);
        return Ok(health);
    }

    /// <summary>Returns recent cross-tenant activity log entries.</summary>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(IReadOnlyList<ProviderLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogs(
        [FromQuery] int limit = 100,
        CancellationToken ct  = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var logs = await _provider.GetLogsAsync(limit, ct);
        return Ok(logs);
    }
}
