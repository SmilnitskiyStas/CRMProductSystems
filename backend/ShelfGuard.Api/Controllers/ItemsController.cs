using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/items")]
[Authorize(Policy = AppPolicies.CanViewStock)]
public sealed class ItemsController : ControllerBase
{
    private readonly IItemService _catalog;

    public ItemsController(IItemService catalog) => _catalog = catalog;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? category_id,
        [FromQuery] Guid? segment_id,
        [FromQuery] string? management_type,
        [FromQuery] string? search = null,
        [FromQuery(Name = "ids")] Guid[]? ids = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool? sortDescending = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] decimal? min_price = null,
        [FromQuery] decimal? max_price = null,
        [FromQuery] bool? uncategorized = null,
        CancellationToken ct = default)
    {
        // tenantId is accepted by the service but not yet used for filtering (RLS handles isolation at DB level).
        // Provider users have no tenant_id claim — still allowed to view the global catalog.
        var tenantId = GetTenantId() ?? Guid.Empty;

        // ids means "give me exactly these," not a paginated browse (TASK-570/572, ADR-032) —
        // clamp to 30 (registry's MaxItems bound for curatable blocks) and force page 1/pageSize 30
        // regardless of the caller's own page/pageSize.
        List<Guid>? clampedIds = null;
        if (ids is { Length: > 0 })
        {
            clampedIds = ids.Take(30).ToList();
            page = 1;
            pageSize = 30;
        }

        var query = new PagedQuery { Page = page, PageSize = pageSize };
        var result = await _catalog.GetPagedAsync(
            tenantId, category_id, segment_id, management_type, search, clampedIds, sortBy, sortDescending,
            query.ClampedPage, query.ClampedPageSize, min_price, max_price, uncategorized, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var product = await _catalog.GetByIdAsync(id, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpGet("by-barcode/{code}")]
    [ProducesResponseType(typeof(ItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByBarcode(string code, CancellationToken ct)
    {
        var product = await _catalog.GetByBarcodeAsync(code, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(ItemDto), StatusCodes.Status201Created)]
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
    [ProducesResponseType(typeof(ItemDto), StatusCodes.Status200OK)]
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

    [HttpGet("lookup")]
    [ProducesResponseType(typeof(BarcodeProductLookupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LookupByBarcode([FromQuery] string barcode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return BadRequest(new { error = "Barcode is required." });

        var result = await _catalog.LookupByBarcodeExternalAsync(barcode, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/image")]
    [Authorize(Policy = AppPolicies.AtLeastStoreManager)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { error = "File too large. Max 5 MB." });

        using var stream = file.OpenReadStream();
        var (url, error) = await _catalog.UploadImageAsync(id, stream, file.FileName, ct);

        if (error == "Product not found.") return NotFound(new { error });
        if (error is not null) return BadRequest(new { error });

        return Ok(new { imageUrl = url });
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
