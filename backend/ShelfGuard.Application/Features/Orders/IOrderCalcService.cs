using ShelfGuard.Application.Features.Orders.Dtos;

namespace ShelfGuard.Application.Features.Orders;

public interface IOrderCalcService
{
    /// <summary>
    /// Computes order quantities for every buffered product in the store (v2-spec §3):
    /// Order = Buffer + SafetyBuffer − StockOnHand − InTransit, then MOQ/USQ rounding.
    /// Stateless — persistence arrives with AI suggestions (phase 5).
    /// </summary>
    /// <param name="excludeAutoUntakenDrafts">
    /// TASK-755: ignore untaken auto-replenishment drafts when computing "in transit" (only the daily
    /// auto-replenishment job passes true — it rewrites those drafts). Default false.
    /// </param>
    Task<(OrderCalcResult? Result, string? Error)> CalculateAsync(
        Guid storeId, CancellationToken ct = default, bool excludeAutoUntakenDrafts = false);
}
