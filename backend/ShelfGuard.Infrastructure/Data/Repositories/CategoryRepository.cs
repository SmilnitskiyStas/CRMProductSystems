using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class CategoryRepository : ICategoryRepository
{
    private readonly AppDbContext _db;

    public CategoryRepository(AppDbContext db) => _db = db;

    // B1: `platform_categories` is a global, provider-curated table — no TenantId, no RLS.
    // Every tenant reads the same rows; B2 narrows the result to the caller's
    // Tenant.BusinessType (in CategoryService, in memory — jsonb List<string> doesn't
    // translate to a LINQ-to-SQL Where, same gotcha as ItemRepository's barcode column).
    public Task<List<PlatformCategory>> GetAllActiveAsync(CancellationToken ct = default) =>
        _db.PlatformCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public Task<List<PlatformCategory>> GetAllAsync(CancellationToken ct = default) =>
        _db.PlatformCategories
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

    public Task<PlatformCategory?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PlatformCategories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> ActiveExistsAsync(Guid id, CancellationToken ct = default) =>
        _db.PlatformCategories.AnyAsync(c => c.Id == id && c.IsActive, ct);

    public Task<bool> HasActiveChildrenAsync(Guid parentId, CancellationToken ct = default) =>
        _db.PlatformCategories.AnyAsync(c => c.ParentId == parentId && c.IsActive, ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountItemsByCategoryAsync(CancellationToken ct = default) =>
        await _db.Items
            .Where(i => i.CategoryId != null)
            .GroupBy(i => i.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);

    public Task AddAsync(PlatformCategory category, CancellationToken ct = default) =>
        _db.PlatformCategories.AddAsync(category, ct).AsTask();

    public void Update(PlatformCategory category) =>
        _db.PlatformCategories.Update(category);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
