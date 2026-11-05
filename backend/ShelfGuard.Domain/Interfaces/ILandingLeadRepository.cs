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
        string status, string? search, int page, int pageSize, CancellationToken ct, string? requestType = null);

    /// <summary>Phones of leads created at or after <paramref name="since"/> (repeat-lead detection).</summary>
    Task<List<string>> GetPhonesSinceAsync(DateTime since, CancellationToken ct);

    /// <summary>Lead previously stored for the given source-system idempotency key, or null.</summary>
    Task<LandingLead?> GetByExternalIdAsync(string externalId, CancellationToken ct);

    Task<int> CountUnprocessedAsync(CancellationToken ct);
}
