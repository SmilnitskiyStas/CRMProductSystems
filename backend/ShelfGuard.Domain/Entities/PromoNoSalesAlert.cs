namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Dedup ledger for the "promo item without sales" alert: one row per (store, product, promo).
/// The worker (promo-no-sales.job.ts) notifies once per row and again only after the re-notify
/// interval while the situation persists. <see cref="PromoKey"/> = "{source}:{sourceId}".
/// </summary>
public sealed class PromoNoSalesAlert
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid LocationId { get; init; }
    public Guid ProductId { get; init; }
    public string PromoKey { get; init; } = string.Empty;
    public DateTime FirstDetectedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastNotifiedAt { get; set; } = DateTime.UtcNow;
    public int NotifyCount { get; set; } = 1;
}
