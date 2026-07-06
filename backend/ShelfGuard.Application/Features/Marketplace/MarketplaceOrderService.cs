using ShelfGuard.Application.Features.Marketplace.Dtos;
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

    public MarketplaceOrderService(
        IMarketplaceOrderRepository orders,
        ISupplierAgreementRepository agreements,
        IMarketplaceRepository marketplace,
        ISupplierChatRepository tenantNames)
    {
        _orders      = orders;
        _agreements  = agreements;
        _marketplace = marketplace;
        _tenantNames = tenantNames;
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

        order.Status    = request.Status;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);

        return (await ToDtoAsync(order, ct), null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
            o.Items
                .OrderBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase)
                .Select(i => new MarketplaceOrderItemDto(
                    i.Id, i.SupplierItemId, i.ItemName, i.Unit, i.Price, i.Qty, i.LineTotal))
                .ToList());
}
