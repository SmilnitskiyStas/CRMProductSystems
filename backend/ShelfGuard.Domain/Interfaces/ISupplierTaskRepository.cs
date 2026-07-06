using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for the supplier task board (TASK-306). Every method is scoped by
/// the calling supplier's <c>tenantId</c> (RLS-enforced) — a supplier tenant can
/// only ever see/mutate its own tasks.
/// </summary>
public interface ISupplierTaskRepository
{
    /// <summary>
    /// Lists tasks owned by the tenant, optionally filtered by assignee (current user),
    /// client tenant, and status. Joined with the assignee's display name and the
    /// client tenant's display name for read convenience.
    /// </summary>
    Task<IReadOnlyList<(SupplierTask Task, string? AssignedToUserName, string? ClientTenantName)>> GetAllAsync(
        Guid tenantId, Guid? assignedToUserId, Guid? clientTenantId, string? status,
        CancellationToken ct = default);

    Task<SupplierTask?> GetByIdAsync(Guid tenantId, Guid taskId, CancellationToken ct = default);

    Task AddAsync(SupplierTask task, CancellationToken ct = default);

    Task<string?> GetUserDisplayNameAsync(Guid userId, CancellationToken ct = default);

    Task<string?> GetTenantDisplayNameAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Whether a user with this id belongs to the given tenant (assignment guard).</summary>
    Task<bool> UserBelongsToTenantAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
