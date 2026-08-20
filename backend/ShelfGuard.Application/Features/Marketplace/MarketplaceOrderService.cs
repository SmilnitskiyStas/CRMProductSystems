using System.Text.Json;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// B2B marketplace orders (TASK-317). Hard gate: a pair may only place orders
/// while its cooperation agreement is ACTIVE. Item name/price/unit are
/// snapshotted into order items at creation time — later catalog edits never
/// rewrite history.
/// </summary>
public sealed class MarketplaceOrderService : IMarketplaceOrderService
{
    public const string AgreementRequiredError =
        "Замовлення доступні лише після укладення договору про співпрацю";
    public const string SupplierNotFoundError = "Постачальника не знайдено.";
    public const string OrderNotFoundError = "Замовлення не знайдено.";
    public const string EmptyOrderError = "Додайте хоча б одну позицію до замовлення.";
    public const string CancelReasonRequiredError = "Вкажіть причину скасування.";
    public const string OnlyNewCancellableError = "Скасувати можна лише замовлення у статусі «нове».";
    public const string EstimatedDeliveryDaysRequiredError = "Вкажіть орієнтовну кількість днів до доставки.";
    public const string DelayReasonRequiredError = "Вкажіть причину затримки доставки.";
    public const string OnlyShippedCanHaveDelayReasonError = "Причину затримки можна вказати лише для відправленого замовлення.";

    /// <summary>Allowed supplier-side status transitions.</summary>
    private static readonly Dictionary<string, string[]> AllowedTransitions = new()
    {
        [MarketplaceOrderStatus.New]       = [MarketplaceOrderStatus.Confirmed, MarketplaceOrderStatus.Cancelled],
        [MarketplaceOrderStatus.Confirmed] = [MarketplaceOrderStatus.Shipped, MarketplaceOrderStatus.Cancelled],
        [MarketplaceOrderStatus.Shipped]   = [MarketplaceOrderStatus.Delivered],
    };

    private readonly IMarketplaceOrderRepository _orders;
    private readonly ISupplierAgreementRepository _agreements;
    private readonly IMarketplaceRepository _marketplace;
    private readonly ISupplierChatRepository _tenantNames;
    private readonly INotificationRepository _notifications;
    private readonly ITenantSessionOverride _tenantSessionOverride;

    public MarketplaceOrderService(
        IMarketplaceOrderRepository orders,
        ISupplierAgreementRepository agreements,
        IMarketplaceRepository marketplace,
        ISupplierChatRepository tenantNames,
        INotificationRepository notifications,
        ITenantSessionOverride tenantSessionOverride)
    {
        _orders      = orders;
        _agreements  = agreements;
        _marketplace = marketplace;
        _tenantNames = tenantNames;
        _notifications = notifications;
        _tenantSessionOverride = tenantSessionOverride;
    }

    // ── Client side ───────────────────────────────────────────────────────────

    public async Task<(MarketplaceOrderDto? Order, string? Error, bool IsGateViolation)> CreateOrderAsync(
        Guid clientTenantId, Guid supplierId, CreateMarketplaceOrderDto request, Guid userId,
        CancellationToken ct = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            return (null, EmptyOrderError, false);

        var supplierTenantId = await _marketplace.GetSupplierTenantIdAsync(supplierId, ct);
        if (supplierTenantId is null)
            return (null, SupplierNotFoundError, false);

        // The gate: only an ACTIVE agreement of the pair unlocks ordering.
        var agreement = await _agreements.GetForPairAsync(supplierTenantId.Value, clientTenantId, ct);
        if (agreement is null || agreement.Status != SupplierAgreementStatus.Active)
            return (null, AgreementRequiredError, true);

        // Validate every requested position against the supplier's live catalog.
        var catalog = (await _marketplace.GetSupplierItemsAsync(supplierId, ct))
            .ToDictionary(i => i.Id);

        var orderItems = new List<MarketplaceOrderItem>(request.Items.Count);
        foreach (var line in request.Items)
        {
            if (!catalog.TryGetValue(line.SupplierItemId, out var item))
                return (null, "Позицію не знайдено в каталозі постачальника.", false);

            var name = item.CustomName ?? item.Item?.Name ?? string.Empty;

            if (!item.IsAvailable)
                return (null, $"Позиція «{name}» наразі недоступна.", false);

            if (line.Qty <= 0)
                return (null, $"Кількість для «{name}» має бути більшою за нуль.", false);

            if (item.MinQty.HasValue && line.Qty < item.MinQty.Value)
                return (null, $"Мінімальна кількість для «{name}» — {item.MinQty.Value}.", false);

            if (item.MaxQty.HasValue && line.Qty > item.MaxQty.Value)
                return (null, $"Максимальна кількість для «{name}» — {item.MaxQty.Value}.", false);

            var price = item.Price ?? 0m;
            orderItems.Add(new MarketplaceOrderItem
            {
                SupplierTenantId = supplierTenantId.Value,
                ClientTenantId   = clientTenantId,
                SupplierItemId   = item.Id,
                ItemName         = name,
                Unit             = item.Unit,
                Price            = price,
                Qty              = line.Qty,
                LineTotal        = decimal.Round(price * line.Qty, 2),
            });
        }

        var order = new MarketplaceOrder
        {
            OrderNumber      = await NextOrderNumberAsync(supplierTenantId.Value, ct),
            AgreementId      = agreement.Id,
            SupplierTenantId = supplierTenantId.Value,
            ClientTenantId   = clientTenantId,
            Status           = MarketplaceOrderStatus.New,
            Comment          = NormalizeComment(request.Comment),
            TotalAmount      = orderItems.Sum(i => i.LineTotal),
            CreatedByUserId  = userId,
        };

        foreach (var item in orderItems)
        {
            item.OrderId = order.Id;
            order.Items.Add(item);
        }

        await _orders.AddAsync(order, ct);
        await _orders.SaveChangesAsync(ct);

        return (await ToDtoAsync(order, ct), null, false);
    }

    public async Task<IReadOnlyList<MarketplaceOrderDto>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default)
    {
        var rows = await _orders.ListForClientAsync(clientTenantId, ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<(MarketplaceOrderDto? Order, string? Error)> CancelOrderAsync(
        Guid clientTenantId, Guid orderId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (null, CancelReasonRequiredError);

        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.ClientTenantId != clientTenantId)
            return (null, OrderNotFoundError);

        if (order.Status != MarketplaceOrderStatus.New)
            return (null, OnlyNewCancellableError);

        order.Status       = MarketplaceOrderStatus.Cancelled;
        order.CancelReason = reason.Trim();
        order.UpdatedAt    = DateTimeOffset.UtcNow;

        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);

        return (await ToDtoAsync(order, ct), null);
    }

    // ── Supplier side ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MarketplaceOrderDto>> ListForSupplierAsync(
        Guid supplierTenantId, CancellationToken ct = default)
    {
        var rows = await _orders.ListForSupplierAsync(supplierTenantId, ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<(MarketplaceOrderDto? Order, string? Error)> UpdateOrderStatusAsync(
        Guid supplierTenantId, Guid orderId, UpdateMarketplaceOrderStatusDto request,
        CancellationToken ct = default)
    {
        if (!MarketplaceOrderStatus.All.Contains(request.Status))
            return (null, $"Невідомий статус: '{request.Status}'.");

        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.SupplierTenantId != supplierTenantId)
            return (null, OrderNotFoundError);

        if (!AllowedTransitions.TryGetValue(order.Status, out var allowed)
            || !allowed.Contains(request.Status))
            return (null, $"Перехід зі статусу '{order.Status}' у '{request.Status}' неможливий.");

        if (request.Status == MarketplaceOrderStatus.Cancelled)
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                return (null, CancelReasonRequiredError);
            order.CancelReason = request.Reason.Trim();
        }

        if (request.Status == MarketplaceOrderStatus.Shipped)
        {
            if (request.EstimatedDeliveryDays is null or <= 0)
                return (null, EstimatedDeliveryDaysRequiredError);
            order.ShippedAt = DateTimeOffset.UtcNow;
            order.EstimatedDeliveryDays = request.EstimatedDeliveryDays;
        }
        else if (request.Status == MarketplaceOrderStatus.Delivered)
        {
            order.DeliveredAt = DateTimeOffset.UtcNow;
        }

        order.Status    = request.Status;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.Status == MarketplaceOrderStatus.Shipped)
        {
            // TASK-584: mirrors TASK-582's SupplierAgreementService.MarkSignedAsync fix — the
            // notification-queue insert below targets order.ClientTenantId (the recipient), while
            // the ambient DB session here is authenticated as the SUPPLIER tenant (whoever called
            // this endpoint). notification_queue's plain tenant_isolation RLS policy only allows
            // TenantId = session tenant, so an unscoped insert would throw an unhandled Postgres
            // RLS-violation exception (42501) that surfaces to the client as a masked 500/CORS
            // error. Run the enqueue and the final SaveChangesAsync under an explicit override of
            // the CLIENT tenant's RLS context instead — safe because order.ClientTenantId is
            // already a trusted value at this point (the SupplierTenantId ownership check above
            // already confirmed this order belongs to the calling supplier tenant), and
            // marketplace_orders' own RLS policy is OR-based on both SupplierTenantId/
            // ClientTenantId, so the status-change columns flushed by this same SaveChangesAsync
            // call still satisfy their RLS under either tenant. Bonus: the status change and the
            // outbox row commit atomically.
            _orders.Update(order);
            await _tenantSessionOverride.ExecuteAsync(order.ClientTenantId, async () =>
            {
                await EnqueueShippedNotificationAsync(order, ct);
                await _orders.SaveChangesAsync(ct);
                return true;
            }, ct);
        }
        else
        {
            _orders.Update(order);
            await _orders.SaveChangesAsync(ct);
        }

        return (await ToDtoAsync(order, ct), null);
    }

    /// <summary>
    /// Records why a shipped order's delivery is running late (TASK-585). Notifies the
    /// client tenant the same cross-tenant-outbox way the Shipped branch above does.
    /// </summary>
    public async Task<(MarketplaceOrderDto? Order, string? Error)> SetDelayReasonAsync(
        Guid supplierTenantId, Guid orderId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (null, DelayReasonRequiredError);

        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.SupplierTenantId != supplierTenantId)
            return (null, OrderNotFoundError);

        if (order.Status != MarketplaceOrderStatus.Shipped)
            return (null, OnlyShippedCanHaveDelayReasonError);

        order.DelayReason = reason.Trim();
        order.UpdatedAt = DateTimeOffset.UtcNow;

        // Same TASK-584/TASK-582 pattern: the notification-queue insert targets
        // order.ClientTenantId (the recipient) while the ambient DB session is authenticated
        // as the SUPPLIER tenant, so the enqueue + SaveChanges tail must run under an explicit
        // override of the CLIENT tenant's RLS context — order.ClientTenantId is already a
        // trusted value here (the SupplierTenantId ownership check above confirmed this order
        // belongs to the calling supplier tenant).
        _orders.Update(order);
        await _tenantSessionOverride.ExecuteAsync(order.ClientTenantId, async () =>
        {
            await EnqueueDelayReasonNotificationAsync(order, ct);
            await _orders.SaveChangesAsync(ct);
            return true;
        }, ct);

        return (await ToDtoAsync(order, ct), null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ADR-018 §2: Postgres outbox row (EventType = "marketplace_order.shipped") for the client
    /// tenant, picked up by the worker's notification-dispatch job. UserId = null,
    /// Channel = "system", Status = "pending". Directly closes the "nowhere shows the order is
    /// on the way" complaint — the client gets a proactive notification, not just a DB field they
    /// have to go look for.
    /// </summary>
    private async Task EnqueueShippedNotificationAsync(MarketplaceOrder order, CancellationToken ct)
    {
        var supplierName = await _tenantNames.GetTenantDisplayNameAsync(order.SupplierTenantId, ct)
                           ?? "Постачальник";

        var payload = JsonSerializer.Serialize(new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            supplierName,
            estimatedDeliveryDays = order.EstimatedDeliveryDays,
            shippedAt = order.ShippedAt,
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId  = order.ClientTenantId,
            UserId    = null,
            StoreId   = null,
            Title     = $"Замовлення {order.OrderNumber} відправлено — очікується за ~{order.EstimatedDeliveryDays} дн.",
            Channel   = "system",
            EventType = "marketplace_order.shipped",
            Payload   = payload,
            Status    = "pending",
        }, ct);
    }

    /// <summary>
    /// TASK-585: Postgres outbox row (EventType = "marketplace_order.delay_reason_added") for
    /// the client tenant when the supplier explains why a shipped order is running late.
    /// </summary>
    private async Task EnqueueDelayReasonNotificationAsync(MarketplaceOrder order, CancellationToken ct)
    {
        var supplierName = await _tenantNames.GetTenantDisplayNameAsync(order.SupplierTenantId, ct)
                           ?? "Постачальник";

        var payload = JsonSerializer.Serialize(new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            supplierName,
            reason = order.DelayReason,
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId  = order.ClientTenantId,
            UserId    = null,
            StoreId   = null,
            Title     = $"Затримка доставки: {order.OrderNumber}",
            Channel   = "system",
            EventType = "marketplace_order.delay_reason_added",
            Payload   = payload,
            Status    = "pending",
        }, ct);
    }

    /// <summary>«MP-{yyyy}-{NNN}» — NNN sequential per supplier via CountForSupplierAsync.</summary>
    private async Task<string> NextOrderNumberAsync(Guid supplierTenantId, CancellationToken ct)
    {
        var seq = await _orders.CountForSupplierAsync(supplierTenantId, ct) + 1;
        return $"MP-{DateTime.UtcNow.Year}-{seq:D3}";
    }

    private static string? NormalizeComment(string? comment)
    {
        var trimmed = comment?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private async Task<IReadOnlyList<MarketplaceOrderDto>> ToDtosAsync(
        IReadOnlyList<MarketplaceOrder> rows, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        var result = new List<MarketplaceOrderDto>(rows.Count);
        foreach (var row in rows)
            result.Add(ToDto(row,
                await GetNameCachedAsync(row.SupplierTenantId, names, ct),
                await GetNameCachedAsync(row.ClientTenantId, names, ct)));
        return result;
    }

    private async Task<MarketplaceOrderDto> ToDtoAsync(MarketplaceOrder o, CancellationToken ct) =>
        ToDto(o,
            await _tenantNames.GetTenantDisplayNameAsync(o.SupplierTenantId, ct) ?? string.Empty,
            await _tenantNames.GetTenantDisplayNameAsync(o.ClientTenantId, ct) ?? string.Empty);

    private async Task<string> GetNameCachedAsync(
        Guid tenantId, Dictionary<Guid, string> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(tenantId, out var name)) return name;
        name = await _tenantNames.GetTenantDisplayNameAsync(tenantId, ct) ?? string.Empty;
        cache[tenantId] = name;
        return name;
    }

    private static MarketplaceOrderDto ToDto(
        MarketplaceOrder o, string supplierName, string clientName) =>
        new(
            o.Id,
            o.OrderNumber,
            o.AgreementId,
            o.SupplierTenantId,
            o.ClientTenantId,
            supplierName,
            clientName,
            o.Status,
            o.Comment,
            o.CancelReason,
            o.TotalAmount,
            o.CreatedAt,
            o.UpdatedAt,
            o.ShippedAt,
            o.EstimatedDeliveryDays,
            o.DeliveredAt,
            o.DelayReason,
            o.Items
                .OrderBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase)
                .Select(i => new MarketplaceOrderItemDto(
                    i.Id, i.SupplierItemId, i.ItemName, i.Unit, i.Price, i.Qty, i.LineTotal))
                .ToList());
}
