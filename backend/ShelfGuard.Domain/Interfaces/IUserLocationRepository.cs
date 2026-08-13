namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for store-scoped user↔location assignment grants (TASK-392 schema,
/// TASK-392b service layer). Every restricted rank (network_manager and below) gets
/// exactly one row per assigned location here — enterprise_admin never appears in this
/// table (unconditional bypass via app.role, see <c>UserLocation</c>'s own doc comment).
/// No enforcement reads this table yet (Stage 3, later).
/// </summary>
public interface IUserLocationRepository
{
    /// <summary>Current set of location ids a user is assigned to, within one tenant.</summary>
    Task<IReadOnlyList<Guid>> GetLocationIdsForUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Full-replace: deletes every existing user_locations row for (tenantId, userId) and
    /// inserts one fresh row per distinct id in <paramref name="locationIds"/>. An empty
    /// collection leaves the user with zero rows (e.g. store cleared, or role no longer
    /// participates in store-scoping). Does not call SaveChanges — the caller controls the
    /// transaction boundary (e.g. UserService commits this together with a User.StoreId
    /// change via one shared AppDbContext / SaveChangesAsync call).
    /// </summary>
    Task ReplaceForUserAsync(
        Guid tenantId, Guid userId, IReadOnlyCollection<Guid> locationIds,
        Guid? assignedByUserId, CancellationToken ct = default);

    /// <summary>
    /// True when the user has at least one user_locations row in this tenant (TASK-395,
    /// UserDto.NeedsLocationAssignment). For single-user paths (GetById, Invite, Update) where
    /// one extra existence query is cheap and correctness matters more than batching. For a
    /// whole user list, use <see cref="GetUserIdsWithAnyLocationAsync"/> instead — a per-user
    /// loop over this method would be exactly the N+1 that method exists to avoid.
    /// </summary>
    Task<bool> HasAnyLocationAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Batched existence check backing UserDto.NeedsLocationAssignment on the users LIST
    /// endpoint (TASK-395): given a candidate set of user ids, returns the subset that has at
    /// least one user_locations row, via a single query — avoids one query per user.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetUserIdsWithAnyLocationAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>
    /// Batched existence check backing the users LIST endpoint's <c>storeIds</c> filter
    /// (TASK-517): given a candidate set of user ids, returns the subset that has at least one
    /// user_locations row whose LocationId is in <paramref name="locationIds"/> — same batching
    /// rationale as <see cref="GetUserIdsWithAnyLocationAsync"/>, one extra location membership
    /// constraint.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetUserIdsWithLocationInAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> locationIds,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
