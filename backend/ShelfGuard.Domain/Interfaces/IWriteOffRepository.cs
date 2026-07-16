using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IWriteOffRepository
{
    Task<List<WriteOff>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    Task<(List<WriteOff> Items, int Total)> GetPagedAsync(Guid? storeId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<WriteOff?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProductStock?> GetStockByIdAsync(Guid stockId, CancellationToken ct = default);

    /// <summary>
    /// Batches for (productId, storeId) with quantity > 0, ordered by expiry_date ASC
    /// (nearest-first — FEFO). Used by <c>ApproveAsync</c> to deduct stock for write-off
    /// items that don't reference a specific batch (the only shape the mobile "quick
    /// write-off" create flow sends today — see TASK-354 audit).
    /// </summary>
    Task<List<ProductStock>> GetFefoOrderedAsync(Guid productId, Guid storeId, CancellationToken ct = default);

    Task AddAsync(WriteOff writeOff, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    void Update(WriteOff writeOff);
    void UpdateStock(ProductStock stock);
    Task SaveChangesAsync(CancellationToken ct = default);
}
