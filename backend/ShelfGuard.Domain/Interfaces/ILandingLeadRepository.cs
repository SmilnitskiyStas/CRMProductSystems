using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ILandingLeadRepository
{
    Task AddAsync(LandingLead lead, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>Tracked entity (for updates) or null.</summary>
    Task<LandingLead?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>status: all | new | processed. Newest first.</summary>
    Task<(List<LandingLead> Items, int Total)> ListAsync(
        string status, string? search, int page, int pageSize, CancellationToken ct);

    Task<int> CountUnprocessedAsync(CancellationToken ct);
}
