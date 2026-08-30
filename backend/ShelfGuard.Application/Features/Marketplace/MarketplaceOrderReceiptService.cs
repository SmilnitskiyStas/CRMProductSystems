using System.Text.Json;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Services;
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
/// TASK-599, Wave 2: <see cref="ReceiveAsync"/> now DOES enqueue a cross-tenant side effect —
/// auto-opening a <see cref="SupplierSupportTicket"/> (+ notification) when any item was flagged
/// with DiscrepancyNotes during scanning. This updates the ADR-033 Consequences note above (that
/// note described the v1/TASK-586 scope, not the current one).
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
    private readonly IMarketplaceRepository _marketplace;
    private readonly ISupplierSupportService _supplierSupport;
    private readonly INotificationRepository _notifications;
    private readonly ITenantSessionOverride _tenantSessionOverride;

    public MarketplaceOrderReceiptService(
        IMarketplaceOrderReceiptRepository receipts,
        IMarketplaceOrderRepository orders,
        IMarketplaceOrderService orderService,
        IItemRepository items,
        IMarketplaceRepository marketplace,
        ISupplierSupportService supplierSupport,
        INotificationRepository notifications,
        ITenantSessionOverride tenantSessionOverride)
    {
        _receipts = receipts;
        _orders = orders;
        _orderService = orderService;
        _items = items;
        _marketplace = marketplace;
        _supplierSupport = supplierSupport;
        _notifications = notifications;
        _tenantSessionOverride = tenantSessionOverride;
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
            return (await ToDtoAsync(existing, ct), null);

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
        return (saved is null ? null : await ToDtoAsync(saved, ct), null);
    }

    public async Task<(MarketplaceOrderReceiptDto? Receipt, string? Error)> GetAsync(
        Guid clientTenantId, Guid orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.ClientTenantId != clientTenantId)
            return (null, OrderNotFoundError);

        var receipt = await _receipts.GetByOrderIdAsync(orderId, ct);
        return receipt is null ? (null, ReceiptNotFoundError) : (await ToDtoAsync(receipt, ct), null);
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
            // Existence check only. TASK-643/KI-036 correction: the comment that used to sit here
            // claimed "GetByIdAsync is RLS-scoped to the ambient (client) session, so a foreign
            // tenant's ProductId can never resolve" — that invariant is FALSE as a general rule.
            // IItemRepository carries no app-level TenantId filter by convention, so it holds only
            // while no provider/worker bypass role is active on the connection, which is a
            // property of call ordering, not of this method. It happens to hold on this request
            // today (no marketplace bypass method runs before this point — verified TASK-641 §6
            // F6), but the ownership guarantee below is the receipt's own ClientTenantId check,
            // not RLS. If a supplier-side lookup is ever hoisted above this line, add an explicit
            // `product.TenantId != receipt.ClientTenantId` check here — the same fix
            // MarketplaceOrderService.PlanCatalogOutcomeAsync needed.
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
        return (saved is null ? null : await ToDtoAsync(saved, ct), null);
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

        // TASK-599, Wave 2: auto-open a supplier support ticket when any item was flagged with a
        // discrepancy note during scanning. Deliberately a SECOND SaveChangesAsync call, not
        // folded into the one above, despite the original brief asking for a single atomic write
        // covering finalize + ticket + notification: product_stocks/stock_movements (just written
        // above) carry TenantId = clientTenantId under the plain single-tenant tenant_isolation
        // RLS policy those tables use — they can only be written while the ambient session is the
        // CLIENT tenant. The notification_queue row below must land with TenantId =
        // receipt.SupplierTenantId under the SAME plain single-tenant policy, which requires the
        // opposite: the ambient session overridden to the SUPPLIER tenant
        // (ITenantSessionOverride). No single ambient tenant satisfies both at once, so the two
        // phases cannot share one SaveChangesAsync call without breaking RLS on one side. Splitting
        // into two calls (this one already durably committed, the discrepancy side effect right
        // after) is the closest safe equivalent: finalize is never blocked by a downstream
        // notification hiccup, at the cost of not being a single DB transaction end-to-end.
        var discrepantItems = receipt.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.DiscrepancyNotes))
            .ToList();

        if (discrepantItems.Count > 0)
        {
            await _tenantSessionOverride.ExecuteAsync(receipt.SupplierTenantId, async () =>
            {
                var subject = $"Розбіжності при прийомці замовлення {order.OrderNumber}";
                var body = BuildDiscrepancyTicketBody(discrepantItems);

                var ticket = await _supplierSupport.CreateSystemTicketAsync(
                    clientTenantId, receipt.SupplierTenantId, order.Id, subject, body, receivedByUserId, ct);

                await EnqueueDiscrepancyTicketNotificationAsync(receipt, order, ticket, ct);
                await _receipts.SaveChangesAsync(ct);
                return true;
            }, ct);
        }

        var saved = await _receipts.GetByOrderIdAsync(orderId, ct);
        return (saved is null ? null : await ToDtoAsync(saved, ct), null);
    }

    // ── Discrepancy-ticket helpers (TASK-599, Wave 2) ───────────────────────────

    private static string BuildDiscrepancyTicketBody(IReadOnlyList<MarketplaceOrderReceiptItem> items)
    {
        var body = string.Join("\n", items.Select(i =>
            $"{i.Product?.Name ?? i.ItemNameSnapshot}: {i.DiscrepancyNotes}"));
        return body.Length > SupplierSupportService.MaxBodyLength
            ? body[..SupplierSupportService.MaxBodyLength]
            : body;
    }

    /// <summary>
    /// Postgres outbox row (EventType = "supplier_support_ticket.opened") for the supplier
    /// tenant, picked up by worker/src/jobs/notification-dispatch.job.ts. Mirrors the shape of
    /// MarketplaceOrderService.EnqueueShippedNotificationAsync.
    /// </summary>
    private async Task EnqueueDiscrepancyTicketNotificationAsync(
        MarketplaceOrderReceipt receipt, MarketplaceOrder order, SupplierSupportTicketDto ticket,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            ticketId = ticket.Id,
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId  = receipt.SupplierTenantId,
            UserId    = null,
            StoreId   = null,
            Title     = $"Розбіжності при прийомці замовлення {order.OrderNumber}",
            Channel   = "system",
            EventType = "supplier_support_ticket.opened",
            Payload   = payload,
            Status    = "pending",
        }, ct);
    }

    // ── mapping ────────────────────────────────────────────────────────────────

    private async Task<MarketplaceOrderReceiptDto> ToDtoAsync(MarketplaceOrderReceipt r, CancellationToken ct)
    {
        // Batch-fetch fallback reference images for every not-yet-scanned line in one round trip
        // (provider-bypass read — supplier_items RLS has no client-read policy, see
        // MarketplaceRepository.GetSupplierItemImagesByIdsAsync).
        var supplierItemIds = r.Items
            .Where(i => i.ProductId is null && i.OrderItem?.SupplierItemId is not null)
            .Select(i => i.OrderItem!.SupplierItemId!.Value)
            .Distinct()
            .ToList();

        var imagesBySupplierItem = await _marketplace.GetSupplierItemImagesByIdsAsync(supplierItemIds, ct);

        return new(
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
            r.Items.Select(i => ToItemDto(i, imagesBySupplierItem)).ToList());
    }

    private static string? PrimarySupplierItemImageUrl(
        Guid? supplierItemId, IReadOnlyDictionary<Guid, IReadOnlyList<SupplierItemImage>> imagesBySupplierItem)
    {
        if (supplierItemId is null) return null;
        if (!imagesBySupplierItem.TryGetValue(supplierItemId.Value, out var images) || images.Count == 0)
            return null;

        // Same "main, else lowest SortOrder" convention MarketplaceService/SupplierCabinetService
        // already use for supplier item images.
        return (images.FirstOrDefault(img => img.Kind == "main") ?? images.OrderBy(img => img.SortOrder).First()).Url;
    }

    private static MarketplaceOrderReceiptItemDto ToItemDto(
        MarketplaceOrderReceiptItem i, IReadOnlyDictionary<Guid, IReadOnlyList<SupplierItemImage>> imagesBySupplierItem) => new(
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
        IsResolved: i.ProductId.HasValue && i.QuantityReceived.HasValue && i.ExpiryDate.HasValue,
        Price: i.OrderItem?.Price ?? 0m,
        ReferenceImageUrl: i.ProductId.HasValue
            ? i.Product?.ImageUrl
            : PrimarySupplierItemImageUrl(i.OrderItem?.SupplierItemId, imagesBySupplierItem));
}
