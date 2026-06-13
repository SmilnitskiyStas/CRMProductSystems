using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// POS endpoints: shift management and sales.
/// All endpoints require at least the CanReceiveStock role (storekeeper and above).
/// Offline-first: sale DB commit succeeds even when fiscal provider is unavailable;
/// fiscalization status is returned in every response.
/// </summary>
[ApiController]
[Route("api/pos")]
[Authorize(Policy = AppPolicies.CanReceiveStock)]
public sealed class PosController : ControllerBase
{
    private readonly IPosService _pos;

    public PosController(IPosService pos) => _pos = pos;

    // ── Shifts ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a new fiscal shift for the current tenant.
    /// Returns 409 if a shift is already open.
    /// Fiscal provider failure → shift created locally with fiscalStatus=open_failed (offline-first).
    /// </summary>
    [HttpPost("shifts/open")]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OpenShift([FromBody] OpenShiftRequest request, CancellationToken ct)
    {
        var (tenantId, cashierId) = GetContext();
        if (tenantId is null || cashierId is null) return Forbid();

        var (shift, error, status) = await _pos.OpenShiftAsync(tenantId.Value, cashierId.Value, request, ct);

        if (error is not null)
            return status == 409 ? Conflict(new { error }) : BadRequest(new { error });

        return Ok(shift);
    }

    /// <summary>Returns the current open shift or 404 when no shift is active.</summary>
    [HttpGet("shifts/current")]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentShift(CancellationToken ct)
    {
        var (tenantId, _) = GetContext();
        if (tenantId is null) return Forbid();

        var shift = await _pos.GetCurrentShiftAsync(tenantId.Value, ct);
        return shift is null ? NotFound() : Ok(shift);
    }

    /// <summary>
    /// Closes the current open shift and generates a Z-report via the fiscal provider.
    /// Returns 404 when no shift is open.
    /// </summary>
    [HttpPost("shifts/close")]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloseShift(CancellationToken ct)
    {
        var (tenantId, _) = GetContext();
        if (tenantId is null) return Forbid();

        var (shift, error, status) = await _pos.CloseShiftAsync(tenantId.Value, ct);

        if (error is not null)
            return status == 404 ? NotFound(new { error }) : BadRequest(new { error });

        return Ok(shift);
    }

    // ── Sales ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a sale (one DB transaction: FEFO write-down + stock_events + pos_transaction).
    /// Fiscalization is async and never blocks; fiscalStatus reflects the current state.
    ///
    /// HTTP 423 (Locked) is returned when any item in the cart is fully expired.
    /// HTTP 409 when the shift is closed or not found.
    /// </summary>
    [HttpPost("sales")]
    [ProducesResponseType(typeof(SaleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(object), StatusCodes.Status423Locked)]
    public async Task<IActionResult> CreateSale([FromBody] CreateSaleRequest request, CancellationToken ct)
    {
        var (tenantId, cashierId) = GetContext();
        if (tenantId is null || cashierId is null) return Forbid();

        var (sale, error, status) = await _pos.CreateSaleAsync(tenantId.Value, cashierId.Value, request, ct);

        if (error is not null)
        {
            return status switch
            {
                409 => Conflict(new { error }),
                423 => StatusCode(StatusCodes.Status423Locked, new { error }),
                _ => BadRequest(new { error })
            };
        }

        return CreatedAtAction(nameof(GetSales), new { shiftId = sale!.ShiftId }, sale);
    }

    /// <summary>Lists all sales for a given shift.</summary>
    [HttpGet("sales")]
    [ProducesResponseType(typeof(SalesListDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSales([FromQuery] Guid shiftId, CancellationToken ct)
    {
        var (tenantId, _) = GetContext();
        if (tenantId is null) return Forbid();

        var result = await _pos.GetSalesForShiftAsync(tenantId.Value, shiftId, ct);
        return Ok(result);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private (Guid? TenantId, Guid? UserId) GetContext()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        var tenantIdStr = User.FindFirst("tenant_id")?.Value;

        Guid.TryParse(userIdStr, out var userId);
        Guid.TryParse(tenantIdStr, out var tenantId);

        return (
            tenantId == Guid.Empty ? null : tenantId,
            userId == Guid.Empty ? null : userId
        );
    }
}
