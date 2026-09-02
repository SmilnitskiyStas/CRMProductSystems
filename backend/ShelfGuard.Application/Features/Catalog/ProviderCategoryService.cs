using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Catalog;

public sealed class ProviderCategoryService : IProviderCategoryService
{
    // Mirrors Tenant.UpdateBusinessType's allow-list (Domain/Entities/Tenant.cs) — inlined here
    // rather than shared so the two lists can diverge if a business type is ever category-only.
    private static readonly string[] ValidBusinessTypes =
    {
        "retail", "auto_service", "warehouse", "restaurant",
        "production", "distribution", "pharmacy", "floristry", "supplier",
    };

    private readonly ICategoryRepository _repo;

    public ProviderCategoryService(ICategoryRepository repo) => _repo = repo;

    public async Task<List<PlatformCategoryDto>> GetAllAsync(CancellationToken ct = default)
    {
        var categories = await _repo.GetAllAsync(ct);
        var counts = await _repo.CountItemsByCategoryAsync(ct);
        return categories.Select(c => ToDto(c, counts)).ToList();
    }

    public async Task<(PlatformCategoryDto? Dto, string? Error)> CreateAsync(
        CreatePlatformCategoryRequest request, CancellationToken ct = default)
    {
        if (NormalizeName(request.Name) is not { } name)
            return (null, "Category name is required.");

        var (businessTypes, btError) = NormalizeBusinessTypes(request.BusinessTypes);
        if (btError is not null)
            return (null, btError);

        if (request.ParentId is Guid parentId)
        {
            var parentError = await ValidateParentAsync(parentId, selfId: null, ct);
            if (parentError is not null)
                return (null, parentError);
        }

        var category = new PlatformCategory
        {
            Name = name,
            ParentId = request.ParentId,
            BusinessTypes = businessTypes,
            SortOrder = request.SortOrder ?? 0,
            IsActive = true,
        };

        await _repo.AddAsync(category, ct);
        await _repo.SaveChangesAsync(ct);

        // Freshly created — no items point at it yet.
        return (ToDto(category, new Dictionary<Guid, int>()), null);
    }

    public async Task<(PlatformCategoryDto? Dto, string? Error)> UpdateAsync(
        Guid id, UpdatePlatformCategoryRequest request, CancellationToken ct = default)
    {
        var category = await _repo.GetByIdAsync(id, ct);
        if (category is null)
            return (null, "Category not found.");

        if (NormalizeName(request.Name) is not { } name)
            return (null, "Category name is required.");

        var (businessTypes, btError) = NormalizeBusinessTypes(request.BusinessTypes);
        if (btError is not null)
            return (null, btError);

        if (request.ParentId is Guid parentId)
        {
            var parentError = await ValidateParentAsync(parentId, selfId: id, ct);
            if (parentError is not null)
                return (null, parentError);
        }

        category.Name = name;
        category.ParentId = request.ParentId;
        category.BusinessTypes = businessTypes;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;

        _repo.Update(category);
        await _repo.SaveChangesAsync(ct);

        var counts = await _repo.CountItemsByCategoryAsync(ct);
        return (ToDto(category, counts), null);
    }

    public async Task<string?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var category = await _repo.GetByIdAsync(id, ct);
        if (category is null)
            return "Category not found.";

        if (await _repo.HasActiveChildrenAsync(id, ct))
            return "Category has active sub-categories.";

        // Soft-delete only — items keep the FK; the business-type filter hides the row.
        category.IsActive = false;
        _repo.Update(category);
        await _repo.SaveChangesAsync(ct);
        return null;
    }

    // ── validation helpers ─────────────────────────────────────────────────

    private static string? NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var name = raw.Trim();
        return name.Length > 255 ? null : name;
    }

    private static (List<string> Values, string? Error) NormalizeBusinessTypes(string[]? raw)
    {
        var normalized = new List<string>();
        if (raw is null || raw.Length == 0)
            return (normalized, null); // empty = "all business types"

        foreach (var value in raw)
        {
            var bt = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(bt)) continue;
            if (!ValidBusinessTypes.Contains(bt))
                return (normalized,
                    $"Unknown business type '{value}'. Valid: {string.Join(", ", ValidBusinessTypes)}.");
            if (!normalized.Contains(bt)) normalized.Add(bt);
        }
        return (normalized, null);
    }

    /// <summary>
    /// Parent must resolve to an existing category and must not create a cycle: walk
    /// <c>ParentId</c> up from the proposed parent — reject if it reaches <paramref name="selfId"/>
    /// (self-parent or descendant-as-parent on update) or loops on a pre-existing corrupt chain.
    /// </summary>
    private async Task<string?> ValidateParentAsync(Guid parentId, Guid? selfId, CancellationToken ct)
    {
        if (selfId is Guid self && parentId == self)
            return "Category parent would create a cycle.";

        var all = await _repo.GetAllAsync(ct);
        var byId = all.ToDictionary(c => c.Id, c => c.ParentId);

        if (!byId.ContainsKey(parentId))
            return "Category parent does not exist.";

        var seen = new HashSet<Guid>();
        Guid? cursor = parentId;
        while (cursor is Guid node)
        {
            if (selfId is Guid s && node == s) return "Category parent would create a cycle.";
            if (!seen.Add(node)) return "Category parent would create a cycle.";
            cursor = byId.TryGetValue(node, out var next) ? next : null;
        }
        return null;
    }

    private static PlatformCategoryDto ToDto(PlatformCategory c, IReadOnlyDictionary<Guid, int> counts) => new(
        c.Id,
        c.Name,
        c.ParentId,
        c.BusinessTypes.ToArray(),
        c.SortOrder,
        c.IsActive,
        counts.TryGetValue(c.Id, out var count) ? count : 0);
}
