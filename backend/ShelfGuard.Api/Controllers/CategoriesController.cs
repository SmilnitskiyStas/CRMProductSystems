using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Read-only category lookup for the catalog filter dropdown (TASK-632). No create/update/delete
/// here — the global <c>platform_categories</c> catalogue is provider-curated via
/// <c>api/provider/categories</c>. This endpoint returns only the entries visible to the
/// caller's tenant business type (B2).
/// </summary>
[ApiController]
[Route("api/categories")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categories;
    private readonly ITenantContext _tenantContext;

    public CategoriesController(ICategoryService categories, ITenantContext tenantContext)
    {
        _categories = categories;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var categories = await _categories.GetAllAsync(_tenantContext.TenantId, ct);
        return Ok(categories);
    }

    /// <summary>
    /// Category typeahead (supplier-portal expansion #8, Phase 6e): case-insensitive name match
    /// over ALL active categories (not business-type-filtered — a supplier sells across
    /// verticals). <c>limit</c> defaults to 20, capped at 50. Each hit carries its parent name
    /// (disambiguation) and the caller tenant's own item count in that category.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<CategorySearchResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null) return Forbid();

        var results = await _categories.SearchAsync(tenantId.Value, q, limit, ct);
        return Ok(results);
    }
}
