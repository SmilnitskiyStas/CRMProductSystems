using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ISupplierRepository
{
    Task<List<Supplier>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<(List<Supplier> Items, int Total)> GetPagedAsync(bool includeInactive, int page, int pageSize, CancellationToken ct = default);
    Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId, CancellationToken ct = default);
    Task AddAsync(Supplier supplier, CancellationToken ct = default);
    void Update(Supplier supplier);
    Task SaveChangesAsync(CancellationToken ct = default);
}
