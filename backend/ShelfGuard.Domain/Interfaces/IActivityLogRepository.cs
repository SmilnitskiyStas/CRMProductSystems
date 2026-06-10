using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IActivityLogRepository
{
    /// <summary>Returns activity logs for a specific user, newest first.</summary>
    Task<IReadOnlyList<ActivityLog>> GetByUserAsync(
        Guid tenantId, Guid userId,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>Returns activity logs for a specific tenant, newest first (provider view).</summary>
    Task<IReadOnlyList<ActivityLog>> GetByTenantAsync(
        Guid tenantId,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Returns cross-tenant activity logs (newest first).
    /// Requires provider role — bypasses RLS via provider_bypass policy.
    /// </summary>
    Task<IReadOnlyList<ActivityLog>> GetAllTenantsAsync(
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>Appends a new activity log entry (fire-and-forget safe).</summary>
    Task LogAsync(ActivityLog entry, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
