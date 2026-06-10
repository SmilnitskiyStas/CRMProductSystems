using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IDiscountRepository
{
    Task<IReadOnlyList<Discount>> GetAllAsync(
        Guid tenantId,
        Guid? storeId   = null,
        string? status  = null,
        CancellationToken ct = default);

    Task<Discount?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Discount discount, CancellationToken ct = default);

    void Update(Discount discount);

    Task SaveChangesAsync(CancellationToken ct = default);
}
