using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Api.Controllers;

/// <summary>
/// Production block read models: overview, plan/MRP, shipments, warehouse (TASK-763).
/// Same access as ProductionController (authenticated + "production" module). Thin: delegates to IProductionInsightsService.
/// </summary>
[ApiController]
[Route("api/production")]
[Authorize]
[RequireModule("production")]
public sealed class ProductionInsightsController : ControllerBase
{
    private readonly IProductionInsightsService _svc;

    public ProductionInsightsController(IProductionInsightsService svc) => _svc = svc;

    [HttpGet("overview")]
    [ProducesResponseType(typeof(ProductionOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview([FromQuery] Guid? locationId, CancellationToken ct) =>
        Ok(await _svc.GetOverviewAsync(locationId, ct));

    /// <param name="granularity">day | week | month (default day).</param>
    [HttpGet("plan")]
    [ProducesResponseType(typeof(ProductionPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Plan(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? granularity,
        [FromQuery] Guid? locationId, CancellationToken ct) =>
        Ok(await _svc.GetPlanAsync(from, to, granularity, locationId, ct));

    /// <param name="status">ready (default) | shipped.</param>
    [HttpGet("shipments")]
    [ProducesResponseType(typeof(List<ReadyShipmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(List<ShippedShipmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Shipments(
        [FromQuery] string? status, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? locationId, CancellationToken ct)
    {
        if (string.Equals(status, "shipped", StringComparison.OrdinalIgnoreCase))
            return Ok(await _svc.GetShippedShipmentsAsync(from, to, locationId, ct));
        if (string.IsNullOrWhiteSpace(status) || string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase))
            return Ok(await _svc.GetReadyShipmentsAsync(locationId, ct));
        return BadRequest(new { error = "status must be 'ready' or 'shipped'." });
    }

    /// <summary>4-stage pipeline: new → in work → ready (current) → shipped (period). from/to inclusive, default today.</summary>
    [HttpGet("pipeline")]
    [ProducesResponseType(typeof(ProductionPipelineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Pipeline(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? locationId, CancellationToken ct)
    {
        try { return Ok(await _svc.GetPipelineAsync(from, to, locationId, ct)); }
        catch (ArgumentException e) { return BadRequest(new { error = e.Message }); }
    }

    /// <param name="kind">all (default) | raw | finished.</param>
    [HttpGet("stock")]
    [ProducesResponseType(typeof(List<ProductionStockItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Stock(
        [FromQuery] Guid? locationId, [FromQuery] string? kind, CancellationToken ct) =>
        Ok(await _svc.GetStockAsync(locationId, kind, ct));
}
