using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.SupplierInventory;
using ShelfGuard.Application.Features.SupplierInventory.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Supplier cabinet — warehouse batch inventory + manual receiving (supplier-portal
/// expansion Phase 2, plan `1-partitioned-book.md`, decisions D2/D3). Thin wrapper over
/// <see cref="ISupplierStockService"/> / <see cref="ISupplierStockReceiptService"/>.
///
/// Gated at the class by the "supplier_inventory" module (provider-granted, default-off);
/// per-action by the "warehouse_management" supplier permission. Every operation is scoped
/// to the calling supplier tenant via <see cref="ResolveTenantId"/> — RLS is the backstop.
/// </summary>
[ApiController]
[Route("api/supplier-cabinet")]
[Authorize(Policy = AppPolicies.SupplierCabinet)]
[RequireModule("supplier_inventory")]
public sealed class SupplierCabinetInventoryController : ControllerBase
{
    private readonly ISupplierStockService _stock;
    private readonly ISupplierStockReceiptService _receipts;

    public SupplierCabinetInventoryController(
        ISupplierStockService stock, ISupplierStockReceiptService receipts)
    {
        _stock = stock;
        _receipts = receipts;
    }

    // ── Stock ────────────────────────────────────────────────────────────────

    /// <summary>FEFO-ordered batches of one warehouse.</summary>
    [HttpGet("warehouses/{warehouseId:guid}/stock")]
    public async Task<IActionResult> GetStock(
        Guid warehouseId, [FromQuery] Guid? supplierItemId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!HasWarehousePermission()) return Forbid();

        var result = await _stock.GetStockAsync(
            tenantId.Value, warehouseId, supplierItemId,
            Math.Max(1, page), Math.Clamp(pageSize, 1, 200), ct);
        return Ok(result);
    }

    /// <summary>Adds a single batch to a warehouse.</summary>
    [HttpPost("warehouses/{warehouseId:guid}/stock")]
    public async Task<IActionResult> AddBatch(
        Guid warehouseId, [FromBody] AddSupplierBatchRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!HasWarehousePermission()) return Forbid();

        var (stock, error) = await _stock.AddBatchAsync(
            tenantId.Value, warehouseId, request.SupplierItemId, request.ExpiryDate,
            request.Quantity, request.BatchNumber, CurrentUserId(), ct);
        if (error is not null) return BadRequest(new { error });
        return StatusCode(StatusCodes.Status201Created, stock);
    }

    /// <summary>Adjusts a batch's quantity (stock-take correction / manual write-off).</summary>
    [HttpPost("stock/{batchId:guid}/adjust")]
    public async Task<IActionResult> AdjustBatch(
        Guid batchId, [FromBody] AdjustSupplierStockRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!HasWarehousePermission()) return Forbid();

        var (stock, error) = await _stock.AdjustAsync(
            tenantId.Value, batchId, request.Quantity, request.Reason, CurrentUserId(), ct);
        if (error == "Партію не знайдено.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(stock);
    }

    // ── Receipts ─────────────────────────────────────────────────────────────

    [HttpGet("warehouses/{warehouseId:guid}/receipts")]
    public async Task<IActionResult> ListReceipts(
        Guid warehouseId, [FromQuery] string? status, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!HasWarehousePermission()) return Forbid();

        var receipts = await _receipts.ListAsync(tenantId.Value, warehouseId, status, ct);
        return Ok(receipts);
    }

    [HttpPost("warehouses/{warehouseId:guid}/receipts")]
    public async Task<IActionResult> CreateReceipt(
        Guid warehouseId, [FromBody] CreateSupplierReceiptRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!HasWarehousePermission()) return Forbid();

        var (receipt, error) = await _receipts.CreateDraftAsync(
            tenantId.Value, warehouseId, request.Reference, request.Notes, CurrentUserId(), ct);
        if (error is not null) return BadRequest(new { error });
        return StatusCode(StatusCodes.Status201Created, receipt);
    }

    [HttpGet("receipts/{id:guid}")]
    public async Task<IActionResult> GetReceipt(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!HasWarehousePermission()) return Forbid();

        var receipt = await _receipts.GetAsync(tenantId.Value, id, ct);
        return receipt is null ? NotFound() : Ok(receipt);
    }

    [HttpPut("receipts/{id:guid}")]
    public async Task<IActionResult> UpdateReceipt(
        Guid id, [FromBody] UpdateSupplierReceiptRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!HasWarehousePermission()) return Forbid();

        var (receipt, error) = await _receipts.UpdateAsync(tenantId.Value, id, request, ct);
        if (error == "Прийом не знайдено.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(receipt);
    }

    [HttpPost("receipts/{id:guid}/lines")]
    public async Task<IActionResult> AddReceiptLine(
        Guid id, [FromBody] AddSupplierReceiptLineRequest request, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!HasWarehousePermission()) return Forbid();

        var (receipt, error) = await _receipts.AddLineAsync(tenantId.Value, id, request, ct);
        if (error == "Прийом не знайдено.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(receipt);
    }

    [HttpDelete("receipts/{id:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> RemoveReceiptLine(Guid id, Guid lineId, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!HasWarehousePermission()) return Forbid();

        var (receipt, error) = await _receipts.RemoveLineAsync(tenantId.Value, id, lineId, ct);
        if (error is "Прийом не знайдено." or "Позицію не знайдено.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(receipt);
    }

    [HttpPost("receipts/{id:guid}/finalize")]
    public async Task<IActionResult> FinalizeReceipt(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();
        if (!HasWarehousePermission()) return Forbid();

        var (receipt, error) = await _receipts.ReceiveAsync(tenantId.Value, id, CurrentUserId(), ct);
        if (error == "Прийом не знайдено.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });
        return Ok(receipt);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private bool HasWarehousePermission() =>
        SupplierPermissionAuthorization.HasPermission(User, SupplierPermissions.WarehouseManagement);

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private Guid CurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
