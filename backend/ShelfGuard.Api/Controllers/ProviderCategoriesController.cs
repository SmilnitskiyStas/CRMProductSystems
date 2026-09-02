using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Provider (super admin) CRUD over the global <c>platform_categories</c> catalogue (B2).
/// No tenant scoping — the provider curates one tree for every tenant; each tenant then reads
/// the business-type-filtered subset via <c>GET /api/categories</c>.
/// </summary>
[ApiController]
[Route("api/provider/categories")]
[Authorize(Policy = AppPolicies.ProviderOnly)]
public sealed class ProviderCategoriesController : ControllerBase
{
    private readonly IProviderCategoryService _categories;

    public ProviderCategoriesController(IProviderCategoryService categories) => _categories = categories;

    /// <summary>Full tree — all business types, incl. inactive — ordered SortOrder then Name.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<PlatformCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var categories = await _categories.GetAllAsync(ct);
        return Ok(categories);
    }

    [HttpPost]
    [ProducesResponseType(typeof(PlatformCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreatePlatformCategoryRequest request, CancellationToken ct)
    {
        var (dto, error) = await _categories.CreateAsync(request, ct);
        if (error is not null)
            return MapError(error);
        return CreatedAtAction(nameof(GetAll), new { id = dto!.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PlatformCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePlatformCategoryRequest request, CancellationToken ct)
    {
        var (dto, error) = await _categories.UpdateAsync(id, request, ct);
        return error is null ? Ok(dto) : MapError(error);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var error = await _categories.DeleteAsync(id, ct);
        return error is null ? NoContent() : MapError(error);
    }

    private IActionResult MapError(string error) =>
        error.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? NotFound(new { error })
            : BadRequest(new { error });
}
