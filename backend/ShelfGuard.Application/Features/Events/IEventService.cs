using ShelfGuard.Application.Features.Events.Dtos;

namespace ShelfGuard.Application.Features.Events;

public interface IEventService
{
    Task<List<DemandEventDto>> GetAsync(
        DateOnly? from, DateOnly? to, Guid[]? storeIds, CancellationToken ct = default);

    Task<(DemandEventDto? Event, string? Error)> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(DemandEventDto? Event, string? Error)> CreateAsync(
        Guid tenantId, Guid? createdBy, UpsertEventRequest request, CancellationToken ct = default);

    Task<(DemandEventDto? Event, string? Error)> UpdateAsync(
        Guid id, UpsertEventRequest request, CancellationToken ct = default);

    Task<string?> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<(EventCoefficientDto? Coefficient, string? Error)> AddCoefficientAsync(
        Guid eventId, CreateCoefficientRequest request, CancellationToken ct = default);

    Task<(EventCoefficientDto? Coefficient, string? Error)> UpdateCoefficientAsync(
        Guid eventId, Guid coefId, UpdateCoefficientRequest request, CancellationToken ct = default);

    Task<string?> RemoveCoefficientAsync(Guid eventId, Guid coefId, CancellationToken ct = default);

    /// <summary>Seeds standard Ukrainian holidays with default coefficients (v2-spec §4). Idempotent.</summary>
    Task<(SeedDefaultsResult? Result, string? Error)> SeedDefaultsAsync(
        Guid tenantId, CancellationToken ct = default);
}
