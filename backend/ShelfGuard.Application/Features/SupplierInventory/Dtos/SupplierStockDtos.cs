namespace ShelfGuard.Application.Features.SupplierInventory.Dtos;

// ═══════════════════════════════════════════════════════════════════════════
// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decisions D2, D3).
// Supplier warehouse batch inventory + manual "what actually arrived" receiving.
// Parallel to the retail Stock / Receipts DTOs — the supplier catalog is SupplierItem
// (nullable ItemId), not Item, and there is no zone / store-scope surface.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>One FEFO batch of a supplier warehouse.</summary>
public sealed record SupplierStockDto(
    Guid Id,
    Guid SupplierItemId,
    string SupplierItemName,
    Guid WarehouseId,
    string WarehouseName,
    DateOnly ExpiryDate,
    int DaysLeft,
    decimal Quantity,
    decimal QuantityInitial,
    string? BatchNumber,
    string Status,
    string? SourceType,
    DateTime AddedAt,
    DateTime LastCheckedAt);

public sealed record AddSupplierBatchRequest(
    Guid SupplierItemId,
    DateOnly ExpiryDate,
    decimal Quantity,
    string? BatchNumber);

public sealed record AdjustSupplierStockRequest(
    decimal Quantity,
    string? Reason);

// ── Receiving ────────────────────────────────────────────────────────────

public sealed record SupplierStockReceiptDto(
    Guid Id,
    Guid WarehouseId,
    string WarehouseName,
    string Status,
    string? Reference,
    string? Notes,
    DateTime? ReceivedAt,
    DateTime CreatedAt,
    IReadOnlyList<SupplierStockReceiptItemDto> Items);

public sealed record SupplierStockReceiptItemDto(
    Guid Id,
    Guid SupplierItemId,
    string SupplierItemName,
    DateOnly? ExpiryDate,
    decimal Quantity,
    string? BatchNumber,
    decimal? UnitCost,
    string? Notes);

public sealed record CreateSupplierReceiptRequest(
    string? Reference,
    string? Notes);

public sealed record UpdateSupplierReceiptRequest(
    Guid WarehouseId,
    string? Reference,
    string? Notes);

public sealed record AddSupplierReceiptLineRequest(
    Guid SupplierItemId,
    DateOnly? ExpiryDate,
    decimal Quantity,
    string? BatchNumber,
    decimal? UnitCost,
    string? Notes);

// ── FEFO consumption (Phase 3 shipping consumes this; Phase 2 lands + tests it) ──

/// <summary>
/// Result of walking supplier warehouse batches nearest-expiry-first to cover a quantity.
/// A non-zero <see cref="Shortfall"/> is NOT an error — Phase 3 shipping allows a shortfall
/// with a warning (user decision 2026-09-02: "нестача залишку при відвантаженні — дозволити").
/// </summary>
public sealed record SupplierFefoConsumeResult(
    decimal QuantityConsumed,
    decimal Shortfall,
    IReadOnlyList<SupplierBatchConsumed> BatchesConsumed);

public sealed record SupplierBatchConsumed(
    Guid BatchId,
    string? BatchNumber,
    DateOnly ExpiryDate,
    decimal Qty);
