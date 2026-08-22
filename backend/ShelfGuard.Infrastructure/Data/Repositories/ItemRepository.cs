using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
        int page, int pageSize, CancellationToken ct = default)
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
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => EF.Functions.ILike(p.Name, $"%{search}%"));
        if (ids is { Count: > 0 })
            query = query.Where(p => ids.Contains(p.Id));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
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
