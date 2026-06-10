using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/products")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class CatalogController : ControllerBase
{
    private readonly ICatalogProductService _catalog;

    public CatalogController(ICatalogProductService catalog) => _catalog = catalog;

    [HttpGet]
    [ProducesResponseType(typeof(List<CatalogProductDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? category_id,
        [FromQuery] Guid? segment_id,
        [FromQuery] string? management_type,
        CancellationToken ct)
    {
        // tenantId is accepted by the service but not yet used for filtering (RLS handles isolation at DB level).
        // Provider users have no tenant_id claim — still allowed to view the global catalog.
        var tenantId = GetTenantId() ?? Guid.Empty;
        var products = await _catalog.GetAllAsync(tenantId, category_id, segment_id, management_type, ct);
        return Ok(products);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CatalogProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var product = await _catalog.GetByIdAsync(id, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("by-barcode/{code}")]
    [ProducesResponseType(typeof(CatalogProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByBarcode(string code, CancellationToken ct)
    {
        var product = await _catalog.GetByBarcodeAsync(code, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(CatalogProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null)
            return Forbid();

        var (product, error) = await _catalog.CreateAsync(tenantId.Value, request, ct);
        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = product!.Id }, product);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(CatalogProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var (product, error) = await _catalog.UpdateAsync(id, request, ct);

        if (error == "Product not found.")
            return NotFound();

        if (error is not null)
            return BadRequest(new { error });

        return Ok(product);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _catalog.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("{id:guid}/suppliers")]
    [ProducesResponseType(typeof(List<ProductSupplierSettingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSuppliers(Guid id, CancellationToken ct)
    {
        var settings = await _catalog.GetSuppliersAsync(id, ct);
        return Ok(settings);
    }

    [HttpPost("{id:guid}/suppliers")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ProductSupplierSettingDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddSupplier(Guid id, AddProductSupplierRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null)
            return Forbid();

        var (setting, error) = await _catalog.AddSupplierAsync(id, tenantId.Value, request, ct);

        if (error == "Product not found.")
            return NotFound(new { error });

        if (error is not null)
            return BadRequest(new { error });

        return CreatedAtAction(nameof(GetSuppliers), new { id }, setting);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
