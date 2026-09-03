using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.SupplierInventory.Dtos;

namespace ShelfGuard.Application.Features.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D2).
/// Batch inventory for a supplier's own warehouses: list (FEFO order), add a batch,
/// adjust a batch quantity, and FEFO-consume across batches (used by Phase 3 shipping).
/// </summary>
public interface ISupplierStockService
{
    Task<PagedResult<SupplierStockDto>> GetStockAsync(
        Guid tenantId, Guid? warehouseId, Guid? supplierItemId,
        int page, int pageSize, CancellationToken ct = default);

    Task<(SupplierStockDto? Stock, string? Error)> AddBatchAsync(
        Guid tenantId, Guid warehouseId, Guid supplierItemId, DateOnly expiryDate,
        decimal quantity, string? batchNumber, Guid addedBy, CancellationToken ct = default);

    Task<(SupplierStockDto? Stock, string? Error)> AdjustAsync(
        Guid tenantId, Guid batchId, decimal newQuantity, string? reason,
        Guid performedBy, CancellationToken ct = default);

    /// <summary>
    /// Walks the (supplierItem, warehouse) batches nearest-expiry-first, decrements each,
    /// writes one <c>ship</c> movement per touched batch. A non-zero
    /// <see cref="SupplierFefoConsumeResult.Shortfall"/> is returned, not thrown — Phase 3
    /// shipping allows a shortfall with a warning.
    /// </summary>
    Task<SupplierFefoConsumeResult> FefoConsumeAsync(
        Guid tenantId, Guid supplierItemId, Guid warehouseId, decimal qty,
        string? referenceType, Guid? referenceId, Guid performedBy, CancellationToken ct = default);
}
