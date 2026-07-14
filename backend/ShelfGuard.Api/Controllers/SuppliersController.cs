using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Suppliers;
using ShelfGuard.Application.Features.Suppliers.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

// ADR-020 (TASK-346): no class-level policy — Get* keep the pre-existing AtLeastStoreManager
// floor OR "suppliers.view"; Create/Update/Delete keep the pre-existing, tighter
// AtLeastNetworkManager floor OR "suppliers.manage" (must NOT loosen to StoreManager).
[ApiController]
[Route("api/suppliers")]
public sealed class SuppliersController : ControllerBase
{
    private readonly ISupplierService _suppliers;

    public SuppliersController(ISupplierService suppliers) => _suppliers = suppliers;

    [HttpGet]
    [Authorize(Policy = AppPolicies.SuppliersViewOrCapability)]
    [ProducesResponseType(typeof(PagedResult<SupplierDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool include_inactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = new PagedQuery { Page = page, PageSize = pageSize };
        var result = await _suppliers.GetPagedAsync(include_inactive, query.ClampedPage, query.ClampedPageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPolicies.SuppliersViewOrCapability)]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var supplier = await _suppliers.GetByIdAsync(id, ct);
        return supplier is null ? NotFound() : Ok(supplier);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.SuppliersManageOrCapability)]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateSupplierRequest request, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (supplier, error) = await _suppliers.CreateAsync(tenantId.Value, request, ct);
        if (error is not null) return BadRequest(new { error });

        return CreatedAtAction(nameof(GetById), new { id = supplier!.Id }, supplier);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AppPolicies.SuppliersManageOrCapability)]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateSupplierRequest request, CancellationToken ct)
    {
        var (supplier, error) = await _suppliers.UpdateAsync(id, request, ct);

        if (error == "Supplier not found.") return NotFound();
        if (error is not null) return BadRequest(new { error });

        return Ok(supplier);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AppPolicies.SuppliersManageOrCapability)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _suppliers.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
