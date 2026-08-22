using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// B2B marketplace orders (TASK-317). Placing an order is gated by an
/// ACTIVE cooperation agreement between the client and the supplier —
/// the gate violation maps to HTTP 403 in the controller.
/// </summary>
public interface IMarketplaceOrderService
{
    // ── Client side ───────────────────────────────────────────────────────────

    /// <summary>IsGateViolation = true → controller returns 403 (no active agreement).</summary>
    Task<(MarketplaceOrderDto? Order, string? Error, bool IsGateViolation)> CreateOrderAsync(
        Guid clientTenantId, Guid supplierId, CreateMarketplaceOrderDto request, Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Read-only pre-flight for the checkout screen (TASK-597): runs the same per-line supplier
    /// catalog validation as <see cref="CreateOrderAsync"/> plus a barcode-collision check against
    /// the calling client tenant's own Item catalog. Creates nothing. Same gate as CreateOrderAsync
    /// (IsGateViolation = true → 403) since it previews exactly what a real order would need to
    /// pass. Empty conflicts list means the items are safe to submit as-is (CatalogAction can stay
    /// null/"auto" on every line).
    /// </summary>
    Task<(IReadOnlyList<MarketplaceOrderConflictDto>? Conflicts, string? Error, bool IsGateViolation)> CheckCatalogConflictsAsync(
        Guid clientTenantId, Guid supplierId, IReadOnlyList<CreateMarketplaceOrderItemDto> items,
        CancellationToken ct = default);

    Task<IReadOnlyList<MarketplaceOrderDto>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default);

    /// <summary>Client may cancel only orders still in status "new".</summary>
    Task<(MarketplaceOrderDto? Order, string? Error)> CancelOrderAsync(
        Guid clientTenantId, Guid orderId, string reason, CancellationToken ct = default);

    // ── Supplier side ─────────────────────────────────────────────────────────

    Task<IReadOnlyList<MarketplaceOrderDto>> ListForSupplierAsync(
        Guid supplierTenantId, CancellationToken ct = default);

    /// <summary>
    /// Allowed transitions: new → confirmed | cancelled; confirmed → shipped | cancelled. No
    /// supplier-initiated transition exists out of shipped any more (TASK-586, ADR-033 Decision
    /// 4) — delivered is now set exclusively by <see cref="MarketplaceOrderReceiptService"/>'s
    /// client-confirmed receiving flow; a status update of "delivered" always 400s here.
    /// Cancelling requires a reason.
    /// </summary>
    Task<(MarketplaceOrderDto? Order, string? Error)> UpdateOrderStatusAsync(
        Guid supplierTenantId, Guid orderId, UpdateMarketplaceOrderStatusDto request,
        CancellationToken ct = default);

    /// <summary>
    /// Records why a shipped order's delivery is running late (TASK-585). Only allowed
    /// while the order is still status = shipped; notifies the client tenant.
    /// </summary>
    Task<(MarketplaceOrderDto? Order, string? Error)> SetDelayReasonAsync(
        Guid supplierTenantId, Guid orderId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Shipped orders of the calling client tenant that still need to be received (TASK-586) —
    /// no <see cref="MarketplaceOrderReceipt"/> yet, or one still in "draft". Used by
    /// <see cref="MarketplaceOrderReceiptService"/>.
    /// </summary>
    Task<IReadOnlyList<MarketplaceOrderDto>> ListAwaitingReceiptForClientAsync(
        Guid clientTenantId, CancellationToken ct = default);
}
