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

    public Task<Item?> GetByBarcodeAsync(string barcode, CancellationToken ct = default) =>
        _db.Items
            .Include(p => p.Category)
            .Include(p => p.Segment)
            .Include(p => p.DefaultSupplier)
            .FirstOrDefaultAsync(p => p.Barcode == barcode, ct);

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
