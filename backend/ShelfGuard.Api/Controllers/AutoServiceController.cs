using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.AutoService;
using ShelfGuard.Application.Features.AutoService.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Auto Service Module — Customers, Vehicles, Service Catalog, Work Orders.
/// All endpoints require authentication and the "auto_service" module.
/// Thin controller: zero business logic — delegates entirely to IAutoServiceService.
/// </summary>
[ApiController]
[Route("api/auto-service")]
[Authorize]
[RequireModule("auto_service")]
public sealed class AutoServiceController : ControllerBase
{
    private readonly IAutoServiceService _svc;

    public AutoServiceController(IAutoServiceService svc) => _svc = svc;

    // ─────────────────────────────────────────────────────────────────────────
    // Customers
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("customers")]
    [ProducesResponseType(typeof(List<CustomerListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] string? search, CancellationToken ct)
    {
        var result = await _svc.GetCustomersAsync(search, ct);
        return Ok(result);
    }

    [HttpGet("customers/{id:guid}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerById(Guid id, CancellationToken ct)
    {
        var result = await _svc.GetCustomerByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("customers")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCustomer(
        [FromBody] CustomerCreateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { error = "Name is required." });

        var result = await _svc.CreateCustomerAsync(dto, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("customers/{id:guid}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCustomer(
        Guid id, [FromBody] CustomerUpdateDto dto, CancellationToken ct)
    {
        var (customer, error, statusCode) = await _svc.UpdateCustomerAsync(id, dto, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return Ok(customer);
    }

    [HttpDelete("customers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCustomer(Guid id, CancellationToken ct)
    {
        var (ok, error, statusCode) = await _svc.DeleteCustomerAsync(id, ct);
        if (!ok) return StatusCode(statusCode!.Value, new { error });
        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Vehicles
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("vehicles")]
    [ProducesResponseType(typeof(List<VehicleListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVehicles(
        [FromQuery] Guid? customerId, CancellationToken ct)
    {
        var result = await _svc.GetVehiclesAsync(customerId, ct);
        return Ok(result);
    }

    [HttpGet("vehicles/{id:guid}")]
    [ProducesResponseType(typeof(VehicleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVehicleById(Guid id, CancellationToken ct)
    {
        var result = await _svc.GetVehicleByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("vehicles")]
    [ProducesResponseType(typeof(VehicleDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateVehicle(
        [FromBody] VehicleCreateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Brand) || string.IsNullOrWhiteSpace(dto.Model))
            return BadRequest(new { error = "Brand and Model are required." });

        var (vehicle, error, statusCode) = await _svc.CreateVehicleAsync(dto, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return StatusCode(StatusCodes.Status201Created, vehicle);
    }

    [HttpPut("vehicles/{id:guid}")]
    [ProducesResponseType(typeof(VehicleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateVehicle(
        Guid id, [FromBody] VehicleUpdateDto dto, CancellationToken ct)
    {
        var (vehicle, error, statusCode) = await _svc.UpdateVehicleAsync(id, dto, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return Ok(vehicle);
    }

    [HttpDelete("vehicles/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteVehicle(Guid id, CancellationToken ct)
    {
        var (ok, error, statusCode) = await _svc.DeleteVehicleAsync(id, ct);
        if (!ok) return StatusCode(statusCode!.Value, new { error });
        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Service Catalog
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("service-catalog")]
    [ProducesResponseType(typeof(List<ServiceCatalogItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetServiceCatalog(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _svc.GetServiceCatalogAsync(includeInactive, ct);
        return Ok(result);
    }

    [HttpPost("service-catalog")]
    [ProducesResponseType(typeof(ServiceCatalogItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateServiceCatalogItem(
        [FromBody] ServiceCatalogCreateDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { error = "Name is required." });

        var result = await _svc.CreateServiceCatalogItemAsync(dto, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("service-catalog/{id:guid}")]
    [ProducesResponseType(typeof(ServiceCatalogItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateServiceCatalogItem(
        Guid id, [FromBody] ServiceCatalogUpdateDto dto, CancellationToken ct)
    {
        var (item, error, statusCode) = await _svc.UpdateServiceCatalogItemAsync(id, dto, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return Ok(item);
    }

    [HttpDelete("service-catalog/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteServiceCatalogItem(Guid id, CancellationToken ct)
    {
        var (ok, error, statusCode) = await _svc.DeleteServiceCatalogItemAsync(id, ct);
        if (!ok) return StatusCode(statusCode!.Value, new { error });
        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Work Orders
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("work-orders")]
    [ProducesResponseType(typeof(List<WorkOrderListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkOrders(
        [FromQuery] string? status,
        [FromQuery] Guid? vehicleId,
        [FromQuery] Guid? mechanicUserId,
        CancellationToken ct)
    {
        var result = await _svc.GetWorkOrdersAsync(status, vehicleId, mechanicUserId, ct);
        return Ok(result);
    }

    [HttpGet("work-orders/{id:guid}")]
    [ProducesResponseType(typeof(WorkOrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkOrderById(Guid id, CancellationToken ct)
    {
        var result = await _svc.GetWorkOrderByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("work-orders")]
    [ProducesResponseType(typeof(WorkOrderDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateWorkOrder(
        [FromBody] WorkOrderCreateDto dto, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (order, error, statusCode) = await _svc.CreateWorkOrderAsync(dto, tenantId.Value, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return StatusCode(StatusCodes.Status201Created, order);
    }

    [HttpPut("work-orders/{id:guid}")]
    [ProducesResponseType(typeof(WorkOrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateWorkOrder(
        Guid id, [FromBody] WorkOrderUpdateDto dto, CancellationToken ct)
    {
        var (order, error, statusCode) = await _svc.UpdateWorkOrderAsync(id, dto, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return Ok(order);
    }

    [HttpPost("work-orders/{id:guid}/lines")]
    [ProducesResponseType(typeof(WorkOrderLineDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddLine(
        Guid id, [FromBody] WorkOrderLineCreateDto dto, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null) return Forbid();

        var (line, error, statusCode) = await _svc.AddLineAsync(id, dto, tenantId.Value, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return StatusCode(StatusCodes.Status201Created, line);
    }

    [HttpDelete("work-orders/{id:guid}/lines/{lineId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveLine(Guid id, Guid lineId, CancellationToken ct)
    {
        var (ok, error, statusCode) = await _svc.RemoveLineAsync(id, lineId, ct);
        if (!ok) return StatusCode(statusCode!.Value, new { error });
        return NoContent();
    }

    [HttpPost("work-orders/{id:guid}/complete")]
    [ProducesResponseType(typeof(WorkOrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(object), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(object), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CompleteWorkOrder(Guid id, CancellationToken ct)
    {
        var tenantId  = ResolveTenantId();
        var userId    = ResolveUserId();
        if (tenantId is null || userId is null) return Forbid();

        var (order, error, statusCode) = await _svc.CompleteWorkOrderAsync(id, userId.Value, tenantId.Value, ct);
        if (error is not null) return StatusCode(statusCode!.Value, new { error });
        return Ok(order);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid? ResolveTenantId()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private Guid? ResolveUserId()
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}
