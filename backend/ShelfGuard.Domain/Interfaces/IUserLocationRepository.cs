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

    Task SaveChangesAsync(CancellationToken ct = default);
}
