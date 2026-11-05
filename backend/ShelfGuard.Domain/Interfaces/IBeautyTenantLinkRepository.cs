using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IBeautyTenantLinkRepository
{
    /// <summary>Tracked link by ShelfGuard tenant id, or null.</summary>
    Task<BeautyTenantLink?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct);
    Task AddAsync(BeautyTenantLink link, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
