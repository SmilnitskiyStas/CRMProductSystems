using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IStockStatusSnapshotRepository
{
    /// <summary>
    /// Idempotent upsert of a single store's snapshot for a given date, keyed on
    /// the (TenantId, StoreId, SnapshotDate) unique index. Safe to call repeatedly
    /// for the same day (e.g. worker retries) — later calls overwrite the counts.
    /// </summary>
    Task<StockStatusSnapshot> UpsertAsync(
        Guid tenantId,
        Guid storeId,
        DateOnly snapshotDate,
        int safeCount,
        int warningCount,
        int criticalCount,
        int expiredCount,
        CancellationToken ct = default);

    /// <summary>Single-store snapshot for a given date, or null if none was recorded.</summary>
    Task<StockStatusSnapshot?> GetAsync(
        Guid tenantId,
        Guid storeId,
        DateOnly snapshotDate,
        CancellationToken ct = default);

    /// <summary>
    /// All store snapshots for a tenant on a given date — caller sums the counts
    /// for the network-wide (all stores) dashboard view.
    /// </summary>
    Task<List<StockStatusSnapshot>> GetByTenantAndDateAsync(
        Guid tenantId,
        DateOnly snapshotDate,
        CancellationToken ct = default);
}
