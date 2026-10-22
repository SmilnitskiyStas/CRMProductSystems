using ShelfGuard.Application.Features.Production.Dtos;

namespace ShelfGuard.Application.Features.Production;

/// <summary>
/// Store -> production requests: store creates/submits, production reviews, aggregates into
/// production orders and ships via a StockTransfer. Tuple results: (value, error, statusCode).
/// </summary>
public interface IProductionRequestService
{
    Task<List<ProductionRequestListItemDto>> ListAsync(
        Guid? storeLocationId, Guid? productionLocationId, string? status,
        DateOnly? neededFrom, DateOnly? neededTo, CancellationToken ct = default);

    Task<ProductionRequestDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> CreateAsync(
        ProductionRequestCreateDto dto, Guid tenantId, Guid userId, CancellationToken ct = default);

    Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> UpdateAsync(
        Guid id, ProductionRequestUpdateDto dto, CancellationToken ct = default);

    /// <summary>draft -> submitted; enqueues a notification for the production location.</summary>
    Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> SubmitAsync(
        Guid id, CancellationToken ct = default);

    /// <summary>draft/submitted -> cancelled (store side).</summary>
    Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> CancelAsync(
        Guid id, CancellationToken ct = default);

    /// <summary>submitted -> accepted / partial / rejected, with per-line accepted quantities.</summary>
    Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> ReviewAsync(
        Guid id, ProductionRequestReviewDto dto, Guid userId, CancellationToken ct = default);

    Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> RejectAsync(
        Guid id, string reason, Guid userId, CancellationToken ct = default);

    Task<List<ProductionRequestSummaryItemDto>> GetSummaryAsync(
        Guid? productionLocationId, string? status, CancellationToken ct = default);

    Task<(List<ProductionOrderCreatedDto>? Orders, string? Error, int? StatusCode)> CreateOrdersFromRequestsAsync(
        ProductionOrdersFromRequestsDto dto, Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// accepted/partial -> fulfilled: FEFO StockTransfer production -> store for the accepted
    /// quantities (expiry/batch copied as-is by the transfer), linked to the request.
    /// </summary>
    Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> ShipAsync(
        Guid id, Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Explicit re-sync of the request's production orders with its accepted quantities (idempotent).</summary>
    Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> SyncOrdersAsync(
        Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// For a Done order: ships every linked accepted/partial request whose linked orders are all Done.
    /// One request failing never aborts the batch (per-request result).
    /// </summary>
    Task<(List<ProductionShipRequestResultDto>? Results, string? Error, int? StatusCode)> ShipOrderRequestsAsync(
        Guid orderId, Guid tenantId, Guid userId, CancellationToken ct = default);
}
