using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D3).
/// Data access for the parallel <see cref="SupplierStockReceipt"/> intake documents.
/// </summary>
public interface ISupplierStockReceiptRepository
{
    Task<List<SupplierStockReceipt>> ListAsync(
        Guid tenantId, Guid? warehouseId, string? status, CancellationToken ct = default);

    Task<SupplierStockReceipt?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task AddAsync(SupplierStockReceipt receipt, CancellationToken ct = default);

    /// <summary>Adds a <see cref="SupplierStock"/> batch produced by finalizing a receipt line.</summary>
    Task AddStockAsync(SupplierStock stock, CancellationToken ct = default);
    Task AddMovementAsync(SupplierStockMovement movement, CancellationToken ct = default);

    void Update(SupplierStockReceipt receipt);
    void AddItem(SupplierStockReceiptItem item);
    void RemoveItem(SupplierStockReceiptItem item);
    Task SaveChangesAsync(CancellationToken ct = default);
}
