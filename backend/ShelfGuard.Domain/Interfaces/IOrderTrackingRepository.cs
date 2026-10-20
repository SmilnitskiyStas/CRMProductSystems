namespace ShelfGuard.Domain.Interfaces;

/// <summary>One line of a tracked order (what was ordered / how much arrived).</summary>
public sealed record TrackedOrderLineRow(
    string Name, decimal Quantity, decimal? QuantityReceived, string? Unit, decimal? Amount);

/// <summary>
/// A purchase order the buyer placed, from either source: an internal supplier receipt document
/// ("receipt") or a B2B marketplace order ("marketplace"). <c>Status</c> is already mapped to the
/// unified vocabulary: new | confirmed | in_transit | delivered | cancelled.
/// </summary>
public sealed record TrackedOrderRow(
    string Source,
    Guid Id,
    string? Number,
    string Status,
    string? Counterparty,
    Guid? StoreId,
    string? StoreName,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime? ConfirmedAt,
    string? ConfirmedBy,
    DateTime? ShippedAt,
    string? ShippedBy,
    DateOnly? ExpectedAt,
    DateTime? ClosedAt,
    string? ClosedBy,
    decimal TotalQuantity,
    decimal TotalAmount,
    string? Note,
    IReadOnlyList<TrackedOrderLineRow> Lines,
    int PostponeCount = 0,
    DateOnly? OriginalExpectedAt = null,
    DateTime? PostponedAt = null);

public sealed record OrderTrackingSummaryRow(
    int New, int Confirmed, int InTransit, int Delivered, int Cancelled, int Overdue,
    decimal OpenQuantity, decimal InTransitQuantity, decimal OverdueQuantity, int Postponed = 0);

public interface IOrderTrackingRepository
{
    /// <summary>
    /// One page of the tenant's purchase orders from both sources, newest first. <paramref name="statuses"/>
    /// uses the unified vocabulary; null/empty = all. <paramref name="source"/> is "receipt" | "marketplace" | null.
    /// </summary>
    Task<(IReadOnlyList<TrackedOrderRow> Items, int Total)> GetPagedAsync(
        Guid tenantId, string? source, IReadOnlyCollection<string>? statuses, Guid? storeId, string? search,
        int page, int pageSize, bool overdueOnly = false, DateOnly? today = null, CancellationToken ct = default);

    Task<OrderTrackingSummaryRow> GetSummaryAsync(
        Guid tenantId, Guid? storeId, DateOnly today, CancellationToken ct = default);
}
