using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IWriteOffRepository
{
    Task<List<WriteOff>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default);
    Task<WriteOff?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProductStock?> GetStockByIdAsync(Guid stockId, CancellationToken ct = default);

    Task AddAsync(WriteOff writeOff, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    void Update(WriteOff writeOff);
    void UpdateStock(ProductStock stock);
    Task SaveChangesAsync(CancellationToken ct = default);
}
