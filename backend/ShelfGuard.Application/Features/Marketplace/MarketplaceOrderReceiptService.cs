using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Client-confirmed receipt of a shipped marketplace order (TASK-586, ADR-033). Mirrors
/// <see cref="ReceiptService"/>'s create-draft / update-item / finalize shape, with the
/// deliberate deviations ADR-033 Decision 5 calls out: per-item update (not bulk), and the
/// finalize gate additionally requires ProductId (resolved at scan time, unlike Receipts where
/// ProductId is fixed at creation).
///
/// No supplier notification is enqueued on finalize — ADR-033's Consequences section explicitly
/// scopes that out of this design (the plan only asked for a read-only supplier-cabinet display
/// after Delivered, not a push/outbox event); this deliberately does NOT mirror
/// MarketplaceOrderService's Shipped/delay-reason ITenantSessionOverride notification pattern.
/// </summary>
public sealed class MarketplaceOrderReceiptService : IMarketplaceOrderReceiptService
{
    public const string OrderNotFoundError = "Замовлення не знайдено.";
    public const string OrderNotShippedError = "Прийом можливий лише для відправлених замовлень.";
    public const string DestinationStoreMissingError =
        "У замовлення не вказано магазин-призначення. Зверніться до підтримки.";
    public const string ReceiptNotFoundError = "Документ прийому не знайдено.";
    public const string ReceiptAlreadyReceivedError = "Документ прийому вже підтверджено.";
    public const string ReceiptItemNotFoundError = "Позицію документа прийому не знайдено.";
    public const string NegativeQuantityError = "Отримана кількість не може бути від'ємною.";
    public const string ProductNotFoundError = "Товар не знайдено у вашому каталозі.";
    public const string ReceiveGateError =
        "Усі позиції мають бути відскановані з кількістю та терміном придатності перед підтвердженням.";

    private readonly IMarketplaceOrderReceiptRepository _receipts;
    private readonly IMarketplaceOrderRepository _orders;
    private readonly IMarketplaceOrderService _orderService;
    private readonly IItemRepository _items;

    public MarketplaceOrderReceiptService(
        IMarketplaceOrderReceiptRepository receipts,
        IMarketplaceOrderRepository orders,
        IMarketplaceOrderService orderService,
        IItemRepository items)
    {
        _receipts = receipts;
        _orders = orders;
        _orderService = orderService;
        _items = items;
    }

    public Task<IReadOnlyList<MarketplaceOrderDto>> ListAwaitingReceiptAsync(
        Guid clientTenantId, CancellationToken ct = default) =>
        _orderService.ListAwaitingReceiptForClientAsync(clientTenantId, ct);

    public async Task<(MarketplaceOrderReceiptDto? Receipt, string? Error)> GetOrCreateDraftAsync(
        Guid clientTenantId, Guid orderId, Guid userId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.ClientTenantId != clientTenantId)
            return (null, OrderNotFoundError);

        // Idempotent: an already-started (or already-finished) receipt is just returned as-is —
        // resumes an interrupted receiving session, never errors on a repeat call.
        var existing = await _receipts.GetByOrderIdAsync(orderId, ct);
        if (existing is not null)
            return (ToDto(existing), null);

        if (order.Status != MarketplaceOrderStatus.Shipped)
            return (null, OrderNotShippedError);

        // ADR-033 Decision 2's historical-gap case: orders placed before DestinationStoreId
        // existed can never be received through this flow.
        if (order.DestinationStoreId is null)
            return (null, DestinationStoreMissingError);

        var receipt = new MarketplaceOrderReceipt
        {
            MarketplaceOrderId = order.Id,
            ClientTenantId = clientTenantId,
            SupplierTenantId = order.SupplierTenantId,
            DestinationStoreId = order.DestinationStoreId.Value,
            Status = "draft",
            CreatedByUserId = userId,
        };

        foreach (var item in order.Items)
        {
            receipt.Items.Add(new MarketplaceOrderReceiptItem
            {
                ReceiptId = receipt.Id,
                MarketplaceOrderItemId = item.Id,
                ClientTenantId = clientTenantId,
                SupplierTenantId = order.SupplierTenantId,
                ItemNameSnapshot = item.ItemName,
                QuantityOrdered = item.Qty,
            });
        }

        await _receipts.AddAsync(receipt, ct);
        await _receipts.SaveChangesAsync(ct);

        var saved = await _receipts.GetByIdAsync(receipt.Id, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(MarketplaceOrderReceiptDto? Receipt, string? Error)> GetAsync(
        Guid clientTenantId, Guid orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.ClientTenantId != clientTenantId)
            return (null, OrderNotFoundError);

        var receipt = await _receipts.GetByOrderIdAsync(orderId, ct);
        return receipt is null ? (null, ReceiptNotFoundError) : (ToDto(receipt), null);
    }

    public async Task<(MarketplaceOrderReceiptDto? Receipt, string? Error)> UpdateItemAsync(
        Guid clientTenantId, Guid orderId, Guid itemId, UpdateMarketplaceOrderReceiptItemRequest request,
        CancellationToken ct = default)
    {
        var receipt = await _receipts.GetByOrderIdAsync(orderId, ct);
        if (receipt is null || receipt.ClientTenantId != clientTenantId)
            return (null, ReceiptNotFoundError);

        if (receipt.Status != "draft")
            return (null, ReceiptAlreadyReceivedError);

        var item = receipt.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            return (null, ReceiptItemNotFoundError);

        if (request.QuantityReceived is < 0)
            return (null, NegativeQuantityError);

        if (request.ProductId.HasValue)
        {
            // Defence in depth: GetByIdAsync is RLS-scoped to the ambient (client) session, so a
            // ProductId belonging to another tenant can never resolve here even if guessed.
            var product = await _items.GetByIdAsync(request.ProductId.Value, ct);
            if (product is null)
                return (null, ProductNotFoundError);
        }

        // Field semantics mirror ReceiptService.UpdateItemsAsync exactly: QuantityReceived and
        // DiscrepancyNotes overwrite directly (an omitted field clears it — the caller is
        // expected to resend the full known value each call), ProductId/ExpiryDate/BatchNumber
        // merge with the existing value when omitted (send null to leave alone, not to clear).
        item.ProductId = request.ProductId ?? item.ProductId;
        item.QuantityReceived = request.QuantityReceived;
        item.ExpiryDate = request.ExpiryDate ?? item.ExpiryDate;
        item.BatchNumber = request.BatchNumber ?? item.BatchNumber;
        item.DiscrepancyNotes = request.DiscrepancyNotes;

        receipt.UpdatedAt = DateTimeOffset.UtcNow;

        _receipts.UpdateItem(item);
        _receipts.Update(receipt);
        await _receipts.SaveChangesAsync(ct);

        var saved = await _receipts.GetByOrderIdAsync(orderId, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    public async Task<(MarketplaceOrderReceiptDto? Receipt, string? Error)> ReceiveAsync(
        Guid clientTenantId, Guid orderId, Guid receivedByUserId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.ClientTenantId != clientTenantId)
            return (null, OrderNotFoundError);

        var receipt = await _receipts.GetByOrderIdAsync(orderId, ct);
        if (receipt is null || receipt.ClientTenantId != clientTenantId)
            return (null, ReceiptNotFoundError);

        if (receipt.Status == "received")
            return (null, ReceiptAlreadyReceivedError);

        // Gate: every item needs ProductId + QuantityReceived + ExpiryDate — extends
        // ReceiptService.ReceiveAsync's expiry-only gate with the not-yet-scanned case.
        var unresolved = receipt.Items
            .Where(i => i.ProductId is null || i.QuantityReceived is null || i.ExpiryDate is null)
            .ToList();
        if (unresolved.Count > 0)
            return (null, ReceiveGateError);

        foreach (var item in receipt.Items)
        {
            var qty = item.QuantityReceived!.Value;
            if (qty <= 0) continue;

            var stock = new ProductStock
            {
                TenantId = clientTenantId,
                ProductId = item.ProductId!.Value,
                StoreId = receipt.DestinationStoreId,
                BatchNumber = item.BatchNumber,
                Quantity = qty,
                QuantityInitial = qty,
                ExpiryDate = item.ExpiryDate!.Value,
                Status = StockStatus.Compute(qty, item.ExpiryDate!.Value, DateTime.UtcNow),
                // ADR-033 Decision 5's endpoint (e) table — SourceId points at the receipt row
                // (not the order), same "reference the thing that actually did the writing"
                // convention ReceiptService uses (SourceId = receipt.Id, not supplier.Id).
                SourceType = "marketplace_order_receipt",
                SourceId = receipt.Id,
                AddedBy = receivedByUserId,
            };
            await _receipts.AddStockAsync(stock, ct);

            // MovementType stays "receipt" (not a new "marketplace_order" value) — same
            // real-world event (goods physically added to stock) as a regular supplier
            // delivery; ReferenceType/ReferenceId distinguish the marketplace origin for anyone
            // tracing the audit trail back to its source document.
            var unitPrice = item.OrderItem?.Price;
            var movement = new StockMovement
            {
                TenantId = clientTenantId,
                MovementType = "receipt",
                ProductStockId = stock.Id,
                ProductId = item.ProductId!.Value,
                ToStoreId = receipt.DestinationStoreId,
                Quantity = qty,
                QuantityBefore = 0,
                QuantityAfter = qty,
                UnitPrice = unitPrice,
                TotalAmount = unitPrice.HasValue ? unitPrice.Value * qty : null,
                ReferenceId = receipt.Id,
                ReferenceType = "marketplace_order_receipt",
                PerformedBy = receivedByUserId,
            };
            await _receipts.AddMovementAsync(movement, ct);
        }

        receipt.Status = "received";
        receipt.ReceivedAt = DateTimeOffset.UtcNow;
        receipt.ReceivedByUserId = receivedByUserId;
        receipt.UpdatedAt = DateTimeOffset.UtcNow;
        _receipts.Update(receipt);

        // The only code path in the whole codebase that may set Delivered (ADR-033 Decision 4).
        // No ITenantSessionOverride needed — the client session already has native RLS write
        // access to marketplace_orders (its tenant_isolation policy is OR-based on both tenants).
        order.Status = MarketplaceOrderStatus.Delivered;
        order.DeliveredAt = DateTimeOffset.UtcNow;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        _orders.Update(order);

        // One shared AppDbContext behind both repositories (both scoped, resolved within the
        // same request) — a single SaveChangesAsync call flushes the receipt/item/stock/movement
        // inserts and the order's status change together, atomically.
        await _receipts.SaveChangesAsync(ct);

        var saved = await _receipts.GetByOrderIdAsync(orderId, ct);
        return (saved is null ? null : ToDto(saved), null);
    }

    // ── mapping ────────────────────────────────────────────────────────────────

    private static MarketplaceOrderReceiptDto ToDto(MarketplaceOrderReceipt r) => new(
        r.Id,
        r.MarketplaceOrderId,
        r.ClientTenantId,
        r.SupplierTenantId,
        r.DestinationStoreId,
        r.DestinationStore?.Name ?? "—",
        r.Status,
        r.CreatedByUserId,
        r.ReceivedByUserId,
        r.ReceivedAt,
        r.CreatedAt,
        r.UpdatedAt,
        r.Items.Select(ToItemDto).ToList());

    private static MarketplaceOrderReceiptItemDto ToItemDto(MarketplaceOrderReceiptItem i) => new(
        i.Id,
        i.MarketplaceOrderItemId,
        i.ProductId,
        i.ItemNameSnapshot,
        i.Product?.Name,
        i.QuantityOrdered,
        i.QuantityReceived,
        i.ExpiryDate,
        i.BatchNumber,
        i.DiscrepancyNotes,
        IsResolved: i.ProductId.HasValue && i.QuantityReceived.HasValue && i.ExpiryDate.HasValue);
}
