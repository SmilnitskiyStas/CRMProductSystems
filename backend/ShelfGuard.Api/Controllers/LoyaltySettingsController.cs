using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Application.Services;
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
    private readonly ITenantContext _tenantContext;

    public LoyaltySettingsController(ILoyaltyService loyalty, ITenantContext tenantContext)
    {
        _loyalty = loyalty;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(LoyaltyProgramSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
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
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (dto, error) = await _loyalty.UpsertSettingsAsync(tenantId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });

        return Ok(dto);
    }

    [HttpPost("reset-balances")]
    public async Task<IActionResult> ResetBalances(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();
        var affected = await _loyalty.ResetAllBonusBalancesAsync(tenantId.Value, ct);
        return Ok(new { affectedMemberships = affected });
    }
}
