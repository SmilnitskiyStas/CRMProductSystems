using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Settings;
using ShelfGuard.Application.Features.Settings.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Read-only module/business-type info for the calling tenant (ADR-015).
/// Activation/deactivation is provider-only via /api/admin/tenants/{id}/modules.
/// </summary>
[ApiController]
[Route("api/settings/modules")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
public sealed class ModulesSettingsController : ControllerBase
{
    private readonly IModulesSettingsService _modules;

    public ModulesSettingsController(IModulesSettingsService modules) => _modules = modules;

    [HttpGet]
    [ProducesResponseType(typeof(ModulesSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var dto = await _modules.GetAsync(tenantId.Value, ct);
        return dto is not null ? Ok(dto) : Forbid();
    }

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
