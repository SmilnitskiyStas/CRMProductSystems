using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IEventRepository
{
    Task<List<DemandEvent>> GetAsync(
        DateOnly? from, DateOnly? to, Guid[]? storeIds, CancellationToken ct = default);

    Task<DemandEvent?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>All events (with coefficients) possibly active on the date for the store.</summary>
    Task<List<DemandEvent>> GetCandidatesForDateAsync(
        Guid storeId, DateOnly date, CancellationToken ct = default);

    Task<DemandEventCoefficient?> GetCoefficientAsync(Guid coefId, CancellationToken ct = default);

    Task<int> CountByNameAndTypeAsync(Guid tenantId, string eventType, CancellationToken ct = default);

    Task AddAsync(DemandEvent demandEvent, CancellationToken ct = default);
    Task AddCoefficientAsync(DemandEventCoefficient coefficient, CancellationToken ct = default);
    void Remove(DemandEvent demandEvent);
    void RemoveCoefficient(DemandEventCoefficient coefficient);

    /// <summary>Replaces the full set of targeted-store links for an event (delete existing, insert new).
    /// Does not call SaveChangesAsync — same transaction-boundary convention as
    /// <c>UserLocationRepository.ReplaceForUserAsync</c>.</summary>
    Task ReplaceStoresForEventAsync(Guid eventId, IReadOnlyCollection<Guid> storeIds, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
