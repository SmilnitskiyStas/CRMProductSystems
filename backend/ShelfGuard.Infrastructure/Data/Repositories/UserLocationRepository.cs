using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for store-scoped user↔location assignment grants (TASK-392 schema,
/// TASK-392b service layer). See <see cref="IUserLocationRepository"/> for the
/// transaction-boundary convention (mutating methods do not call SaveChanges).
/// </summary>
public sealed class UserLocationRepository : IUserLocationRepository
{
    private readonly AppDbContext _db;

    public UserLocationRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> GetLocationIdsForUserAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default) =>
        await _db.UserLocations
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UserId == userId)
            .Select(x => x.LocationId)
            .ToListAsync(ct);

    public async Task ReplaceForUserAsync(
        Guid tenantId, Guid userId, IReadOnlyCollection<Guid> locationIds,
        Guid? assignedByUserId, CancellationToken ct = default)
    {
        var existing = await _db.UserLocations
            .Where(x => x.TenantId == tenantId && x.UserId == userId)
            .ToListAsync(ct);

        if (existing.Count > 0)
            _db.UserLocations.RemoveRange(existing);

        foreach (var locationId in locationIds.Distinct())
            await _db.UserLocations.AddAsync(
                UserLocation.Create(tenantId, userId, locationId, assignedByUserId), ct);
    }

    public async Task<bool> HasAnyLocationAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default) =>
        await _db.UserLocations
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);

    public async Task<IReadOnlyCollection<Guid>> GetUserIdsWithAnyLocationAsync(
        Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return Array.Empty<Guid>();

        var ids = userIds.ToList();
        return await _db.UserLocations
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && ids.Contains(x.UserId))
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
