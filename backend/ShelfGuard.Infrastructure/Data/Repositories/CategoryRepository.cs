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

    // Phase 6e: typeahead over active categories. `platform_categories` has no RLS (global
    // reference data) so the ILIKE carries no leakproof/index concern — same note as
    // AudienceBuilderRepository.SearchCategoriesAsync. The per-tenant item count reads `items`,
    // whose own RLS already scopes to the caller; the explicit TenantId predicate is
    // defence-in-depth and keeps the count correct under any session role.
    public async Task<IReadOnlyList<CategorySearchRow>> SearchActiveAsync(
        Guid tenantId, string? term, int limit, CancellationToken ct = default)
    {
        var t = term?.Trim() ?? string.Empty;

        var q = _db.PlatformCategories.AsNoTracking().Where(c => c.IsActive);
        if (t.Length > 0)
            q = q.Where(c => EF.Functions.ILike(c.Name, $"%{t}%"));

        var cats = await q.OrderBy(c => c.Name).Take(limit).ToListAsync(ct);
        if (cats.Count == 0)
            return Array.Empty<CategorySearchRow>();

        var ids = cats.Select(c => c.Id).ToList();
        var parentIds = cats.Where(c => c.ParentId != null)
                            .Select(c => c.ParentId!.Value).Distinct().ToList();

        var parentNames = parentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.PlatformCategories.AsNoTracking()
                .Where(c => parentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var counts = await _db.Items.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.CategoryId != null && ids.Contains(i.CategoryId.Value))
            .GroupBy(i => i.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);

        return cats.Select(c => new CategorySearchRow(
            c.Id,
            c.Name,
            c.ParentId is { } pid && parentNames.TryGetValue(pid, out var pn) ? pn : null,
            counts.TryGetValue(c.Id, out var cnt) ? cnt : 0)).ToList();
    }

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
