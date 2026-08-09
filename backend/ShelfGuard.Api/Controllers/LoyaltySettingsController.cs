using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Per-tenant loyalty program configuration (Loyalty Фаза 0, TASK-405) — one row per tenant,
/// same upsert shape as PrroSettingsController. GET returns proposed defaults
/// (3%/50%/0/30s/barcode, enabled) when the tenant has never saved a row, so the Settings page
/// has something sensible to show before first save.
/// </summary>
[ApiController]
[Route("api/settings/loyalty")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
public sealed class LoyaltySettingsController : ControllerBase
{
    private readonly ILoyaltyService _loyalty;

    public LoyaltySettingsController(ILoyaltyService loyalty) => _loyalty = loyalty;

    [HttpGet]
    [ProducesResponseType(typeof(LoyaltyProgramSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var dto = await _loyalty.GetSettingsAsync(tenantId.Value, ct);
        return Ok(dto);
    }

    [HttpPut]
    [ProducesResponseType(typeof(LoyaltyProgramSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upsert([FromBody] UpsertLoyaltyProgramSettingsRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (dto, error) = await _loyalty.UpsertSettingsAsync(tenantId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });

        return Ok(dto);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
