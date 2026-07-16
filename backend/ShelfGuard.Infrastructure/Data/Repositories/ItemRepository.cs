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
        Guid? categoryId, Guid? segmentId, string? managementType,
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
