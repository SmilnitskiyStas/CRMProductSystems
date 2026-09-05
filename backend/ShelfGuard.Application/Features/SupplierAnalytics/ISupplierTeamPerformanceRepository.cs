namespace ShelfGuard.Application.Features.SupplierAnalytics;

/// <summary>
/// Data access for supplier team performance (TASK-695, Phase 8). Every query runs on the
/// supplier's own RLS session:
///   • <c>marketplace_orders</c> — OR-based <c>tenant_isolation</c>, plus an explicit
///     <c>SupplierTenantId</c> predicate (defence-in-depth);
///   • <c>marketplace_order_receipts</c> / <c>_items</c> — the supplier's <c>supplier_read</c>
///     (ADR-033);
///   • <c>supplier_chat_sessions</c> / <c>_messages</c> — OR-based <c>tenant_isolation</c>;
///   • <c>supplier_employee_reviews</c> — the supplier's <c>supplier_read</c> (TASK-695).
/// Marketplace volume is low (B2B), so the service does the roll-up in memory over these rows.
/// </summary>
public interface ISupplierTeamPerformanceRepository
{
    /// <summary>
    /// Non-cancelled orders of <paramref name="supplierTenantId"/> touched at or after
    /// <paramref name="since"/> on any of <c>CreatedAt</c> / <c>ConfirmedAt</c> / <c>ShippedAt</c>
    /// / <c>DeliveredAt</c> — the union that can possibly land in the current or preceding window.
    /// </summary>
    Task<IReadOnlyList<TeamPerfOrderRow>> GetOrdersSinceAsync(
        Guid supplierTenantId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>
    /// One row per <paramref name="orderIds"/> entry that has a FINALIZED
    /// (<c>Status == "received"</c>) receipt, with a flag for whether any receipt item carries a
    /// discrepancy note. Orders with no finalized receipt are simply absent from the result.
    /// </summary>
    Task<IReadOnlyList<TeamPerfReceiptRow>> GetFinalizedReceiptFlagsAsync(
        Guid supplierTenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken ct = default);

    /// <summary>Chat messages in <paramref name="supplierTenantId"/>'s threads sent at or after <paramref name="since"/>.</summary>
    Task<IReadOnlyList<TeamPerfChatMessageRow>> GetChatMessagesSinceAsync(
        Guid supplierTenantId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Buyer→employee ratings of <paramref name="supplierTenantId"/>'s staff created at or after <paramref name="since"/>.</summary>
    Task<IReadOnlyList<TeamPerfReviewRow>> GetEmployeeReviewsSinceAsync(
        Guid supplierTenantId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Every buyer→employee rating of one staff member, newest first (the "read the feedback" view).</summary>
    Task<IReadOnlyList<TeamPerfReviewDetailRow>> GetEmployeeReviewDetailsAsync(
        Guid supplierTenantId, Guid supplierUserId, CancellationToken ct = default);
}

/// <summary>Projected order row for team-performance aggregation.</summary>
public sealed record TeamPerfOrderRow(
    Guid OrderId,
    Guid? ConfirmedByUserId,
    Guid? ShippedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateOnly? ExpectedDeliveryDate);

/// <summary>Projected finalized-receipt row: the order, and whether any item had a discrepancy note.</summary>
public sealed record TeamPerfReceiptRow(Guid OrderId, bool HasDiscrepancy);

/// <summary>Projected chat message row.</summary>
public sealed record TeamPerfChatMessageRow(
    Guid SessionId, Guid SenderUserId, Guid SenderTenantId, DateTimeOffset CreatedAt);

/// <summary>Projected buyer→employee rating row for the windowed rollup.</summary>
public sealed record TeamPerfReviewRow(Guid SupplierUserId, short Rating, DateTimeOffset CreatedAt);

/// <summary>Full buyer→employee rating row for the per-employee feedback view.</summary>
public sealed record TeamPerfReviewDetailRow(
    Guid Id,
    Guid SupplierUserId,
    string SupplierUserName,
    short Rating,
    string? Comment,
    string Source,
    Guid? OrderId,
    Guid? ChatSessionId,
    string? RatedByName,
    DateTimeOffset CreatedAt);
