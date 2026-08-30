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
        CancellationToken ct = default)
    {
        var query = _db.Items
            .Include(p => p.Category)
            .Include(p => p.Segment)
            .Include(p => p.DefaultSupplier)
            .AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId);

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
        CancellationToken ct = default)
    {
        var query = _db.Items
            .Include(p => p.Category)
            .Include(p => p.Segment)
            .Include(p => p.DefaultSupplier)
            .AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId);
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
