using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IBeautyTenantLinkRepository
{
    /// <summary>Tracked link by ShelfGuard tenant id, or null.</summary>
    Task<BeautyTenantLink?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct);
    Task AddAsync(BeautyTenantLink link, CancellationToken ct);
    /// <summary>Adds and saves; returns false (entity detached) if the unique index rejected it (concurrent create).</summary>
    Task<bool> TryAddAsync(BeautyTenantLink link, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
