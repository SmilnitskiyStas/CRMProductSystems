using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Suppliers;
using ShelfGuard.Application.Features.Suppliers.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class SuppliersController : ControllerBase
{
    private readonly ISupplierService _suppliers;

    public SuppliersController(ISupplierService suppliers) => _suppliers = suppliers;

    [HttpGet]
    [ProducesResponseType(typeof(List<SupplierDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool include_inactive = false,
        CancellationToken ct = default)
    {
        var suppliers = await _suppliers.GetAllAsync(include_inactive, ct);
        return Ok(suppliers);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var supplier = await _suppliers.GetByIdAsync(id, ct);
        return supplier is null ? NotFound() : Ok(supplier);
    }

    [HttpPost]
    [Authorize(Policy = AppPolicies.AtLeastNetworkManager)]
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
    [Authorize(Policy = AppPolicies.AtLeastNetworkManager)]
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
    [Authorize(Policy = AppPolicies.AtLeastNetworkManager)]
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
