using ShelfGuard.Application.Features.Provider.Dtos;

namespace ShelfGuard.Application.Features.Provider;

public interface IProviderScheduleService
{
    Task<List<ProviderScheduleSlotDto>> GetAllAsync(Guid? userId, CancellationToken ct = default);
    Task<(ProviderScheduleSlotDto? Slot, string? Error)> CreateAsync(CreateProviderScheduleSlotRequest req, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
