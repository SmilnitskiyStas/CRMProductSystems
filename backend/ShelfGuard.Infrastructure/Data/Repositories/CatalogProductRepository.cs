using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class CatalogProductRepository : ICatalogProductRepository
{
    private readonly AppDbContext _db;

    public CatalogProductRepository(AppDbContext db) => _db = db;

    public async Task<List<CatalogProduct>> GetAllAsync(
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        CancellationToken ct = default)
    {
        var query = _db.CatalogProducts
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

    public Task<CatalogProduct?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.CatalogProducts
            .Include(p => p.Category)
            .Include(p => p.Segment)
            .Include(p => p.DefaultSupplier)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<CatalogProduct?> GetByBarcodeAsync(string barcode, CancellationToken ct = default) =>
        _db.CatalogProducts
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

    public async Task AddAsync(CatalogProduct product, CancellationToken ct = default) =>
        await _db.CatalogProducts.AddAsync(product, ct);

    public async Task AddSupplierSettingAsync(ProductSupplierSetting setting, CancellationToken ct = default) =>
        await _db.ProductSupplierSettings.AddAsync(setting, ct);

    public void Update(CatalogProduct product) =>
        _db.CatalogProducts.Update(product);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
