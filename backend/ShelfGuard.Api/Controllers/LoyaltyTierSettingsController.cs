using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Per-tenant loyalty tier ladder configuration (TASK-615) — same authorization tier and
/// upsert-shape convention as <see cref="LoyaltySettingsController"/>. GET returns the current
/// ladder ordered by SortOrder (empty, never null, when the tenant has none yet); PUT
/// bulk-replaces the whole ladder — see <see cref="ILoyaltyService.UpsertTierLadderAsync"/> for
/// how submitted rows are matched against existing ones.
/// </summary>
[ApiController]
[Route("api/settings/loyalty/tiers")]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
[RequireModule("mobile_app")] // TASK-674: "Застосунок" admin section
public sealed class LoyaltyTierSettingsController : ControllerBase
{
    private readonly ILoyaltyService _loyalty;
    private readonly ITenantContext _tenantContext;

    public LoyaltyTierSettingsController(ILoyaltyService loyalty, ITenantContext tenantContext)
    {
        _loyalty = loyalty;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LoyaltyTierDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var tiers = await _loyalty.GetTierLadderAsync(tenantId.Value, ct);
        return Ok(tiers);
    }

    [HttpPut]
    [ProducesResponseType(typeof(IReadOnlyList<LoyaltyTierDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upsert([FromBody] List<UpsertTierRequest> tiers, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var (result, error) = await _loyalty.UpsertTierLadderAsync(tenantId.Value, tiers, ct);
        if (error is not null) return BadRequest(new { error });

        return Ok(result);
    }

    [HttpPost("{id:guid}/image")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file uploaded." });
        if (file.Length > 5 * 1024 * 1024) return BadRequest(new { error = "File too large. Max 5 MB." });

        using var stream = file.OpenReadStream();
        var (url, error) = await _loyalty.UploadTierImageAsync(tenantId.Value, id, stream, file.FileName, ct);
        if (error == "Tier not found.") return NotFound(new { error });
        return error is null ? Ok(new { imageUrl = url }) : BadRequest(new { error });
    }
}
