using ShelfGuard.Application.Features.SupplierInventory.Dtos;

namespace ShelfGuard.Application.Features.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D3).
/// Manual "what actually arrived" receiving: a draft receipt against one warehouse,
/// N lines (one per expiry/batch, lines may repeat a SupplierItem), then finalize —
/// each line becomes a <c>SupplierStock</c> batch + a <c>receipt</c> movement.
/// </summary>
public interface ISupplierStockReceiptService
{
    Task<(SupplierStockReceiptDto? Receipt, string? Error)> CreateDraftAsync(
        Guid tenantId, Guid warehouseId, string? reference, string? notes,
        Guid createdBy, CancellationToken ct = default);

    Task<SupplierStockReceiptDto?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<List<SupplierStockReceiptDto>> ListAsync(
        Guid tenantId, Guid? warehouseId, string? status, CancellationToken ct = default);

    Task<(SupplierStockReceiptDto? Receipt, string? Error)> UpdateAsync(
        Guid tenantId, Guid id, UpdateSupplierReceiptRequest request, CancellationToken ct = default);

    Task<(SupplierStockReceiptDto? Receipt, string? Error)> AddLineAsync(
        Guid tenantId, Guid receiptId, AddSupplierReceiptLineRequest request, CancellationToken ct = default);

    Task<(SupplierStockReceiptDto? Receipt, string? Error)> RemoveLineAsync(
        Guid tenantId, Guid receiptId, Guid lineId, CancellationToken ct = default);

    Task<(SupplierStockReceiptDto? Receipt, string? Error)> ReceiveAsync(
        Guid tenantId, Guid receiptId, Guid receivedBy, CancellationToken ct = default);
}
