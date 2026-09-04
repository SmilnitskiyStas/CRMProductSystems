using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ItemRepository : IItemRepository
{
    private readonly AppDbContext _db;

    public ItemRepository(AppDbContext db) => _db = db;

    public async Task<List<Item>> GetAllAsync(
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        bool? uncategorized = null,
        CancellationToken ct = default)
    {
        var query = _db.Items
            .Include(p => p.Category)
            .Include(p => p.Segment)
            .Include(p => p.DefaultSupplier)
            .AsQueryable();

        query = await ApplyCategoryFilterAsync(query, categoryId, uncategorized, ct);

        if (segmentId.HasValue)
            query = query.Where(p => p.SegmentId == segmentId);

        if (!string.IsNullOrWhiteSpace(managementType))
            query = query.Where(p => p.ManagementType == managementType.ToUpperInvariant());

        return await query
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<(List<Item> Items, int Total)> GetPagedAsync(
        Guid? categoryId, Guid? segmentId, string? managementType, string? search, IReadOnlyList<Guid>? ids,
        string? sortBy, bool? sortDescending,
        int page, int pageSize,
        decimal? minPrice = null, decimal? maxPrice = null,
        bool? uncategorized = null,
        CancellationToken ct = default)
    {
        var query = _db.Items
            .Include(p => p.Category)
            .Include(p => p.Segment)
            .Include(p => p.DefaultSupplier)
            .AsQueryable();

        query = await ApplyCategoryFilterAsync(query, categoryId, uncategorized, ct);
        if (segmentId.HasValue)
            query = query.Where(p => p.SegmentId == segmentId);
        if (!string.IsNullOrWhiteSpace(managementType))
            query = query.Where(p => p.ManagementType == managementType.ToUpperInvariant());
        // TASK-640: min_price/max_price range filter on Item.PriceRetail for the frontend table
        // filter UI. `.HasValue` checks (never a truthy/non-zero check) — 0 is a valid bound.
        if (minPrice.HasValue)
            query = query.Where(p => p.PriceRetail >= minPrice);
        if (maxPrice.HasValue)
            query = query.Where(p => p.PriceRetail <= maxPrice);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Also match an exact barcode, not just a name substring — the mobile receiving
            // screen's manual "знайти вручну" fallback routes typed input through this same
            // search endpoint (it has no separate barcode field), so a scanned/typed barcode
            // that fails camera resolution must still be findable by pasting it here.
            var barcodeNeedle = JsonSerializer.Serialize(new[] { search.Trim() });
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, $"%{search}%") ||
                EF.Functions.JsonContains(p.Barcodes, barcodeNeedle));
        }
        if (ids is { Count: > 0 })
            query = query.Where(p => ids.Contains(p.Id));

        var total = await query.CountAsync(ct);
        query = ApplySort(query, sortBy, sortDescending);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <summary>
    /// B2 category filter. <c>uncategorized == true</c> → items with no category (overrides
    /// <paramref name="categoryId"/>). Otherwise a set <paramref name="categoryId"/> expands to
    /// that category's whole subtree: the <c>platform_categories</c> (Id, ParentId) pairs are
    /// pulled once and the descendant set is closed in memory (the tree is ~100 rows, flat
    /// today) — cheaper and simpler than a recursive CTE, and provider-safe (no RLS on
    /// platform_categories).
    /// </summary>
    private async Task<IQueryable<Item>> ApplyCategoryFilterAsync(
        IQueryable<Item> query, Guid? categoryId, bool? uncategorized, CancellationToken ct)
    {
        if (uncategorized == true)
            return query.Where(p => p.CategoryId == null);

        if (!categoryId.HasValue)
            return query;

        var tree = await _db.PlatformCategories
            .Select(c => new { c.Id, c.ParentId })
            .ToListAsync(ct);

        var wanted = new HashSet<Guid> { categoryId.Value };
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var n in tree)
                if (n.ParentId is Guid p && wanted.Contains(p) && wanted.Add(n.Id))
                    grew = true;
        }

        return query.Where(p => p.CategoryId != null && wanted.Contains(p.CategoryId.Value));
    }

    private static IQueryable<Item> ApplySort(IQueryable<Item> query, string? sortBy, bool? sortDescending)
    {
        var key = ItemSortKeys.Normalize(sortBy);

        // "name" (the pre-existing default, previously a bare OrderBy(Name) here) stays ascending
        // when the caller omits sortDescending — byte-identical to the old behavior. Any other
        // explicit key defaults to descending on omission, matching the Receipts/Transfers/
        // WriteOffs/Stock convention from this same week (TASK-630/631) — those precedents
        // (Stock's "productname", Receipts' "supplier"/"destination") default text columns to
        // descending too rather than special-casing by data type, so no per-key override here.
        var descending = sortDescending ?? key != ItemSortKeys.Default;

        return (key, descending) switch
        {
            // "barcode": Barcodes is a jsonb-mapped List<string> with no natural single sortable
            // scalar (first element, longest, etc. are all arbitrary) — and this exact column has
            // a documented history of LINQ shapes that build fine but throw against real Postgres
            // (see GetByBarcodeAsync's comment above). Extracting a raw jsonb array element via a
            // Postgres-specific SQL fragment is possible but not worth the complexity for a
            // "sort by barcode" nicety, so this key falls back to the same order as "name"
            // (documented judgment call, TASK-632) rather than risk a repeat of that failure mode.
            ("barcode", false) => query.OrderBy(p => p.Name),
            ("barcode", true) => query.OrderByDescending(p => p.Name),
            // Null category sorts last regardless of direction (typical UI expectation for an
            // "uncategorized" bucket) — achieved via a primary always-ascending "is null" key
            // (0 = has category, 1 = uncategorized) so only the secondary Name comparison flips
            // with the requested direction.
            ("category", false) => query.OrderBy(p => p.Category == null ? 1 : 0)
                .ThenBy(p => p.Category != null ? p.Category.Name : null),
            ("category", true) => query.OrderBy(p => p.Category == null ? 1 : 0)
                .ThenByDescending(p => p.Category != null ? p.Category.Name : null),
            ("purchaseprice", false) => query.OrderBy(p => p.PricePurchase),
            ("purchaseprice", true) => query.OrderByDescending(p => p.PricePurchase),
            ("retailprice", false) => query.OrderBy(p => p.PriceRetail),
            ("retailprice", true) => query.OrderByDescending(p => p.PriceRetail),
            ("minstock", false) => query.OrderBy(p => p.MinStock),
            ("minstock", true) => query.OrderByDescending(p => p.MinStock),
            ("maxstock", false) => query.OrderBy(p => p.MaxStock),
            ("maxstock", true) => query.OrderByDescending(p => p.MaxStock),
            (_, false) => query.OrderBy(p => p.Name),
            (_, true) => query.OrderByDescending(p => p.Name),
        };
    }

    public Task<Item?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Items
            .Include(p => p.Category)
            .Include(p => p.Segment)
            .Include(p => p.DefaultSupplier)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    // TASK-356: `p.Barcodes.Contains(barcode)` over a List<string> mapped to a jsonb
    // column (see AppDbContext: Item.Barcodes .HasColumnType("jsonb")) does NOT
    // translate to a working query — Npgsql's default `Contains` translation assumes
    // native Postgres array semantics and generates a query that tries (and fails) to
    // cast a text[] value to jsonb ("42846: cannot cast type text[] to jsonb"),
    // throwing on every call against a real database (confirmed live — this repo's
    // tests only ever exercised this method against an in-memory fake, never Postgres).
    // Same root cause as BUG-008 (AnalyticsRepository, ~jsonb `.Count`/indexer), just a
    // different LINQ shape. Fix: use the jsonb containment operator explicitly via
    // EF.Functions.JsonContains, which Npgsql does translate to "Barcodes" @> @value.
    public Task<Item?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var needle = JsonSerializer.Serialize(new[] { barcode });
        return _db.Items
            .Include(p => p.Category)
            .Include(p => p.Segment)
            .Include(p => p.DefaultSupplier)
            .FirstOrDefaultAsync(p => EF.Functions.JsonContains(p.Barcodes, needle), ct);
    }

    // TASK-596: batch variant for checkout, where GetByAnyBarcodeAsync is called once per
    // order line and a loop-of-N-queries-per-barcode-per-line would add up. A single-query
    // jsonb-overlap approach IS available here: EF.Functions.JsonExistAny(json, string[] keys)
    // — confirmed present in the installed Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11
    // package (reflected NpgsqlJsonDbFunctionsExtensions: JsonContains/JsonContained/
    // JsonExists/JsonExistAny/JsonExistAll/JsonTypeof) and translates to Postgres's `?|`
    // array-overlap operator ("Barcodes" ?| ARRAY[...]) — "does this jsonb array contain any
    // of these elements", exactly the semantics needed against Barcodes' `'[]'::jsonb` array
    // shape (same column GetByBarcodeAsync above already targets with JsonContains/`@>`).
    // One round trip regardless of how many barcodes are passed.
    public async Task<IReadOnlyList<Item>> GetByAnyBarcodeAsync(IReadOnlyList<string> barcodes, CancellationToken ct = default)
    {
        if (barcodes.Count == 0)
            return Array.Empty<Item>();

        var needles = barcodes as string[] ?? barcodes.ToArray();
        return await _db.Items
            .Include(p => p.Category)
            .Include(p => p.Segment)
            .Include(p => p.DefaultSupplier)
            .Where(p => EF.Functions.JsonExistAny(p.Barcodes, needles))
            .ToListAsync(ct);
    }

    // Slice 3: promo highlight on the catalog table. One query over the page's product ids,
    // aggregated across the tenant's stores (the catalog list itself is tenant-wide, not
    // store-scoped). Only `active` promo-reason / campaign-linked discounts count; an expiry /
    // overstock markdown is a different UX concern. RLS scopes `discounts` to the tenant.
    public async Task<IReadOnlyDictionary<Guid, ItemPromoInfo>> GetPromoStatesAsync(
        IReadOnlyList<Guid> productIds, int upcomingWithinDays, CancellationToken ct = default)
    {
        if (productIds.Count == 0)
            return new Dictionary<Guid, ItemPromoInfo>();

        var now = DateTime.UtcNow;
        var horizon = now.AddDays(Math.Max(1, upcomingWithinDays));
        var ids = productIds as Guid[] ?? productIds.ToArray();

        var rows = await _db.Discounts
            .Where(d => ids.Contains(d.ProductId)
                        && d.Status == DiscountStatus.Active
                        && (d.Reason == DiscountReason.Promo || d.PromotionCampaignId != null)
                        && (d.ValidUntil == null || d.ValidUntil >= now)
                        && d.ValidFrom <= horizon)
            .Select(d => new { d.ProductId, d.ValidFrom, d.DiscountPercent })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, ItemPromoInfo>();
        foreach (var g in rows.GroupBy(r => r.ProductId))
        {
            var active = g.Where(r => r.ValidFrom <= now).ToList();
            if (active.Count > 0)
            {
                var best = active.OrderByDescending(r => r.DiscountPercent).First();
                result[g.Key] = new ItemPromoInfo("active", null, best.DiscountPercent);
            }
            else
            {
                var soonest = g.OrderBy(r => r.ValidFrom).First();
                result[g.Key] = new ItemPromoInfo("upcoming", soonest.ValidFrom, soonest.DiscountPercent);
            }
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, ItemBufferSuggestion>> GetBufferSuggestionsAsync(
        IReadOnlyList<Guid> productIds, CancellationToken ct = default)
    {
        if (productIds.Count == 0)
            return new Dictionary<Guid, ItemBufferSuggestion>();

        var ids = productIds as Guid[] ?? productIds.ToArray();

        var bufferRows = await _db.ProductBuffers
            .Where(b => ids.Contains(b.ProductId))
            .Select(b => new { b.ProductId, b.BufferRed, b.BufferYellow, b.BufferTotal, b.CalculatedAt })
            .ToListAsync(ct);

        if (bufferRows.Count == 0)
            return new Dictionary<Guid, ItemBufferSuggestion>();

        var aduByProduct = (await _db.ProductAdus
                .Where(a => ids.Contains(a.ProductId) && a.AduEffective != null)
                .Select(a => new { a.ProductId, a.AduEffective })
                .ToListAsync(ct))
            .GroupBy(a => a.ProductId)
            .ToDictionary(g => g.Key, g => g.Max(a => a.AduEffective));

        var result = new Dictionary<Guid, ItemBufferSuggestion>();
        foreach (var g in bufferRows.GroupBy(b => b.ProductId))
        {
            // MAX across stores — the highest single figure is the safe tenant-wide default.
            var min = g.Max(b => b.BufferRed + b.BufferYellow); // reorder point (top of yellow)
            var max = g.Max(b => b.BufferTotal);                // max on-hand target (top of green)
            var safety = g.Max(b => b.BufferRed);               // DDMRP safety stock (red zone)
            aduByProduct.TryGetValue(g.Key, out var adu);
            result[g.Key] = new ItemBufferSuggestion(
                Math.Round(min, 2), Math.Round(max, 2), Math.Round(safety, 2),
                adu, g.Max(b => b.CalculatedAt));
        }
        return result;
    }

    public async Task<ItemPromoDetail?> GetPromoDetailAsync(
        Guid productId, int upcomingWithinDays, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(Math.Max(1, upcomingWithinDays));

        var rows = await _db.Discounts
            .Where(d => d.ProductId == productId
                        && d.Status == DiscountStatus.Active
                        && (d.Reason == DiscountReason.Promo || d.PromotionCampaignId != null)
                        && (d.ValidUntil == null || d.ValidUntil >= now)
                        && d.ValidFrom <= horizon)
            .Select(d => new { d.Id, d.ValidFrom, d.DiscountPercent })
            .ToListAsync(ct);

        if (rows.Count == 0) return null;

        // Same active-beats-upcoming, best-by-percent / soonest-by-date resolution as
        // GetPromoStatesAsync, scoped to this one product's own discounts across all stores.
        var active = rows.Where(r => r.ValidFrom <= now).ToList();
        var winner = active.Count > 0
            ? active.OrderByDescending(r => r.DiscountPercent).First()
            : rows.OrderBy(r => r.ValidFrom).First();
        var state = active.Count > 0 ? "active" : "upcoming";

        // The real order-formula forecast, never an invented one: only an APPLIED cannibalization
        // row for this exact discount+product counts (CannibalizationService.PromoProductCoefficient,
        // manager-approved). Most promos never go through that review — null here just means the
        // banner falls back to its plain "order will increase automatically" wording.
        var coefficient = await _db.PromoCannibalizations
            .Where(pc => pc.IsApplied && pc.DiscountId == winner.Id && pc.AffectedProductId == productId)
            .Select(pc => (decimal?)pc.OrderCoefficient)
            .FirstOrDefaultAsync(ct);

        return new ItemPromoDetail(state, active.Count > 0 ? null : winner.ValidFrom, winner.DiscountPercent, coefficient);
    }

    public Task<List<ProductSupplierSetting>> GetSupplierSettingsAsync(Guid productId, CancellationToken ct = default) =>
        _db.ProductSupplierSettings
            .Include(s => s.Supplier)
            .Where(s => s.ProductId == productId)
            .OrderByDescending(s => s.IsPrimary)
            .ThenBy(s => s.Supplier!.Name)
            .ToListAsync(ct);

    public Task<bool> SupplierSettingExistsAsync(Guid productId, Guid supplierId, CancellationToken ct = default) =>
        _db.ProductSupplierSettings
            .AnyAsync(s => s.ProductId == productId && s.SupplierId == supplierId, ct);

    public async Task AddAsync(Item product, CancellationToken ct = default) =>
        await _db.Items.AddAsync(product, ct);

    public async Task AddSupplierSettingAsync(ProductSupplierSetting setting, CancellationToken ct = default) =>
        await _db.ProductSupplierSettings.AddAsync(setting, ct);

    public void Update(Item product) =>
        _db.Items.Update(product);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
