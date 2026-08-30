using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IReceiptRepository
{
    Task<List<StockReceipt>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    // TASK-640: categoryId/minItems/maxItems — additive category/line-count range filters for
    // the frontend table filter UI. Appended at the very end (still before ct) so no
    // pre-existing parameter's positional index shifts for existing callers.
    Task<(List<StockReceipt> Items, int Total)> GetPagedAsync(
        Guid? storeId, string? status, string? search, string? sortBy, bool? sortDescending,
        int page, int pageSize,
        Guid? categoryId = null, int? minItems = null, int? maxItems = null,
        CancellationToken ct = default);
    Task<StockReceipt?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<StockReceiptItem?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default);

    Task AddAsync(StockReceipt receipt, CancellationToken ct = default);
    Task AddStockAsync(ProductStock stock, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    void Update(StockReceipt receipt);
    void UpdateItem(StockReceiptItem item);
    Task SaveChangesAsync(CancellationToken ct = default);
}
