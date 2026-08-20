using ShelfGuard.Application.Features.Marketplace.Dtos;

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

    Task<IReadOnlyList<MarketplaceOrderDto>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default);

    /// <summary>Client may cancel only orders still in status "new".</summary>
    Task<(MarketplaceOrderDto? Order, string? Error)> CancelOrderAsync(
        Guid clientTenantId, Guid orderId, string reason, CancellationToken ct = default);

    // ── Supplier side ─────────────────────────────────────────────────────────

    Task<IReadOnlyList<MarketplaceOrderDto>> ListForSupplierAsync(
        Guid supplierTenantId, CancellationToken ct = default);

    /// <summary>
    /// Allowed transitions: new → confirmed | cancelled; confirmed → shipped | cancelled;
    /// shipped → delivered. Cancelling requires a reason.
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
}
