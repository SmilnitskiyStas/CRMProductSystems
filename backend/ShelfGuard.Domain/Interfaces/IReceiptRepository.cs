using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IReceiptRepository
{
    Task<List<StockReceipt>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    Task<(List<StockReceipt> Items, int Total)> GetPagedAsync(Guid? storeId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<StockReceipt?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<StockReceiptItem?> GetItemByIdAsync(Guid itemId, CancellationToken ct = default);

    Task AddAsync(StockReceipt receipt, CancellationToken ct = default);
    Task AddStockAsync(ProductStock stock, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    void Update(StockReceipt receipt);
    void UpdateItem(StockReceiptItem item);
    Task SaveChangesAsync(CancellationToken ct = default);
}
