using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ITransferRepository
{
    Task<List<StockTransfer>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    Task<StockTransfer?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProductStock?> GetStockByIdAsync(Guid stockId, CancellationToken ct = default);

    Task AddAsync(StockTransfer transfer, CancellationToken ct = default);
    Task AddStockAsync(ProductStock stock, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    void Update(StockTransfer transfer);
    void UpdateStock(ProductStock stock);
    Task SaveChangesAsync(CancellationToken ct = default);
}
