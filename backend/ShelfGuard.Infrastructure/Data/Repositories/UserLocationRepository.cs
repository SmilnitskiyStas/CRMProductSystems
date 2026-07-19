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

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
