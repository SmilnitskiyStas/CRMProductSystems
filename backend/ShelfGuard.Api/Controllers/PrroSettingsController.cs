using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Integrations;
using ShelfGuard.Application.Features.Integrations.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Per-tenant ПРРО fiscal provider settings (ADR-013).
/// GET returns masked secrets; PUT keeps stored secrets when the masked placeholder
/// is sent back (write-only secret pattern). POST /test validates connectivity
/// without opening a shift.
/// </summary>
[ApiController]
[Route("api/settings/prro")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class PrroSettingsController : ControllerBase
{
    private readonly IPrroSettingsService _prro;

    public PrroSettingsController(IPrroSettingsService prro) => _prro = prro;

    /// <summary>
    /// Returns the current ПРРО settings for this tenant with masked secrets.
    /// licenseKey = «••••» + last 4 chars when set; cashierPassword/cashierPinCode = «••••».
    /// </summary>
    /// <response code="200">Current settings (may have source="none" when nothing is configured).</response>
    [HttpGet]
    [ProducesResponseType(typeof(PrroSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var dto = await _prro.GetAsync(tenantId.Value, ct);
        return Ok(dto);
    }

    /// <summary>
    /// Creates or updates ПРРО settings for this tenant.
    /// Secret fields (licenseKey, cashierPassword, cashierPinCode): send the masked
    /// placeholder (or null) to keep the stored value; send an empty string to clear it.
    /// </summary>
    /// <response code="200">Updated settings with masked secrets.</response>
    /// <response code="400">Validation error (unknown provider, missing license key, etc.).</response>
    [HttpPut]
    [ProducesResponseType(typeof(PrroSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upsert(
        [FromBody] UpsertPrroSettingsRequest request,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (dto, error) = await _prro.UpsertAsync(tenantId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });

        return Ok(dto);
    }

    /// <summary>
    /// Tests connectivity for the supplied (or stored) ПРРО config:
    ///  1. GET cash-registers/info — verifies license key and reaches Checkbox API
    ///  2. cashier/signin or cashier/signinPinCode — verifies cashier credentials
    /// No shift is opened or closed.
    ///
    /// Body is optional: omit to test the stored/env config; supply to test
    /// not-yet-saved credentials (masked fields keep the stored value).
    /// Always returns HTTP 200; check the «ok» field for the actual result.
    /// </summary>
    /// <response code="200">Test result — check {ok, cashierOk, error}.</response>
    [HttpPost("test")]
    [ProducesResponseType(typeof(PrroTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Test(
        [FromBody] UpsertPrroSettingsRequest? candidate,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var result = await _prro.TestAsync(tenantId.Value, candidate, ct);
        return Ok(result);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
