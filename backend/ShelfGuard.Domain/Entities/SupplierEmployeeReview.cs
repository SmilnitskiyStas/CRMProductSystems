namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A buyer (client tenant) rating one supplier-side employee (TASK-695, Phase 8). Two entry
/// points, distinguished by <see cref="Source"/>:
///   • <c>"order"</c> — after a delivered <see cref="MarketplaceOrder"/>, the buyer rates the
///     responsible manager (<see cref="MarketplaceOrder.ConfirmedByUserId"/>). One rating per
///     (employee, buyer, order).
///   • <c>"chat"</c> — from a <see cref="SupplierChatSession"/> thread, the buyer rates a supplier
///     staff member who replied in it. One rating per (employee, buyer, session).
///
/// Supplier-internal only: NOT shown on the public supplier profile and NOT rolled into
/// <see cref="SupplierMetrics"/>.Rating (that stays company-level, from <see cref="SupplierReview"/>).
///
/// Denormalised: <see cref="ClientTenantId"/>/<see cref="SupplierTenantId"/> are the tenant pair
/// (same convention as <see cref="MarketplaceOrderItem"/>); <see cref="SupplierUserName"/> and
/// <see cref="RatedByName"/> are name snapshots — the buyer cannot join the supplier's
/// <c>users</c> table across the tenant boundary, and the supplier cannot join the buyer's.
///
/// RLS is the ADR-033 split: the buyer writes (<c>tenant_isolation</c> on <see cref="ClientTenantId"/>),
/// the supplier reads (<c>supplier_read</c> FOR SELECT on <see cref="SupplierTenantId"/>).
/// </summary>
public sealed class SupplierEmployeeReview
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid SupplierTenantId { get; set; }
    public Guid ClientTenantId { get; set; }

    /// <summary>The rated employee — a <c>users</c> id in the supplier tenant.</summary>
    public Guid SupplierUserId { get; set; }
    /// <summary>Display-name snapshot of the rated employee, taken at rating time.</summary>
    public string SupplierUserName { get; set; } = string.Empty;

    /// <summary>Buyer-side user who left the rating.</summary>
    public Guid RatedByUserId { get; set; }
    /// <summary>Display-name snapshot of the buyer-side rater.</summary>
    public string? RatedByName { get; set; }

    /// <summary>1–5.</summary>
    public short Rating { get; set; }
    public string? Comment { get; set; }

    /// <summary><c>"order"</c> or <c>"chat"</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Set when <see cref="Source"/> = <c>"order"</c>. FK → <c>marketplace_orders</c>, SET NULL.</summary>
    public Guid? OrderId { get; set; }
    /// <summary>Set when <see cref="Source"/> = <c>"chat"</c>. FK → <c>supplier_chat_sessions</c>, SET NULL.</summary>
    public Guid? ChatSessionId { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
