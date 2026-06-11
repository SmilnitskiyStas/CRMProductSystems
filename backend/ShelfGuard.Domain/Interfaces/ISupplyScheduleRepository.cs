using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ISupplyScheduleRepository
{
    Task<List<SupplySchedule>> GetAsync(Guid? storeId, Guid? supplierId, CancellationToken ct = default);
    Task<SupplySchedule?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>True when an active schedule already exists for (store, supplier), excluding the given id.</summary>
    Task<bool> ActiveExistsAsync(Guid storeId, Guid supplierId, Guid? excludeId, CancellationToken ct = default);

    Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default);
    Task<bool> SupplierExistsAsync(Guid supplierId, CancellationToken ct = default);

    Task AddAsync(SupplySchedule schedule, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
