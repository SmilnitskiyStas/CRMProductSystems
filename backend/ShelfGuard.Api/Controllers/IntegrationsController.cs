using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Integrations;
using ShelfGuard.Application.Features.Integrations.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Manages per-tenant external service integrations (Telegram, Resend, Webhook, ПРРО, IoT).
/// Tenant admins (AtLeastStoreManager) and provider can access; ADR-020 (TASK-346) additionally
/// admits a TenantRole capability holder ("integrations.view"/"integrations.manage") regardless
/// of role rank — per-action policies below, no class-level attribute (see AppPolicies.Configure).
/// GET list/detail is read-only (integrations.view). POST/DELETE require integrations.manage
/// (or the pre-existing AtLeastStoreManager+ role gate, unchanged).
/// </summary>
[ApiController]
[Route("api/integrations")]
public sealed class IntegrationsController : ControllerBase
{
    private readonly IIntegrationService _integrations;

    public IntegrationsController(IIntegrationService integrations)
        => _integrations = integrations;

    /// <summary>Returns a summary list of all configured integrations for the current tenant.</summary>
    [HttpGet]
    [Authorize(Policy = AppPolicies.IntegrationsViewOrCapability)]
    [ProducesResponseType(typeof(IReadOnlyList<IntegrationSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _integrations.GetAllAsync(tenantId.Value, ct);
        return Ok(result);
    }

    /// <summary>Returns full config for a specific service. Config may contain credentials.</summary>
    [HttpGet("{service}")]
    [Authorize(Policy = AppPolicies.IntegrationsViewOrCapability)]
    [ProducesResponseType(typeof(IntegrationConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByService(string service, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (config, error) = await _integrations.GetByServiceAsync(tenantId.Value, service, ct);

        if (error is not null) return BadRequest(new { error });
        if (config is null)    return NotFound(new { error = $"Integration '{service}' is not configured." });

        return Ok(config);
    }

    /// <summary>Creates or updates the config for a service. Merges credentials into JSONB.</summary>
    [HttpPut("{service}")]
    [Authorize(Policy = AppPolicies.IntegrationsManageOrCapability)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upsert(
        string service,
        [FromBody] UpsertIntegrationRequest request,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var error = await _integrations.UpsertAsync(tenantId.Value, service, request, ct);
        return error is null ? NoContent() : BadRequest(new { error });
    }

    /// <summary>Removes the integration config entirely (clears credentials).</summary>
    [HttpDelete("{service}")]
    [Authorize(Policy = AppPolicies.IntegrationsManageOrCapability)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(string service, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (ok, error) = await _integrations.DeleteAsync(tenantId.Value, service, ct);

        if (!ok && error!.StartsWith("Unknown")) return BadRequest(new { error });
        if (!ok) return NotFound(new { error });

        return NoContent();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
