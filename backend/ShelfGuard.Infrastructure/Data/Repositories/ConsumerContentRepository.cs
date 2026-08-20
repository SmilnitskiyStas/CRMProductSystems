using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.ConsumerContent;
using ShelfGuard.Application.Features.ConsumerContent.Dtos;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>See <see cref="IConsumerContentRepository"/> for the contract and the
/// ITenantSessionOverride requirement every method here depends on.</summary>
public sealed class ConsumerContentRepository : IConsumerContentRepository
{
    private readonly AppDbContext _db;

    public ConsumerContentRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ConsumerBannerDto>> GetActiveBannersAsync(
        Guid tenantId, Guid storeId, DateTime utcNow, CancellationToken ct = default)
    {
        var banners = await _db.Banners
            .Where(b => b.TenantId == tenantId
                && b.PublishedAt != null
                && b.IsActive
                && b.ValidFrom <= utcNow
                && (b.ValidUntil == null || b.ValidUntil >= utcNow)
                && _db.BannerLocations.Any(bl => bl.BannerId == b.Id && bl.LocationId == storeId))
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);

        if (banners.Count == 0) return [];

        var bannerIds = banners.Select(b => b.Id).ToList();
        var productRows = await _db.BannerProducts
            .Where(bp => bp.TenantId == tenantId && bannerIds.Contains(bp.BannerId))
            .OrderBy(bp => bp.SortOrder)
            .Join(_db.Items, bp => bp.ItemId, i => i.Id,
                (bp, i) => new { bp.BannerId, i.Id, i.Name, i.ImageUrl, i.Unit, i.PriceRetail })
            .ToListAsync(ct);

        var productsByBanner = productRows
            .GroupBy(r => r.BannerId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ConsumerBannerProductDto>)g
                    .Select(r => new ConsumerBannerProductDto(r.Id, r.Name, r.ImageUrl, r.Unit, r.PriceRetail))
                    .ToList());

        return banners.Select(b => new ConsumerBannerDto(
            b.Id, b.Title, b.Eyebrow, b.Description,
            SplitLines(b.Body), SplitLines(b.Terms),
            b.ImageUrl, b.Icon, b.BackgroundColor, b.AccentColor,
            b.DetailMode, b.ExternalUrl, b.ValidUntil, b.SortOrder,
            productsByBanner.GetValueOrDefault(b.Id, Array.Empty<ConsumerBannerProductDto>()))
        ).ToList();
    }

    public Task<bool> BannerExistsAsync(Guid tenantId, Guid bannerId, CancellationToken ct = default) =>
        _db.Banners.AnyAsync(b => b.Id == bannerId && b.TenantId == tenantId, ct);

    public async Task RecordEventAsync(
        Guid tenantId, Guid bannerId, string eventType, Guid? consumerAccountId, CancellationToken ct = default)
    {
        var evt = BannerEvent.Create(tenantId, bannerId, eventType, consumerAccountId);
        await _db.BannerEvents.AddAsync(evt, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ConsumerPromotionDto>> GetActivePromotionsAsync(
        Guid tenantId, Guid storeId, DateTime utcNow, CancellationToken ct = default)
    {
        return await _db.Discounts
            .Where(d => d.TenantId == tenantId
                && d.StoreId == storeId
                && d.Status == DiscountStatus.Active
                && d.ValidFrom <= utcNow
                && (d.ValidUntil == null || d.ValidUntil >= utcNow))
            .Join(_db.Items, d => d.ProductId, i => i.Id, (d, i) => new { d, i })
            .OrderByDescending(x => x.d.CreatedAt)
            .Select(x => new ConsumerPromotionDto(
                x.d.Id, x.i.Id, x.i.Name, x.i.ImageUrl, x.i.Unit,
                x.d.DiscountPercent, x.d.PriceOriginal, x.d.PriceDiscounted, x.d.ValidUntil))
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<ConsumerCatalogItemDto> Items, int Total)> GetCatalogPagedAsync(
        Guid tenantId, Guid storeId, string? search, Guid? categoryId, int page, int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Items.Where(i => i.TenantId == tenantId && i.IsActive);

        if (categoryId.HasValue)
            query = query.Where(i => i.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i => EF.Functions.ILike(i.Name, $"%{search}%"));

        var total = await query.CountAsync(ct);

        var pageItems = await query
            .Include(i => i.Category)
            .OrderBy(i => i.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Availability lookup restricted to just this page's item ids — cheaper than summing
        // stock for the whole tenant catalog up front (this is a browse endpoint, not a
        // live-inventory guarantee — see TASK-521 brief).
        var itemIds = pageItems.Select(i => i.Id).ToList();
        var stockByProduct = itemIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.ProductStocks
                .Where(s => s.TenantId == tenantId && s.StoreId == storeId && itemIds.Contains(s.ProductId))
                .GroupBy(s => s.ProductId)
                .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);

        var items = pageItems.Select(i => new ConsumerCatalogItemDto(
            i.Id, i.Name, i.ImageUrl, i.Unit, i.PriceRetail,
            i.CategoryId, i.Category?.Name,
            stockByProduct.TryGetValue(i.Id, out var qty) && qty > 0))
            .ToList();

        return (items, total);
    }

    public async Task<IReadOnlyList<ConsumerCatalogItemDto>> GetCatalogByIdsAsync(
        Guid tenantId, Guid storeId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var matchedItems = await _db.Items
            .Where(i => i.TenantId == tenantId && i.IsActive && ids.Contains(i.Id))
            .Include(i => i.Category)
            .ToListAsync(ct);

        // Availability lookup restricted to just the matched item ids — same shape as
        // GetCatalogPagedAsync's own stock join, just keyed off the id list instead of a page.
        var itemIds = matchedItems.Select(i => i.Id).ToList();
        var stockByProduct = itemIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.ProductStocks
                .Where(s => s.TenantId == tenantId && s.StoreId == storeId && itemIds.Contains(s.ProductId))
                .GroupBy(s => s.ProductId)
                .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);

        return matchedItems.Select(i => new ConsumerCatalogItemDto(
            i.Id, i.Name, i.ImageUrl, i.Unit, i.PriceRetail,
            i.CategoryId, i.Category?.Name,
            stockByProduct.TryGetValue(i.Id, out var qty) && qty > 0))
            .ToList();
    }

    private static string[] SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
