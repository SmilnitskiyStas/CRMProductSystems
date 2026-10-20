using ShelfGuard.Application.Features.Orders.Dtos;

namespace ShelfGuard.Application.Features.Orders;

public interface IOrderTrackingService
{
    Task<OrderTrackingPageDto> GetPagedAsync(
        Guid tenantId, string? source, IReadOnlyCollection<string>? statuses, Guid? storeId, string? search,
        int page, int pageSize, bool overdueOnly = false, CancellationToken ct = default);

    Task<OrderTrackingSummaryDto> GetSummaryAsync(Guid tenantId, Guid? storeId, CancellationToken ct = default);
}
