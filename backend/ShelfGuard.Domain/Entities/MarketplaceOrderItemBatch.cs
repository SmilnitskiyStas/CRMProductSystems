namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Supplier-portal expansion — Phase 3 (plan `1-partitioned-book.md`, decision D4).
/// One batch the SUPPLIER allocated to a <see cref="MarketplaceOrderItem"/> when shipping.
///
/// Two jobs in one row, deliberately:
/// <list type="number">
/// <item>the supplier-side <b>stock-consumption ledger entry</b> — which
/// <see cref="SupplierStock"/> batch (expiry + batch number) actually left the warehouse for
/// this order line;</item>
/// <item>the <b>hand-off record</b> the CLIENT reads to prefill its own receiving draft —
/// <see cref="MarketplaceOrderReceiptService"/> turns N batches on one order line into N
/// prefilled <see cref="MarketplaceOrderReceiptItem"/> sub-rows (ADR-033 amendment).</item>
/// </list>
///
/// RLS is the <b>mirror image</b> of ADR-033's receipt split: here the SUPPLIER writes
/// (<c>tenant_isolation</c> on <see cref="SupplierTenantId"/>, FOR ALL + WITH CHECK) and the
/// CLIENT only reads (<c>client_read</c>, FOR SELECT on <see cref="ClientTenantId"/>). This
/// inverts every other marketplace table's direction, so both tenant ids are denormalized onto
/// the row — same convention <see cref="MarketplaceOrderItem"/> established — and no client
/// session may ever write here.
/// </summary>
public sealed class MarketplaceOrderItemBatch
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The order line this allocation covers. CASCADE with the line.</summary>
    public Guid OrderItemId { get; set; }

    /// <summary>Denormalized parent order id — lets the client prefill query filter by order without a join.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Writer side of the split RLS (the supplier that picked and shipped this batch).</summary>
    public Guid SupplierTenantId { get; set; }

    /// <summary>Reader side of the split RLS (the client that will receive it).</summary>
    public Guid ClientTenantId { get; set; }

    /// <summary>
    /// The consumed <see cref="SupplierStock"/> batch. Nullable + SET NULL: a batch row may be
    /// purged/archived later, but the shipped-history record must survive.
    /// </summary>
    public Guid? SupplierStockId { get; set; }

    /// <summary>Snapshot of the consumed batch's expiry — never rewritten (FEFO handoff contract).</summary>
    public DateOnly ExpiryDate { get; set; }

    /// <summary>Snapshot of the consumed batch's number; null when the supplier tracks none.</summary>
    public string? BatchNumber { get; set; }

    /// <summary>Quantity of this batch allocated to the order line.</summary>
    public decimal Qty { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public MarketplaceOrderItem? OrderItem { get; init; }
}
