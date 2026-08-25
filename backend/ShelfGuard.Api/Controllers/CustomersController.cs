using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ConsumerProfile.Dtos;
using ShelfGuard.Application.Features.Customers;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Policy = AppPolicies.AtLeastStoreManager)]
public sealed class CustomersController : ControllerBase
{
    private readonly ICustomerService _customers;

    public CustomersController(ICustomerService customers) => _customers = customers;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CustomerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var query = new PagedQuery { Page = page, PageSize = pageSize };
        var result = await _customers.GetPagedAsync(tenantId.Value, query.ClampedPage, query.ClampedPageSize, search, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var customer = await _customers.GetByIdAsync(id, tenantId.Value, ct);
        return customer is null ? NotFound() : Ok(customer);
    }

    /// <summary>
    /// TASK-621b. Staff-facing view of a customer's linked <c>ConsumerAccountProfileChange</c>
    /// history — separate paged endpoint (not folded into <see cref="CustomerDetailDto"/>) since
    /// history can be long and the customer-detail drawer only loads it when that tab opens.
    /// Empty page (not 404) when the customer has no linked loyalty membership.
    /// </summary>
    [HttpGet("{id:guid}/profile-history")]
    [ProducesResponseType(typeof(PagedResult<ConsumerProfileChangeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfileHistory(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var query = new PagedQuery { Page = page, PageSize = pageSize };
        var result = await _customers.GetProfileChangeHistoryAsync(
            id, tenantId.Value, query.ClampedPage, query.ClampedPageSize, ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateCustomerDto dto, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (customer, error) = await _customers.CreateAsync(tenantId.Value, dto, ct);
        if (error is not null)
        {
            if (error.Contains("already exists"))
                return Conflict(new { error });
            return BadRequest(new { error });
        }

        return CreatedAtAction(nameof(GetById), new { id = customer!.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, UpdateCustomerDto dto, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var (customer, error) = await _customers.UpdateAsync(id, tenantId.Value, dto, ct);

        if (error == "Customer not found.") return NotFound();
        if (error is not null)
        {
            if (error.Contains("already exists"))
                return Conflict(new { error });
            return BadRequest(new { error });
        }

        return Ok(customer);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId is null) return Forbid();

        var deleted = await _customers.DeleteAsync(id, tenantId.Value, ct);
        return deleted ? NoContent() : NotFound();
    }

    private Guid? GetTenantId()
    {
        var claim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
