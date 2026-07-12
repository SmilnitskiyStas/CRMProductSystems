namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Daily snapshot of product_stock status counts for a single store (TASK-335).
/// Written once per (tenant, store, day) by a worker cron job so the dashboard
/// can compare current Safe/Warning/Critical/Expired counts against a prior date
/// (e.g. "a week ago"). Network-wide view is computed by summing rows for a
/// given (tenant, date) across all stores at query time — no separate rollup row.
/// </summary>
public sealed class StockStatusSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public DateOnly SnapshotDate { get; init; }

    public int SafeCount { get; set; }
    public int WarningCount { get; set; }
    public int CriticalCount { get; set; }
    public int ExpiredCount { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Location? Store { get; init; }
}
