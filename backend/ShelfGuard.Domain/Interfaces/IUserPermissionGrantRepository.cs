using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for temporary per-user permission grants (ADR-019, TASK-341).
/// <see cref="GetActiveGrantsForUserAsync"/> is tenant-scoped (RLS-enforced, used at
/// JWT-mint time). <see cref="GetExpiringSoonAsync"/>/<see cref="GetJustExpiredAsync"/>
/// accept an optional tenant filter for the worker's cross-tenant expiry scan — same
/// bypass pattern as <c>ProviderTicketRepository</c>: the caller must run under a DB
/// session with <c>app.role = 'provider'</c> for a null tenantId to see rows across
/// every tenant (otherwise the standard tenant_isolation RLS policy still applies).
/// </summary>
public interface IUserPermissionGrantRepository
{
    /// <summary>
    /// Live grants for a user: ExpiresAt in the future and RevokedAt is null.
    /// Used by the JWT-mint merge step (effective permissions = role/User.Permissions
    /// forced true for every key returned here).
    /// </summary>
    Task<IReadOnlyList<UserPermissionGrant>> GetActiveGrantsForUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

    Task<UserPermissionGrant?> GetByIdAsync(
        Guid tenantId, Guid grantId, CancellationToken ct = default);

    Task AddAsync(UserPermissionGrant grant, CancellationToken ct = default);

    /// <summary>
    /// Early-revokes a grant (sets RevokedAt/RevokedByUserId). Returns false if the
    /// grant does not exist, belongs to a different tenant, or is already revoked.
    /// Does not call SaveChanges.
    /// </summary>
    Task<bool> RevokeAsync(
        Guid tenantId, Guid grantId, Guid revokedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Grants expiring within <paramref name="window"/> from now, not yet revoked,
    /// not yet notified (NotifiedExpiringAt is null). tenantId null = every tenant
    /// (worker scan; requires provider-bypass DB session).
    /// </summary>
    Task<IReadOnlyList<UserPermissionGrant>> GetExpiringSoonAsync(
        Guid? tenantId, TimeSpan window, CancellationToken ct = default);

    /// <summary>
    /// Grants whose ExpiresAt has already passed, not revoked, not yet notified
    /// (NotifiedExpiredAt is null). tenantId null = every tenant.
    /// </summary>
    Task<IReadOnlyList<UserPermissionGrant>> GetJustExpiredAsync(
        Guid? tenantId, CancellationToken ct = default);

    /// <summary>Stamps NotifiedExpiringAt = now. Does not call SaveChanges.</summary>
    Task MarkNotifiedExpiringAsync(Guid grantId, CancellationToken ct = default);

    /// <summary>Stamps NotifiedExpiredAt = now. Does not call SaveChanges.</summary>
    Task MarkNotifiedExpiredAsync(Guid grantId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
