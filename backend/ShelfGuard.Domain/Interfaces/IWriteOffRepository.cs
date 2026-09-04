using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IWriteOffRepository
{
    Task<List<WriteOff>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    // TASK-640: categoryId/minLossAmount/maxLossAmount — additive category/loss-amount range
    // filters for the frontend table filter UI. Appended at the very end (still before ct) so
    // no pre-existing parameter's positional index shifts for existing callers. Range-filters on
    // the already-stored WriteOff.TotalLossAmount column (not recomputed here).
    Task<(List<WriteOff> Items, int Total)> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize,
        Guid? categoryId = null, decimal? minLossAmount = null, decimal? maxLossAmount = null,
        CancellationToken ct = default);
    Task<WriteOff?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Batch lookup for explicit-batch write-off items — one round trip for every
    /// <c>ProductStockId</c> an approval needs, instead of one query per item. Used by
    /// <c>ApproveAsync</c> (see TASK-691 — a 39-item write-off took minutes to approve
    /// because the old per-item <c>GetStockByIdAsync</c> loop issued a query per line and
    /// kept growing the DbContext's change tracker on every one of them).
    /// </summary>
    Task<List<ProductStock>> GetStocksByIdsAsync(IReadOnlyCollection<Guid> stockIds, CancellationToken ct = default);

    /// <summary>
    /// Batches for every product in <paramref name="productIds"/> at <paramref name="storeId"/>
    /// with quantity > 0, ordered by ProductId then expiry_date ASC (nearest-first — FEFO) so
    /// callers can group by ProductId and consume in FEFO order per product. Used by
    /// <c>ApproveAsync</c> to deduct stock for write-off items that don't reference a specific
    /// batch (the only shape the mobile "quick write-off" create flow sends today — see
    /// TASK-354 audit) — fetched once for all such items instead of once per item (TASK-691).
    /// </summary>
    Task<List<ProductStock>> GetFefoOrderedForProductsAsync(IReadOnlyCollection<Guid> productIds, Guid storeId, CancellationToken ct = default);

    Task AddAsync(WriteOff writeOff, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    void Update(WriteOff writeOff);
    void UpdateStock(ProductStock stock);
    Task SaveChangesAsync(CancellationToken ct = default);
}
