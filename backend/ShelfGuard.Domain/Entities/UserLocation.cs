namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Grants a user access to one specific location (TASK-392, Stage 1 schema).
/// Every restricted rank (network_manager, store_manager, merchandiser, storekeeper,
/// cashier, staff) gets exactly one row per assigned location — including
/// single-location roles, so this table is the single enforcement mechanism instead of
/// two parallel ones. enterprise_admin never appears here: it bypasses store scoping
/// entirely via an unconditional <c>app.role</c> check, with access to every location in
/// the tenant by definition.
/// Enforcement itself (RLS RESTRICTIVE store_scope policies on product_stock,
/// daily_sales, pos_shifts, etc. that actually filter query results using this table) is
/// Stage 3 — deliberately not wired here. This Stage 1 migration only creates the table;
/// nothing reads from it yet. <see cref="User.StoreId"/> stays a separate, UI/invite-time
/// "default home location" hint and is never read by access-control enforcement.
/// A pure leaf assignment table — nothing references it by <see cref="Id"/> — so removing
/// an assignment is a plain hard DELETE; no soft-delete/IsActive flag.
/// </summary>
public sealed class UserLocation
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>User granted access to the location.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Location the user is granted access to.</summary>
    public Guid LocationId { get; private set; }

    /// <summary>Who granted this assignment. Nullable/SetNull — deleting the assigner must not orphan the assignment.</summary>
    public Guid? AssignedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private UserLocation() { }

    public static UserLocation Create(
        Guid tenantId, Guid userId, Guid locationId, Guid? assignedByUserId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        UserId = userId,
        LocationId = locationId,
        AssignedByUserId = assignedByUserId,
        CreatedAt = DateTime.UtcNow,
    };
}
