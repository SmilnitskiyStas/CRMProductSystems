using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Services;
using ShelfGuard.Application.Features.ConsumerAnalytics;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]
[Route("api/mobile-catalog-settings")]
public sealed class MobileCatalogSettingsController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ITenantContext _tenant; private readonly IWebHostEnvironment _environment;
    public MobileCatalogSettingsController(AppDbContext db, ITenantContext tenant, IWebHostEnvironment environment) { _db = db; _tenant = tenant; _environment = environment; }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = _tenant.TenantId!.Value;
        var rows = await _db.MobileCatalogSettings.AsNoTracking().Include(x => x.Items).ThenInclude(x => x.Product).Include(x => x.Locations)
            .Where(x => x.TenantId == tenantId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var row = await Load(id, true, ct); return row is null ? NotFound() : Ok(ToDto(row));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveMobileCatalogRequest request, CancellationToken ct)
    {
        var entity = new MobileCatalogSettings { TenantId = _tenant.TenantId!.Value };
        var error = await Apply(entity, request, ct); if (error is not null) return BadRequest(new { error });
        await _db.MobileCatalogSettings.AddAsync(entity, ct); await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(await Load(entity.Id, true, ct) ?? entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveMobileCatalogRequest request, CancellationToken ct)
    {
        // Read the publication without tracking and persist its aggregate explicitly below.
        // Tracking the loaded Settings -> Items graph caused repeatable false optimistic-
        // concurrency failures when EF mixed item deletes/updates/inserts in one SaveChanges.
        var entity = await Load(id, true, ct); if (entity is null) return NotFound();
        if (entity.Status is MobileCatalogPublicationStatus.Archived) return BadRequest(new { error = "Archived catalog cannot be edited. Duplicate it instead." });
        var error = await Apply(entity, request, ct); if (error is not null) return BadRequest(new { error });

        var desiredItems = entity.Items.OrderBy(x => x.SortOrder).Select(x => new MobileCatalogItem
        {
            TenantId = entity.TenantId,
            SettingsId = entity.Id,
            ProductId = x.ProductId,
            SortOrder = x.SortOrder,
            IsFeatured = x.IsFeatured,
            MobileDiscountPercent = x.MobileDiscountPercent,
            ProductNameSnapshot = x.ProductNameSnapshot,
            UnitSnapshot = x.UnitSnapshot,
            ImageUrlSnapshot = x.ImageUrlSnapshot,
            RegularPriceSnapshot = x.RegularPriceSnapshot,
            MobilePriceSnapshot = x.MobilePriceSnapshot,
        }).ToList();
        var desiredLocations = entity.Locations.Select(x => new MobileCatalogLocation
        {
            TenantId = entity.TenantId, SettingsId = entity.Id, LocationId = x.LocationId,
        }).ToList();

        // Product lookups performed by Apply() do not need to remain tracked; only the
        // newly built snapshot rows are inserted after the set-based synchronization.
        _db.ChangeTracker.Clear();
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.MobileCatalogSettings
            .Where(x => x.Id == entity.Id && x.TenantId == entity.TenantId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Title, entity.Title)
                .SetProperty(x => x.Description, entity.Description)
                .SetProperty(x => x.LayoutMode, entity.LayoutMode)
                .SetProperty(x => x.PublishAt, entity.PublishAt)
                .SetProperty(x => x.UnpublishAt, entity.UnpublishAt)
                .SetProperty(x => x.UpdatedAt, entity.UpdatedAt), ct);
        if (updated == 0)
        {
            await transaction.RollbackAsync(ct);
            return Conflict(new { error = "Catalog was changed or removed. Refresh the page and try again." });
        }

        await _db.MobileCatalogItems
            .Where(x => x.SettingsId == entity.Id && x.TenantId == entity.TenantId)
            .ExecuteDeleteAsync(ct);
        await _db.MobileCatalogLocations
            .Where(x => x.SettingsId == entity.Id && x.TenantId == entity.TenantId)
            .ExecuteDeleteAsync(ct);
        if (desiredItems.Count > 0) await _db.MobileCatalogItems.AddRangeAsync(desiredItems, ct);
        if (desiredLocations.Count > 0) await _db.MobileCatalogLocations.AddRangeAsync(desiredLocations, ct);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Ok(ToDto(await Load(entity.Id, true, ct) ?? entity));
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var entity = await Load(id, false, ct); if (entity is null) return NotFound();
        var publishError = await ValidateForPublish(entity, ct); if (publishError is not null) return BadRequest(new { error = publishError });
        entity.Status = entity.PublishAt > DateTime.UtcNow ? MobileCatalogPublicationStatus.Scheduled : MobileCatalogPublicationStatus.Published;
        entity.IsEnabled = true; entity.PublishedAt = DateTime.UtcNow; entity.ArchivedAt = null; entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct); return Ok(ToDto(entity));
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var entity = await Load(id, false, ct); if (entity is null) return NotFound();
        entity.Status = MobileCatalogPublicationStatus.Archived; entity.IsEnabled = false; entity.ArchivedAt = DateTime.UtcNow; entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct); return Ok(ToDto(entity));
    }

    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken ct)
    {
        var source = await Load(id, true, ct); if (source is null) return NotFound();
        var copy = new MobileCatalogSettings { TenantId = source.TenantId, Title = $"{source.Title} — копія", Description = source.Description, BannerUrl = source.BannerUrl, LayoutMode = source.LayoutMode, IsEnabled = false, Status = MobileCatalogPublicationStatus.Draft, PublishAt = DateTime.UtcNow, Items = source.Items.OrderBy(x => x.SortOrder).Select(x => new MobileCatalogItem { TenantId = source.TenantId, ProductId = x.ProductId, SortOrder = x.SortOrder, IsFeatured = x.IsFeatured, MobileDiscountPercent = x.MobileDiscountPercent, ProductNameSnapshot = x.ProductNameSnapshot, UnitSnapshot = x.UnitSnapshot, ImageUrlSnapshot = x.ImageUrlSnapshot, RegularPriceSnapshot = x.RegularPriceSnapshot, MobilePriceSnapshot = x.MobilePriceSnapshot }).ToList(), Locations = source.Locations.Select(x => new MobileCatalogLocation { TenantId = source.TenantId, LocationId = x.LocationId }).ToList() };
        await _db.MobileCatalogSettings.AddAsync(copy, ct); await _db.SaveChangesAsync(ct); return Ok(ToDto(copy));
    }

    [HttpGet("{id:guid}/analytics")]
    public async Task<IActionResult> Analytics(
        Guid id,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] Guid[]? storeIds = null,
        CancellationToken ct = default)
    {
        var catalog = await Load(id, true, ct); if (catalog is null) return NotFound();
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date) return BadRequest(new { error = "The start date must not be later than the end date." });
        var fromUtc = from?.Date;
        var toExclusive = to?.Date.AddDays(1);
        var requestedStores = storeIds?.Distinct().ToArray() ?? [];
        var catalogStores = catalog.Locations.Select(x => x.LocationId).Distinct().ToArray();
        var selectedStores = requestedStores.Length == 0 ? catalogStores : requestedStores.Intersect(catalogStores).ToArray();
        var eventQuery = _db.MobileCatalogEvents.AsNoTracking().Where(x => x.TenantId == catalog.TenantId && x.CatalogId == id);
        if (fromUtc.HasValue) eventQuery = eventQuery.Where(x => x.OccurredAt >= fromUtc.Value);
        if (toExclusive.HasValue) eventQuery = eventQuery.Where(x => x.OccurredAt < toExclusive.Value);
        eventQuery = eventQuery.Where(x => selectedStores.Contains(x.StoreId));
        var events = await eventQuery.ToListAsync(ct);
        var productIds = catalog.Items.Select(x => x.ProductId).ToHashSet();
        var catalogEnd = new[] { catalog.UnpublishAt, catalog.ArchivedAt, DateTime.UtcNow }.Where(x => x.HasValue).Min()!.Value;
        var periodStart = fromUtc.HasValue && fromUtc.Value > catalog.PublishAt ? fromUtc.Value : catalog.PublishAt;
        var periodEnd = toExclusive.HasValue && toExclusive.Value < catalogEnd ? toExclusive.Value : catalogEnd;
        var transactionQuery = _db.PosTransactions.AsNoTracking().Include(x => x.Items).Include(x => x.LoyaltyMembership)
            .Where(x => x.TenantId == catalog.TenantId && x.LoyaltyMembershipId.HasValue
                && x.CreatedAt >= periodStart && x.CreatedAt < periodEnd && x.Status != "cancelled");
        transactionQuery = transactionQuery.Where(x => selectedStores.Contains(x.StoreId));
        var transactions = await transactionQuery.ToListAsync(ct);
        var attributed = new List<(Guid ProductId, Guid StoreId, Guid MembershipId, DateTime OccurredAt, decimal Quantity, decimal Revenue)>();
        foreach (var tx in transactions)
        foreach (var line in tx.Items)
        {
            if (productIds.Contains(line.ProductId)) attributed.Add((line.ProductId, tx.StoreId, tx.LoyaltyMembershipId!.Value, tx.CreatedAt, line.Quantity, line.PriceFinal * line.Quantity));
        }
        var productRows = catalog.Items.OrderBy(x => x.SortOrder).Select(item =>
        {
            var productEvents = events.Where(x => x.ProductId == item.ProductId).ToList(); var sales = attributed.Where(x => x.ProductId == item.ProductId).ToList();
            var views = productEvents.Count(x => x.EventType == MobileCatalogEventType.ProductView); var scanCount = productEvents.Count(x => x.EventType == MobileCatalogEventType.ProductScan); var purchases = sales.Sum(x => x.Quantity);
            return new CatalogProductAnalyticsDto(item.ProductId, item.ProductNameSnapshot, views, scanCount, purchases, sales.Sum(x => x.Revenue), views == 0 ? 0 : Math.Round(purchases / views * 100, 2));
        }).ToList();
        var catalogViews = events.Count(x => x.EventType == MobileCatalogEventType.CatalogView);
        var uniqueUsers = events.Select(x => x.ConsumerAccountId?.ToString() ?? x.SessionId).Distinct().Count();
        var totalPurchases = attributed.Sum(x => x.Quantity);
        var dates = events.Select(x => x.OccurredAt.Date).Concat(attributed.Select(x => x.OccurredAt.Date)).Distinct().OrderBy(x => x).ToList();
        var daily = dates.Select(date => new CatalogDailyAnalyticsDto(date,
            events.Count(x => x.OccurredAt.Date == date && x.EventType == MobileCatalogEventType.CatalogView),
            events.Count(x => x.OccurredAt.Date == date && x.EventType == MobileCatalogEventType.ProductView),
            events.Count(x => x.OccurredAt.Date == date && x.EventType == MobileCatalogEventType.ProductScan),
            attributed.Where(x => x.OccurredAt.Date == date).Sum(x => x.Quantity),
            attributed.Where(x => x.OccurredAt.Date == date).Sum(x => x.Revenue))).ToList();
        var eventStoreIds = events.Select(x => x.StoreId).Concat(attributed.Select(x => x.StoreId)).Distinct().ToArray();
        var storeNames = await _db.Locations.AsNoTracking().Where(x => eventStoreIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var stores = eventStoreIds.Select(storeId => new CatalogStoreAnalyticsDto(storeId, storeNames.GetValueOrDefault(storeId, "Магазин"),
            events.Count(x => x.StoreId == storeId && x.EventType == MobileCatalogEventType.CatalogView),
            events.Count(x => x.StoreId == storeId && x.EventType == MobileCatalogEventType.ProductScan),
            attributed.Where(x => x.StoreId == storeId).Sum(x => x.Quantity), attributed.Where(x => x.StoreId == storeId).Sum(x => x.Revenue))).OrderByDescending(x => x.Revenue).ToList();
        var consumerIds = events.Where(x => x.ConsumerAccountId.HasValue).Select(x => x.ConsumerAccountId!.Value).Distinct().ToArray();
        var memberships = await _db.LoyaltyMemberships.AsNoTracking().Where(x => x.TenantId == catalog.TenantId && consumerIds.Contains(x.ConsumerAccountId)).ToListAsync(ct);
        var membershipByConsumer = memberships.ToDictionary(x => x.ConsumerAccountId);
        var relevantMembershipIds = memberships.Select(x => x.Id).Concat(transactions.Where(x => x.LoyaltyMembershipId.HasValue).Select(x => x.LoyaltyMembershipId!.Value)).Distinct().ToArray();
        var firstPurchases = relevantMembershipIds.Length == 0 ? new Dictionary<Guid, DateTime>() : await _db.PosTransactions.AsNoTracking()
            .Where(x => x.TenantId == catalog.TenantId && x.LoyaltyMembershipId.HasValue && relevantMembershipIds.Contains(x.LoyaltyMembershipId.Value) && x.Status != "cancelled")
            .GroupBy(x => x.LoyaltyMembershipId!.Value).Select(g => new { MembershipId = g.Key, FirstAt = g.Min(x => x.CreatedAt) }).ToDictionaryAsync(x => x.MembershipId, x => x.FirstAt, ct);
        var tierIds = memberships.Select(x => x.CurrentTierId).Concat(transactions.Select(x => x.LoyaltyMembership?.CurrentTierId)).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var tierNames = await _db.LoyaltyTierDefinitions.AsNoTracking().Where(x => x.TenantId == catalog.TenantId && tierIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        CatalogAudienceAnalyticsDto Audience(string key, string label, Guid? tierId, Func<LoyaltyMembership, bool> memberMatch, Func<Guid, bool> purchaseMatch)
        {
            var segmentEvents = events.Where(x => x.ConsumerAccountId is Guid consumerId && membershipByConsumer.TryGetValue(consumerId, out var membership) && memberMatch(membership)).ToList();
            var segmentSales = attributed.Where(x => purchaseMatch(x.MembershipId)).ToList();
            return new CatalogAudienceAnalyticsDto(key, label, tierId,
                segmentEvents.Count(x => x.EventType == MobileCatalogEventType.CatalogView),
                segmentEvents.Count(x => x.EventType == MobileCatalogEventType.ProductScan),
                segmentSales.Sum(x => x.Quantity), segmentSales.Sum(x => x.Revenue));
        }
        var audience = new List<CatalogAudienceAnalyticsDto>
        {
            new("all", "Усі клієнти", null, catalogViews, events.Count(x => x.EventType == MobileCatalogEventType.ProductScan), totalPurchases, attributed.Sum(x => x.Revenue)),
            Audience("loyalty", "Учасники програми лояльності", null, _ => true, _ => true),
            Audience("new", "Нові покупці", null,
                membership => firstPurchases.TryGetValue(membership.Id, out var firstAt) && firstAt >= periodStart,
                membershipId => firstPurchases.TryGetValue(membershipId, out var firstAt) && firstAt >= periodStart),
            Audience("returning", "Постійні покупці", null,
                membership => firstPurchases.TryGetValue(membership.Id, out var firstAt) && firstAt < periodStart,
                membershipId => firstPurchases.TryGetValue(membershipId, out var firstAt) && firstAt < periodStart),
        };
        audience.AddRange(tierIds.Select(tierId => Audience($"tier:{tierId}", tierNames.GetValueOrDefault(tierId, "Рівень лояльності"), tierId,
            membership => membership.CurrentTierId == tierId,
            membershipId => transactions.Any(x => x.LoyaltyMembershipId == membershipId && x.LoyaltyMembership?.CurrentTierId == tierId))));
        return Ok(new CatalogAnalyticsDto(id, catalogViews, uniqueUsers, events.Count(x => x.EventType == MobileCatalogEventType.ProductView), events.Count(x => x.EventType == MobileCatalogEventType.ProductScan), totalPurchases, attributed.Sum(x => x.Revenue), catalogViews == 0 ? 0 : Math.Round(totalPurchases / catalogViews * 100, 2), productRows, daily, stores, audience, ConsumerOfferAttributionPolicy.Describe()));
    }

    [HttpPost("{id:guid}/banner")]
    public async Task<IActionResult> UploadBanner(Guid id, IFormFile file, CancellationToken ct)
    {
        var entity = await Load(id, false, ct); if (entity is null) return NotFound();
        if (file.Length is <= 0 or > 5 * 1024 * 1024) return BadRequest(new { error = "Image must be between 1 byte and 5 MB." });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant(); if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) return BadRequest(new { error = "Unsupported image format." });
        var folder = Path.Combine(_environment.WebRootPath ?? "wwwroot", "uploads", "mobile-catalogs"); Directory.CreateDirectory(folder);
        var name = $"{entity.TenantId:N}-{Guid.NewGuid():N}{ext}"; await using (var stream = System.IO.File.Create(Path.Combine(folder, name))) await file.CopyToAsync(stream, ct);
        entity.BannerUrl = $"/uploads/mobile-catalogs/{name}"; entity.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct); return Ok(new { bannerUrl = entity.BannerUrl });
    }

    private Task<MobileCatalogSettings?> Load(Guid id, bool tracking, CancellationToken ct)
    {
        var query = _db.MobileCatalogSettings.Include(x => x.Items).ThenInclude(x => x.Product).Include(x => x.Locations).Where(x => x.Id == id && x.TenantId == _tenant.TenantId!.Value);
        return (tracking ? query.AsNoTracking() : query).SingleOrDefaultAsync(ct);
    }

    private async Task<string?> Apply(MobileCatalogSettings entity, SaveMobileCatalogRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) return "Catalog title is required.";
        if (request.LayoutMode is not ("grid" or "list" or "featured")) return "Unsupported layout mode.";
        if (request.UnpublishAt.HasValue && request.UnpublishAt <= request.PublishAt) return "Unpublish date must be later than publish date.";
        if (request.Items.Select(x => x.ProductId).Distinct().Count() != request.Items.Count) return "Products must be unique.";
        if (request.Items.Any(x => x.MobileDiscountPercent is <= 0 or > 100)) return "Mobile discount must be between 0 and 100 percent.";
        if (request.LocationIds is null || request.LocationIds.Count == 0) return "Select at least one store.";
        if (request.LocationIds.Distinct().Count() != request.LocationIds.Count) return "Stores must be unique.";
        var locationCount = await _db.Locations.CountAsync(x => x.TenantId == entity.TenantId && x.IsActive && request.LocationIds.Contains(x.Id), ct);
        if (locationCount != request.LocationIds.Count) return "One or more stores are inactive or do not belong to the company.";
        var ids = request.Items.Select(x => x.ProductId).ToList(); var products = await _db.Items.Where(x => x.TenantId == entity.TenantId && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        if (products.Count != ids.Count) return "One or more products do not belong to the current catalog.";
        entity.Title = request.Title.Trim(); entity.Description = request.Description.Trim(); entity.LayoutMode = request.LayoutMode; entity.PublishAt = request.PublishAt; entity.UnpublishAt = request.UnpublishAt; entity.UpdatedAt = DateTime.UtcNow;
        var requestedIds = request.Items.Select(x => x.ProductId).ToHashSet();
        var removedItems = entity.Items.Where(x => !requestedIds.Contains(x.ProductId)).ToList();
        if (removedItems.Count > 0)
        {
            foreach (var removed in removedItems) entity.Items.Remove(removed);
        }

        var existingByProduct = entity.Items.ToDictionary(x => x.ProductId);
        for (var index = 0; index < request.Items.Count; index++)
        {
            var requested = request.Items[index];
            var product = products[requested.ProductId];
            decimal? mobilePrice = requested.MobileDiscountPercent is decimal discount && product.PriceRetail is decimal price
                ? Math.Round(price * (1 - discount / 100m), 2)
                : null;

            if (!existingByProduct.TryGetValue(product.Id, out var catalogItem))
            {
                catalogItem = new MobileCatalogItem
                {
                    TenantId = entity.TenantId,
                    SettingsId = entity.Id,
                    ProductId = product.Id,
                };
                entity.Items.Add(catalogItem);
            }

            catalogItem.SortOrder = index;
            catalogItem.IsFeatured = requested.IsFeatured;
            catalogItem.MobileDiscountPercent = requested.MobileDiscountPercent;
            catalogItem.ProductNameSnapshot = product.Name;
            catalogItem.UnitSnapshot = product.Unit;
            catalogItem.ImageUrlSnapshot = product.ImageUrl;
            catalogItem.RegularPriceSnapshot = product.PriceRetail;
            catalogItem.MobilePriceSnapshot = mobilePrice;
        }
        entity.Locations = request.LocationIds.Select(locationId => new MobileCatalogLocation { TenantId = entity.TenantId, SettingsId = entity.Id, LocationId = locationId }).ToList();
        return null;
    }

    private async Task<string?> ValidateForPublish(MobileCatalogSettings entity, CancellationToken ct)
    {
        if (entity.Items.Count == 0) return "Catalog must contain at least one product.";
        if (entity.Locations.Count == 0) return "Catalog must be assigned to at least one store.";
        var productIds = entity.Items.Select(x => x.ProductId).ToList();
        if (await _db.Items.AnyAsync(x => productIds.Contains(x.Id) && (!x.IsActive || x.PriceRetail == null), ct))
            return "All catalog products must be active and have a retail price.";
        return null;
    }

    private static MobileCatalogDto ToDto(MobileCatalogSettings x) => new(x.Id, x.Title, x.Description, x.BannerUrl, x.LayoutMode, x.IsEnabled, EffectiveStatus(x), x.PublishAt, x.UnpublishAt, x.CreatedAt, x.UpdatedAt, x.PublishedAt, x.ArchivedAt, x.Locations.Select(l => l.LocationId).ToList(), x.Items.OrderBy(i => i.SortOrder).Select(i => new MobileCatalogItemDto(i.ProductId, string.IsNullOrEmpty(i.ProductNameSnapshot) ? i.Product?.Name ?? "" : i.ProductNameSnapshot, i.ImageUrlSnapshot ?? i.Product?.ImageUrl, i.SortOrder, i.IsFeatured, i.MobileDiscountPercent, i.RegularPriceSnapshot, i.MobilePriceSnapshot)).ToList());
    private static string EffectiveStatus(MobileCatalogSettings x) => x.Status == MobileCatalogPublicationStatus.Archived || x.Status == MobileCatalogPublicationStatus.Draft ? x.Status : x.UnpublishAt <= DateTime.UtcNow ? MobileCatalogPublicationStatus.Archived : x.PublishAt > DateTime.UtcNow ? MobileCatalogPublicationStatus.Scheduled : MobileCatalogPublicationStatus.Published;
}

public sealed record SaveMobileCatalogRequest(string Title, string Description, string LayoutMode, DateTime PublishAt, DateTime? UnpublishAt, IReadOnlyList<Guid> LocationIds, IReadOnlyList<SaveMobileCatalogItemRequest> Items);
public sealed record SaveMobileCatalogItemRequest(Guid ProductId, bool IsFeatured, decimal? MobileDiscountPercent);
public sealed record MobileCatalogDto(Guid Id, string Title, string Description, string? BannerUrl, string LayoutMode, bool IsEnabled, string Status, DateTime PublishAt, DateTime? UnpublishAt, DateTime CreatedAt, DateTime UpdatedAt, DateTime? PublishedAt, DateTime? ArchivedAt, IReadOnlyList<Guid> LocationIds, IReadOnlyList<MobileCatalogItemDto> Items);
public sealed record MobileCatalogItemDto(Guid ProductId, string ProductName, string? ImageUrl, int SortOrder, bool IsFeatured, decimal? MobileDiscountPercent, decimal? RegularPriceSnapshot, decimal? MobilePriceSnapshot);
public sealed record CatalogAnalyticsDto(Guid CatalogId, int CatalogViews, int UniqueUsers, int ProductViews, int ProductScans, decimal Purchases, decimal Revenue, decimal ConversionPercent, IReadOnlyList<CatalogProductAnalyticsDto> Products, IReadOnlyList<CatalogDailyAnalyticsDto> Daily, IReadOnlyList<CatalogStoreAnalyticsDto> Stores, IReadOnlyList<CatalogAudienceAnalyticsDto> Audience, ConsumerOfferAttributionPolicyDto AttributionPolicy);
public sealed record CatalogDailyAnalyticsDto(DateTime Date, int CatalogViews, int ProductViews, int Scans, decimal Purchases, decimal Revenue);
public sealed record CatalogStoreAnalyticsDto(Guid StoreId, string StoreName, int CatalogViews, int Scans, decimal Purchases, decimal Revenue);
public sealed record CatalogProductAnalyticsDto(Guid ProductId, string ProductName, int Views, int Scans, decimal Purchases, decimal Revenue, decimal ViewToPurchasePercent);
public sealed record CatalogAudienceAnalyticsDto(string Key, string Label, Guid? TierId, int Reach, int Interactions, decimal Purchases, decimal Revenue);
