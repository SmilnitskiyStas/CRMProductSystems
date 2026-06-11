using ShelfGuard.Application.Features.SupplySchedules.Dtos;

namespace ShelfGuard.Application.Features.SupplySchedules;

public interface ISupplyScheduleService
{
    Task<List<SupplyScheduleDto>> GetAsync(Guid? storeId, Guid? supplierId, CancellationToken ct = default);

    Task<(SupplyScheduleDto? Schedule, string? Error)> CreateAsync(
        Guid tenantId, CreateSupplyScheduleRequest request, CancellationToken ct = default);

    Task<(SupplyScheduleDto? Schedule, string? Error)> UpdateAsync(
        Guid id, UpdateSupplyScheduleRequest request, CancellationToken ct = default);

    /// <summary>Soft delete — sets IsActive = false.</summary>
    Task<string?> DeleteAsync(Guid id, CancellationToken ct = default);
}
