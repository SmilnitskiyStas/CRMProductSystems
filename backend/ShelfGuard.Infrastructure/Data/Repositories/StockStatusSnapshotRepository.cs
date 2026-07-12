using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class StockStatusSnapshotRepository : IStockStatusSnapshotRepository
{
    private readonly AppDbContext _db;

    public StockStatusSnapshotRepository(AppDbContext db) => _db = db;

    public async Task<StockStatusSnapshot> UpsertAsync(
        Guid tenantId,
        Guid storeId,
        DateOnly snapshotDate,
        int safeCount,
        int warningCount,
        int criticalCount,
        int expiredCount,
        CancellationToken ct = default)
    {
        var existing = await _db.StockStatusSnapshots.FirstOrDefaultAsync(
            s => s.TenantId == tenantId && s.StoreId == storeId && s.SnapshotDate == snapshotDate, ct);

        if (existing is null)
        {
            existing = new StockStatusSnapshot
            {
                TenantId = tenantId,
                StoreId = storeId,
                SnapshotDate = snapshotDate,
                SafeCount = safeCount,
                WarningCount = warningCount,
                CriticalCount = criticalCount,
                ExpiredCount = expiredCount,
            };
            await _db.StockStatusSnapshots.AddAsync(existing, ct);
        }
        else
        {
            existing.SafeCount = safeCount;
            existing.WarningCount = warningCount;
            existing.CriticalCount = criticalCount;
            existing.ExpiredCount = expiredCount;
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public Task<StockStatusSnapshot?> GetAsync(
        Guid tenantId, Guid storeId, DateOnly snapshotDate, CancellationToken ct = default) =>
        _db.StockStatusSnapshots
            .Include(s => s.Store)
            .FirstOrDefaultAsync(
                s => s.TenantId == tenantId && s.StoreId == storeId && s.SnapshotDate == snapshotDate, ct);

    public Task<List<StockStatusSnapshot>> GetByTenantAndDateAsync(
        Guid tenantId, DateOnly snapshotDate, CancellationToken ct = default) =>
        _db.StockStatusSnapshots
            .Include(s => s.Store)
            .Where(s => s.TenantId == tenantId && s.SnapshotDate == snapshotDate)
            .ToListAsync(ct);
}
