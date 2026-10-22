using ShelfGuard.Application.Features.Receipts.Dtos;

namespace ShelfGuard.Application.Features.Receipts;

/// <summary>
/// TASK-755: builds / refreshes automatic DRAFT supplier orders (stock_receipts, Source
/// auto_replenishment) for NON-own-production items. Runs inside the caller's RLS context
/// (manual endpoint: the staff JWT; daily cron: <see cref="IAutoReplenishmentTenantRunner"/>).
/// </summary>
public interface IAutoReplenishmentService
{
    /// <summary>
    /// Returns the created/changed drafts (cancelled ones included, status "cancelled").
    /// StatusCode 404 when <paramref name="storeLocationId"/> is not a visible active store.
    /// </summary>
    Task<(List<ReceiptDto>? Drafts, string? Error, int? StatusCode)> GenerateAsync(
        Guid tenantId, Guid? storeLocationId, CancellationToken ct = default);
}
