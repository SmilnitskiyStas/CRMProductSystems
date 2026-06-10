using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Provider-level repository — bypasses RLS to access all tenants.
/// Only used by ProviderService (role = provider).
/// </summary>
public interface ITenantRepository
{
    Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct);
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct);

    // Cross-tenant counts (efficient group queries — no N+1)
    Task<Dictionary<Guid, int>> GetUserCountsAsync(CancellationToken ct);
    Task<Dictionary<Guid, int>> GetStoreCountsAsync(CancellationToken ct);
    Task<Dictionary<Guid, int>> GetExpiredBatchCountsAsync(CancellationToken ct);

    // Aggregate health metrics
    Task<int> GetTotalUsersAsync(CancellationToken ct);
    Task<int> GetTotalExpiredBatchesAsync(CancellationToken ct);

    // Tenant mutations
    Task SaveChangesAsync(CancellationToken ct);
}
