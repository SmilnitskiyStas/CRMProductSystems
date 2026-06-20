using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IProviderScheduleRepository
{
    Task<List<ProviderScheduleSlot>> GetAllAsync(Guid? userId, CancellationToken ct);
    Task<ProviderScheduleSlot?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(ProviderScheduleSlot slot, CancellationToken ct);
    void Remove(ProviderScheduleSlot slot);
    Task SaveChangesAsync(CancellationToken ct);
}
