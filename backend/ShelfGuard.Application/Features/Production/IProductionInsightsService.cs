using ShelfGuard.Application.Features.Production.Dtos;

namespace ShelfGuard.Application.Features.Production;

/// <summary>Read-only aggregates for the production overview / plan / shipments / warehouse pages (TASK-763).</summary>
public interface IProductionInsightsService
{
    Task<ProductionOverviewDto> GetOverviewAsync(Guid? locationId, CancellationToken ct = default);

    Task<ProductionPlanDto> GetPlanAsync(
        DateOnly? from, DateOnly? to, string? granularity, Guid? locationId, CancellationToken ct = default);

    /// <summary>Accepted/partial requests whose derived stage is "ready" (all orders done).</summary>
    Task<List<ReadyShipmentDto>> GetReadyShipmentsAsync(Guid? locationId, CancellationToken ct = default);

    /// <summary>Fulfilled requests shipped in [from, to] (default: last 30 days).</summary>
    Task<List<ShippedShipmentDto>> GetShippedShipmentsAsync(
        DateOnly? from, DateOnly? to, Guid? locationId, CancellationToken ct = default);

    /// <summary>TASK-765: 4-stage pipeline (stages 1-3 current, stage 4 + stats for the period). Throws ArgumentException on bad range.</summary>
    Task<ProductionPipelineDto> GetPipelineAsync(
        DateOnly? from, DateOnly? to, Guid? locationId, CancellationToken ct = default);

    Task<List<ProductionStockItemDto>> GetStockAsync(Guid? locationId, string? kind, CancellationToken ct = default);
}
