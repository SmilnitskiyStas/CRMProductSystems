using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Read-only category lookup for the catalog filter dropdown (TASK-632). No create/update/delete
/// here — categories are managed elsewhere (seed data / future admin UI); this endpoint exists
/// purely so the Catalog page can populate a category filter.
/// </summary>
[ApiController]
[Route("api/categories")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categories;

    public CategoriesController(ICategoryService categories) => _categories = categories;

    [HttpGet]
    [ProducesResponseType(typeof(List<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var categories = await _categories.GetAllAsync(ct);
        return Ok(categories);
    }
}
