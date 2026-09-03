using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D2).
/// Data access for the parallel <see cref="SupplierStock"/> / <see cref="SupplierStockMovement"/>
/// batch model. FEFO ordering mirrors <c>StockRepository.GetFefoOrderedAsync</c>.
/// </summary>
public interface ISupplierStockRepository
{
    /// <summary>Paged batches for a warehouse, FEFO-ordered (nearest expiry first).</summary>
    Task<(IReadOnlyList<SupplierStock> Items, int Total)> GetPagedAsync(
        Guid tenantId, Guid? warehouseId, Guid? supplierItemId,
        int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Active batches for a (supplierItem, warehouse) ordered by expiry_date ASC —
    /// Quantity &gt; 0, Status not in ('sold_out','archived'). Mirrors
    /// <c>StockRepository.GetFefoOrderedAsync</c>.
    /// </summary>
    Task<List<SupplierStock>> GetFefoOrderedAsync(
        Guid tenantId, Guid supplierItemId, Guid warehouseId, CancellationToken ct = default);

    Task<SupplierStock?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>True when the id is an active warehouse-type <see cref="Location"/> of this tenant.</summary>
    Task<bool> WarehouseExistsAsync(Guid tenantId, Guid warehouseId, CancellationToken ct = default);

    /// <summary>True when the id is a <see cref="SupplierItem"/> of this tenant.</summary>
    Task<bool> SupplierItemExistsAsync(Guid tenantId, Guid supplierItemId, CancellationToken ct = default);

    Task AddAsync(SupplierStock stock, CancellationToken ct = default);
    Task AddMovementAsync(SupplierStockMovement movement, CancellationToken ct = default);
    void Update(SupplierStock stock);
    Task SaveChangesAsync(CancellationToken ct = default);
}
