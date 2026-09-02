using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Catalog;

public sealed class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _repo;
    private readonly ITenantRepository _tenantRepo;

    public CategoryService(ICategoryRepository repo, ITenantRepository tenantRepo)
    {
        _repo = repo;
        _tenantRepo = tenantRepo;
    }

    public async Task<List<CategoryDto>> GetAllAsync(Guid? tenantId, CancellationToken ct = default)
    {
        var all = await _repo.GetAllActiveAsync(ct);

        string? businessType = null;
        if (tenantId is Guid tid)
            businessType = (await _tenantRepo.GetByIdAsync(tid, ct))?.BusinessType;

        // Filter in memory: BusinessTypes is a jsonb List<string> — .Contains/.Count in a
        // LINQ-to-SQL Where does NOT translate for jsonb (documented gotcha, see
        // ItemRepository.cs:141-159). ~100 rows, cached 5 min on the frontend.
        var visible = businessType is null
            ? all // provider / no tenant → every active category
            : all.Where(c => c.BusinessTypes.Count == 0
                          || c.BusinessTypes.Contains(businessType, StringComparer.OrdinalIgnoreCase));

        return visible.Select(ToDto).ToList();
    }

    private static CategoryDto ToDto(PlatformCategory c) => new(c.Id, c.Name, c.ParentId);
}
