using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Infrastructure.Authorization;
using System.Security.Claims;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// POS endpoints: shift management and sales.
/// All endpoints require CanAccessPos (cashier, storekeeper, store_manager, network_manager, enterprise_admin).
/// Offline-first: sale DB commit succeeds even when fiscal provider is unavailable;
/// fiscalization status is returned in every response.
/// </summary>
[ApiController]
[Route("api/pos")]
[Authorize(Policy = AppPolicies.CanAccessPos)]
// NOTE: no class-level [RequireModule("pos")]: the legacy shift/sale endpoints (mobile POS) and the
// worker fiscalization-retry cron (service account of a tenant that may lack "pos") never had a
// module gate; only the cashier-app endpoints below are gated.
[AllowOperatorToken]
public sealed class PosController : ControllerBase
{
    private readonly IPosService _pos;
    private readonly IPosSyncService _sync;

    public PosController(IPosService pos, IPosSyncService sync)
    {
        _pos = pos;
        _sync = sync;
    }

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

        // Register operator token: the shift is bound to the register and its store (claims win
        // over whatever the client sent).
        var registerId = GetRegisterId();
        if (registerId is not null && GetStoreId() is Guid tokenStore)
            request = request with { StoreId = tokenStore };

        var (shift, error, status) = await _pos.OpenShiftAsync(
            tenantId.Value, cashierId.Value, request, ct, registerId);

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

        var shift = await _pos.GetCurrentShiftAsync(tenantId.Value, ct, GetRegisterId());
        return shift is null ? NotFound() : Ok(shift);
    }

    /// <summary>
    /// Closes the current open shift and generates a Z-report via the fiscal provider.
    /// Returns 404 when no shift is open.
    ///
    /// TASK-356: body is optional. Omit it (or send no ActualClosingCash) to close
    /// without cash reconciliation, unchanged from before. Send
    /// { "actualClosingCash": 1234.56 } (cash counted by the cashier) to reconcile —
    /// the response's ExpectedCashAmount/CashDiscrepancy fields are populated
    /// (ExpectedCashAmount = OpeningCash + this shift's cash-only sales; discrepancy =
    /// actual - expected; positive = surplus, negative = shortage). Returns 400 if
    /// ActualClosingCash is negative.
    /// </summary>
    [HttpPost("shifts/close")]
    [ProducesResponseType(typeof(ShiftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloseShift([FromBody] CloseShiftRequest? request, CancellationToken ct)
    {
        var (tenantId, _) = GetContext();
        if (tenantId is null) return Forbid();

        var (shift, error, status) = await _pos.CloseShiftAsync(tenantId.Value, request, ct, GetRegisterId());

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

        var (sale, error, status) = await _pos.CreateSaleAsync(
            tenantId.Value, cashierId.Value, request, ct, GetRegisterId());

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

    /// <summary>
    /// Offline sync: submits queued sales (max 100). Every item carries a ClientTransactionId and
    /// is idempotent; the response has one result per item (created | duplicate | failed) and the
    /// overall status is 200 even when some items failed.
    /// </summary>
    [HttpPost("sales/batch")]
    [RequireModule("pos")]
    [ProducesResponseType(typeof(BatchSalesResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSalesBatch([FromBody] BatchSalesRequest request, CancellationToken ct)
    {
        var (tenantId, cashierId) = GetContext();
        if (tenantId is null || cashierId is null) return Forbid();

        var (result, error, status) = await _pos.CreateSalesBatchAsync(
            tenantId.Value, cashierId.Value, request, ct, GetRegisterId());

        return error is null ? Ok(result) : StatusCode(status ?? 400, new { error });
    }

    /// <summary>
    /// Delta catalog for register apps: items changed after (since, afterId), ordered by
    /// (updatedAt, id). Page with nextSince/nextAfterId while hasMore; inactive items are included
    /// so the till can drop them. Omit since for a full initial download.
    /// </summary>
    [HttpGet("sync/catalog")]
    [RequireModule("pos")]
    [ProducesResponseType(typeof(SyncCatalogResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncCatalog(
        [FromQuery] DateTime? since, [FromQuery] Guid? afterId, [FromQuery] int? limit, CancellationToken ct)
    {
        var (tenantId, _) = GetContext();
        if (tenantId is null) return Forbid();

        return Ok(await _sync.GetCatalogAsync(since?.ToUniversalTime(), afterId, limit, ct));
    }

    /// <summary>
    /// Published promotion rules for the register's store (valid now or later), with an etag.
    /// Pass the last etag: when nothing changed the response is { etag, notModified: true, rules: [] }.
    /// Operator tokens always get their register's store; other POS users pass storeId (or their
    /// token's store_id is used).
    /// </summary>
    [HttpGet("sync/promotions")]
    [RequireModule("pos")]
    [ProducesResponseType(typeof(SyncPromotionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncPromotions(
        [FromQuery] string? etag, [FromQuery] Guid? storeId, CancellationToken ct)
    {
        var (tenantId, _) = GetContext();
        if (tenantId is null) return Forbid();

        var (result, error, status) = await _sync.GetPromotionsAsync(
            tenantId.Value, GetRegisterId(), storeId ?? GetStoreId(), etag, ct);
        if (error is not null)
            return status == 403 ? Forbid() : StatusCode(status ?? 400, new { error });
        return Ok(result);
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

    // ── Fiscalization retry (worker-facing) ────────────────────────────────

    /// <summary>
    /// Returns all pos_transactions with FiscalStatus='pending_fiscalization'
    /// older than 30 seconds and with fewer than 5 retry attempts.
    /// Used by the fiscalization-retry worker job (TASK-069).
    /// Auth: AtLeastStoreManager (same service account the worker uses to login).
    /// </summary>
    [HttpGet("sales/pending-fiscalization")]
    [AllowWorkerToken]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(IReadOnlyList<PendingFiscalizationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingFiscalization(CancellationToken ct)
    {
        var result = await _pos.GetPendingFiscalizationAsync(maxRetries: 5, olderThanSeconds: 30, ct);
        return Ok(result);
    }

    /// <summary>
    /// Re-attempts fiscalization for a single pos_transaction.
    /// Increments RetryCount; sets fiscalization_failed when RetryCount reaches 5.
    /// Used by the fiscalization-retry worker job (TASK-069).
    /// Auth: AtLeastStoreManager (same service account the worker uses to login).
    /// </summary>
    [HttpPost("sales/{id:guid}/fiscalize")]
    [AllowWorkerToken]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(FiscalizeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FiscalizeTransaction(Guid id, CancellationToken ct)
    {
        var result = await _pos.FiscalizeTransactionAsync(id, ct);

        if (!result.Ok && result.Error == "Transaction not found.")
            return NotFound(new { error = result.Error });

        return Ok(result);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>register_id claim of a cashier-app operator token; null for regular user tokens.</summary>
    private Guid? GetRegisterId() =>
        Guid.TryParse(User.FindFirst("register_id")?.Value, out var id) && id != Guid.Empty ? id : null;

    private Guid? GetStoreId() =>
        Guid.TryParse(User.FindFirst("store_id")?.Value, out var id) && id != Guid.Empty ? id : null;

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
