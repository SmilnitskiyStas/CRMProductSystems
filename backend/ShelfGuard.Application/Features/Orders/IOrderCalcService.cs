using ShelfGuard.Application.Features.Orders.Dtos;

namespace ShelfGuard.Application.Features.Orders;

public interface IOrderCalcService
{
    /// <summary>
    /// Computes order quantities for every buffered product in the store (v2-spec §3):
    /// Order = Buffer + SafetyBuffer − StockOnHand − InTransit, then MOQ/USQ rounding.
    /// Stateless — persistence arrives with AI suggestions (phase 5).
    /// </summary>
    Task<(OrderCalcResult? Result, string? Error)> CalculateAsync(
        Guid storeId, CancellationToken ct = default);
}
