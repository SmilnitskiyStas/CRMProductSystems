namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A temporary, additive per-user permission grant on top of role/User.Permissions
/// (ADR-019, TASK-341). Always time-bound — <see cref="ExpiresAt"/> is required by
/// definition; permanent overrides continue to live exclusively in
/// <see cref="User.Permissions"/>. Merged into JWT claims once at mint time
/// (AuthService.BuildEffectivePermissionsAsync, TASK-342) — a live, non-revoked,
/// non-expired grant always forces the permission to true, even over an explicit
/// permanent `false`.
/// </summary>
public sealed class UserPermissionGrant
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Recipient of the grant.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Page-slug permission key — same set as <c>ValidPages</c>/<c>User.Permissions</c> keys.</summary>
    public string PermissionKey { get; private set; } = string.Empty;

    /// <summary>Always set — a row with no expiry is not a valid state for this table.</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>Who granted the access.</summary>
    public Guid GrantedByUserId { get; private set; }
    public DateTime GrantedAt { get; private set; }

    /// <summary>Early revoke, independent of natural expiry.</summary>
    public DateTime? RevokedAt { get; private set; }
    public Guid? RevokedByUserId { get; private set; }

    /// <summary>Worker dedupe marker: "expiring soon" notification already sent.</summary>
    public DateTime? NotifiedExpiringAt { get; private set; }

    /// <summary>Worker dedupe marker: "expired" notification already sent.</summary>
    public DateTime? NotifiedExpiredAt { get; private set; }

    public User? User { get; private set; }
    public User? GrantedByUser { get; private set; }
    public User? RevokedByUser { get; private set; }

    private UserPermissionGrant() { }

    public static UserPermissionGrant Create(
        Guid tenantId,
        Guid userId,
        string permissionKey,
        DateTime expiresAt,
        Guid grantedByUserId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        PermissionKey = permissionKey,
        ExpiresAt = expiresAt,
        GrantedByUserId = grantedByUserId,
        GrantedAt = DateTime.UtcNow,
    };

    /// <summary>Live, in-effect grant: not revoked and not yet past expiry.</summary>
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;

    public void Revoke(Guid revokedByUserId)
    {
        RevokedAt = DateTime.UtcNow;
        RevokedByUserId = revokedByUserId;
    }

    public void MarkNotifiedExpiring() => NotifiedExpiringAt = DateTime.UtcNow;

    public void MarkNotifiedExpired() => NotifiedExpiredAt = DateTime.UtcNow;
}
