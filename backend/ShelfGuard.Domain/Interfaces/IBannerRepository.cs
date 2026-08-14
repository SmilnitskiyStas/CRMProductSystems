using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Admin-side data access for <see cref="Banner"/> and its two join tables
/// (<see cref="BannerLocation"/>/<see cref="BannerProduct"/>), plus read-only view/click
/// counts off <see cref="BannerEvent"/> (TASK-521). Runs entirely under the calling staff
/// request's own tenant RLS context (real app.tenant_id claim already set by
/// TenantConnectionInterceptor) — no <see cref="Services.ITenantSessionOverride"/> needed here,
/// unlike the consumer-facing reads in IConsumerContentRepository.
/// </summary>
public interface IBannerRepository
{
    Task<IReadOnlyList<Banner>> GetAllAsync(
        Guid tenantId, Guid? locationId, bool? activeOnly, CancellationToken ct = default);

    Task<Banner?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Batched: banner id → its assigned location ids (unordered).</summary>
    Task<Dictionary<Guid, List<Guid>>> GetLocationIdsForBannersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> bannerIds, CancellationToken ct = default);

    /// <summary>Batched: banner id → its attached product (Item) ids, ordered by SortOrder.</summary>
    Task<Dictionary<Guid, List<Guid>>> GetProductIdsForBannersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> bannerIds, CancellationToken ct = default);

    /// <summary>Batched: banner id → (view count, click count) from banner_events.</summary>
    Task<Dictionary<Guid, (int Views, int Clicks)>> GetEventCountsForBannersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> bannerIds, CancellationToken ct = default);

    Task AddAsync(Banner banner, CancellationToken ct = default);

    void Update(Banner banner);

    /// <summary>
    /// Full-replace: deletes every existing banner_locations row for (tenantId, bannerId) and
    /// inserts one fresh row per distinct id in <paramref name="locationIds"/>. Does not call
    /// SaveChanges — same transaction-boundary convention as
    /// <see cref="IUserLocationRepository.ReplaceForUserAsync"/>.
    /// </summary>
    Task ReplaceLocationsAsync(
        Guid tenantId, Guid bannerId, IReadOnlyCollection<Guid> locationIds, CancellationToken ct = default);

    /// <summary>
    /// Full-replace: deletes every existing banner_products row for (tenantId, bannerId) and
    /// inserts one fresh row per distinct id in <paramref name="productIds"/>, in list order
    /// (SortOrder = position). Does not call SaveChanges.
    /// </summary>
    Task ReplaceProductsAsync(
        Guid tenantId, Guid bannerId, IReadOnlyList<Guid> productIds, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
