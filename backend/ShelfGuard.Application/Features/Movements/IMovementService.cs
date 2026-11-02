using ShelfGuard.Application.Features.Movements.Dtos;

namespace ShelfGuard.Application.Features.Movements;

public interface IMovementService
{
    Task<StockHistoryDto> GetStockHistoryAsync(
        Guid tenantId, Guid productId, Guid? storeId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Open (not yet received) supply for a product, soonest expected first.</summary>
    Task<IReadOnlyList<IncomingSupplyDto>> GetIncomingAsync(
        Guid tenantId, Guid productId, Guid? storeId, CancellationToken ct = default);

    Task<MovementPageDto> GetAsync(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
