using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IDailySalesRepository
{
    Task<List<DailySale>> GetAsync(
        Guid? storeId,
        Guid? productId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default);

    Task<DailySale?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<DailySale?> FindAsync(Guid storeId, Guid productId, DateOnly date, CancellationToken ct = default);

    /// <summary>Maps barcode → product id for CSV import row resolution.</summary>
    Task<Dictionary<string, Guid>> GetProductIdsByBarcodesAsync(
        IReadOnlyCollection<string> barcodes,
        CancellationToken ct = default);

    Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default);
    Task<bool> ProductExistsAsync(Guid productId, CancellationToken ct = default);

    Task AddAsync(DailySale sale, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
