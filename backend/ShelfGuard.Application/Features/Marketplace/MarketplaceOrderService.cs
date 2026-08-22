using System.Text.Json;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
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
    public const string DestinationStoreRequiredError = "Оберіть магазин-призначення для замовлення.";

    // ── TASK-598: marketplace catalog auto-provisioning ─────────────────────────
    public const string BarcodeCollisionError =
        "Штрихкод товару вже існує у вашому каталозі — потрібно вирішити конфлікт перед оформленням.";
    public const string LinkedItemRequiredError = "Оберіть товар каталогу для прив'язки.";
    public const string LinkedItemNotFoundError = "Товар для прив'язки не знайдено у вашому каталозі.";
    public const string LinkedItemBarcodeMismatchError = "Обраний товар не має спільного штрихкоду з позицією постачальника.";
    public const string SupplierItemNameMissingError =
        "У постачальника не вказано назву товару — неможливо додати його до каталогу.";
    public const string CatalogProvisioningFailedError = "Не вдалося додати товар до каталогу.";

    /// <summary>
    /// Allowed supplier-side status transitions. No entry for Shipped (TASK-586, ADR-033 Decision
    /// 4) — the supplier can no longer self-declare Delivered; a Shipped order's only remaining
    /// transition is through <see cref="MarketplaceOrderReceiptService"/>'s client-confirmed
    /// receiving flow, which writes Status/DeliveredAt directly and bypasses this table entirely.
    /// A missing key here reads unambiguously as "no supplier-initiated transition from this
    /// status" — an empty array would invite a reader to wonder if that's a bug.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedTransitions = new()
    {
        [MarketplaceOrderStatus.New]       = [MarketplaceOrderStatus.Confirmed, MarketplaceOrderStatus.Cancelled],
        [MarketplaceOrderStatus.Confirmed] = [MarketplaceOrderStatus.Shipped, MarketplaceOrderStatus.Cancelled],
    };

    private readonly IMarketplaceOrderRepository _orders;
    private readonly ISupplierAgreementRepository _agreements;
    private readonly IMarketplaceRepository _marketplace;
    private readonly ISupplierChatRepository _tenantNames;
    private readonly INotificationRepository _notifications;
    private readonly ITenantSessionOverride _tenantSessionOverride;
    private readonly IItemRepository _items;
    private readonly IItemService _itemService;

    public MarketplaceOrderService(
        IMarketplaceOrderRepository orders,
        ISupplierAgreementRepository agreements,
        IMarketplaceRepository marketplace,
        ISupplierChatRepository tenantNames,
        INotificationRepository notifications,
        ITenantSessionOverride tenantSessionOverride,
        IItemRepository items,
        IItemService itemService)
    {
        _orders      = orders;
        _agreements  = agreements;
        _marketplace = marketplace;
        _tenantNames = tenantNames;
        _notifications = notifications;
        _tenantSessionOverride = tenantSessionOverride;
        _items       = items;
        _itemService = itemService;
    }

    // ── Client side ───────────────────────────────────────────────────────────

    public async Task<(MarketplaceOrderDto? Order, string? Error, bool IsGateViolation)> CreateOrderAsync(
        Guid clientTenantId, Guid supplierId, CreateMarketplaceOrderDto request, Guid userId,
        CancellationToken ct = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            return (null, EmptyOrderError, false);

        // TASK-586, ADR-033 Decision 2: required for every new order (application-layer only —
        // the DB column stays nullable so historical pre-migration orders remain valid rows).
        if (request.DestinationStoreId is null)
            return (null, DestinationStoreRequiredError, false);

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

        // Pass 1: validate every line and plan its catalog outcome (read-only — no Item is
        // created or linked yet). Only once every line clears this pass do we execute the plans
        // (pass 2, below). This ordering matters: without it, a failure on line 3 could leave
        // lines 1-2's auto-provisioned Items already committed to the DB even though the whole
        // order creation failed, and a client retry would then duplicate them (TASK-598).
        var orderItems = new List<MarketplaceOrderItem>(request.Items.Count);
        var plans = new List<CatalogPlan>(request.Items.Count);
        foreach (var line in request.Items)
        {
            var (item, validationError) = ValidateLine(line, catalog);
            if (validationError is not null)
                return (null, validationError, false);

            var (plan, planError) = await PlanCatalogOutcomeAsync(item!, line, ct);
            if (planError is not null)
                return (null, planError, false);
            plans.Add(plan!);

            var name = item!.CustomName ?? item.Item?.Name ?? string.Empty;
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

        // Pass 2: every line cleared validation and planning — now actually create/link catalog
        // Items, before the order itself is built/persisted below. A mid-loop failure here is a
        // last-resort defence only (see ExecuteCatalogPlanAsync — planning already validated
        // everything the execute step could otherwise fail on).
        foreach (var plan in plans)
        {
            var execError = await ExecuteCatalogPlanAsync(clientTenantId, plan, ct);
            if (execError is not null)
                return (null, execError, false);
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
            DestinationStoreId = request.DestinationStoreId,
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

    /// <summary>
    /// TASK-598 read-only pre-flight: same per-line supplier-catalog validation as
    /// <see cref="CreateOrderAsync"/> (via <see cref="ValidateLine"/>) plus a barcode-collision
    /// check against the calling client tenant's own Item catalog. Never creates or links
    /// anything — CreateOrderAsync re-runs the collision check itself and is the sole source of
    /// truth for what actually gets provisioned.
    /// </summary>
    public async Task<(IReadOnlyList<MarketplaceOrderConflictDto>? Conflicts, string? Error, bool IsGateViolation)> CheckCatalogConflictsAsync(
        Guid clientTenantId, Guid supplierId, IReadOnlyList<CreateMarketplaceOrderItemDto> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return (null, EmptyOrderError, false);

        var supplierTenantId = await _marketplace.GetSupplierTenantIdAsync(supplierId, ct);
        if (supplierTenantId is null)
            return (null, SupplierNotFoundError, false);

        var agreement = await _agreements.GetForPairAsync(supplierTenantId.Value, clientTenantId, ct);
        if (agreement is null || agreement.Status != SupplierAgreementStatus.Active)
            return (null, AgreementRequiredError, true);

        var catalog = (await _marketplace.GetSupplierItemsAsync(supplierId, ct))
            .ToDictionary(i => i.Id);

        var conflicts = new List<MarketplaceOrderConflictDto>();
        foreach (var line in items)
        {
            var (item, validationError) = ValidateLine(line, catalog);
            if (validationError is not null)
                return (null, validationError, false);

            var barcodes = item!.Barcodes.Select(b => b.Barcode).ToList();
            if (barcodes.Count == 0)
                continue;

            var matches = await _items.GetByAnyBarcodeAsync(barcodes, ct);
            var match = matches.FirstOrDefault();
            if (match is null)
                continue;

            conflicts.Add(new MarketplaceOrderConflictDto(
                item.Id,
                new MarketplaceOrderConflictingItemDto(match.Id, match.Name, match.ImageUrl, match.Barcodes)));
        }

        return (conflicts, null, false);
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
    /// Shared per-line validation used by both <see cref="CreateOrderAsync"/> and
    /// <see cref="CheckCatalogConflictsAsync"/> (TASK-598): supplier item exists in the given
    /// catalog, is available, and Qty is within MinQty/MaxQty bounds.
    /// </summary>
    private static (SupplierItem? Item, string? Error) ValidateLine(
        CreateMarketplaceOrderItemDto line, Dictionary<Guid, SupplierItem> catalog)
    {
        if (!catalog.TryGetValue(line.SupplierItemId, out var item))
            return (null, "Позицію не знайдено в каталозі постачальника.");

        var name = item.CustomName ?? item.Item?.Name ?? string.Empty;

        if (!item.IsAvailable)
            return (null, $"Позиція «{name}» наразі недоступна.");

        if (line.Qty <= 0)
            return (null, $"Кількість для «{name}» має бути більшою за нуль.");

        if (item.MinQty.HasValue && line.Qty < item.MinQty.Value)
            return (null, $"Мінімальна кількість для «{name}» — {item.MinQty.Value}.");

        if (item.MaxQty.HasValue && line.Qty > item.MaxQty.Value)
            return (null, $"Максимальна кількість для «{name}» — {item.MaxQty.Value}.");

        return (item, null);
    }

    /// <summary>
    /// TASK-598: what CreateOrderAsync's pass 2 (<see cref="ExecuteCatalogPlanAsync"/>) should do
    /// for one order line once planning has decided it's safe to proceed. "Create" builds a new
    /// client Item from the SupplierItem snapshot; "Link" attaches SourceSupplierItemId to an
    /// already-validated existing client Item instead.
    /// </summary>
    private sealed record CatalogPlan(SupplierItem SupplierItem, bool IsLink, Item? LinkedItem);

    /// <summary>
    /// Read-only planning step (TASK-598): resolves what CatalogAction means for this line
    /// without writing anything. "link" validates LinkedItemId belongs to this client tenant
    /// (ambient RLS on GetByIdAsync already enforces this — a foreign-tenant id resolves to null)
    /// and genuinely shares a barcode with the SupplierItem (defence against a forged/stale
    /// request). null/"auto"/"create_new" re-checks the barcode collision authoritatively — never
    /// trusts a stale earlier call to CheckCatalogConflictsAsync — and rejects null/"auto" when a
    /// collision exists, since silently guessing (auto-link or silent duplicate) is exactly what
    /// this feature must not do.
    /// </summary>
    private async Task<(CatalogPlan? Plan, string? Error)> PlanCatalogOutcomeAsync(
        SupplierItem supplierItem, CreateMarketplaceOrderItemDto line, CancellationToken ct)
    {
        var barcodes = supplierItem.Barcodes.Select(b => b.Barcode).ToList();
        var action = string.IsNullOrWhiteSpace(line.CatalogAction) ? "auto" : line.CatalogAction;

        if (action == "link")
        {
            if (line.LinkedItemId is null)
                return (null, LinkedItemRequiredError);

            var linkedItem = await _items.GetByIdAsync(line.LinkedItemId.Value, ct);
            if (linkedItem is null)
                return (null, LinkedItemNotFoundError);

            if (barcodes.Count == 0 || !linkedItem.Barcodes.Intersect(barcodes).Any())
                return (null, LinkedItemBarcodeMismatchError);

            return (new CatalogPlan(supplierItem, IsLink: true, linkedItem), null);
        }

        if (barcodes.Count > 0)
        {
            var collisions = await _items.GetByAnyBarcodeAsync(barcodes, ct);
            if (collisions.Count > 0 && action != "create_new")
                return (null, BarcodeCollisionError);
        }

        // No collision, or the user explicitly overrode one with "create_new" — either way this
        // line creates a new client Item. Fail fast here (planning stage) on the one input this
        // service doesn't fully control — the supplier item's own name — so pass 2's execute step
        // practically never fails once planning has succeeded for every line.
        var newItemName = supplierItem.CustomName ?? supplierItem.Item?.Name;
        if (string.IsNullOrWhiteSpace(newItemName))
            return (null, SupplierItemNameMissingError);

        return (new CatalogPlan(supplierItem, IsLink: false, null), null);
    }

    /// <summary>
    /// Write step (TASK-598), only ever called after every line in the order has already been
    /// planned successfully (see CreateOrderAsync pass 2). Link: sets SourceSupplierItemId on the
    /// already-validated existing Item. Create: provisions a brand-new client Item from the
    /// SupplierItem snapshot — no client-catalog category mapping, no supplier-side stock policy
    /// equivalent (MinStock/MaxStock/SafetyBuffer = 0), ManagementType "NA" (no default exists),
    /// VatRate 0 (no tenant-level default VAT rate exists in this codebase — known simplification,
    /// see task log), PricePurchase from SupplierItem.Price, PriceRetail left null.
    /// </summary>
    private async Task<string?> ExecuteCatalogPlanAsync(Guid clientTenantId, CatalogPlan plan, CancellationToken ct)
    {
        if (plan.IsLink)
        {
            plan.LinkedItem!.SourceSupplierItemId = plan.SupplierItem.Id;
            _items.Update(plan.LinkedItem);
            await _items.SaveChangesAsync(ct);
            return null;
        }

        var supplierItem = plan.SupplierItem;
        var request = new CreateProductRequest(
            Name: supplierItem.CustomName ?? supplierItem.Item?.Name ?? string.Empty,
            Barcodes: supplierItem.Barcodes.Select(b => b.Barcode).ToList(),
            CategoryId: null,
            SegmentId: null,
            Unit: supplierItem.Unit ?? string.Empty,
            ManagementType: "NA",
            ItemType: null,
            MinStock: 0,
            MaxStock: 0,
            SafetyBuffer: 0,
            StorageTempMin: null,
            StorageTempMax: null,
            ShelfLifeDays: null,
            DefaultSupplierId: null,
            VatRate: 0,
            PricePurchase: supplierItem.Price,
            PriceRetail: null,
            ImageUrl: PickImageUrl(supplierItem),
            Manufacturer: supplierItem.Manufacturer,
            CountryOrigin: supplierItem.ManufacturerCountry,
            PerishabilityClass: null,
            SourceSupplierItemId: supplierItem.Id);

        var (created, error) = await _itemService.CreateAsync(clientTenantId, request, ct);
        return created is null ? error ?? CatalogProvisioningFailedError : null;
    }

    /// <summary>
    /// Same "main first" convention as MarketplaceService/SupplierCabinetService's own image
    /// ordering (<c>Images.OrderBy(img => img.SortOrder)</c>): prefer the lowest-SortOrder image
    /// of Kind "main"; fall back to the lowest-SortOrder image of any kind if there's no "main".
    /// </summary>
    private static string? PickImageUrl(SupplierItem supplierItem)
    {
        var ordered = supplierItem.Images.OrderBy(img => img.SortOrder).ToList();
        return ordered.FirstOrDefault(img => img.Kind == "main")?.Url ?? ordered.FirstOrDefault()?.Url;
    }

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
                .ToList(),
            o.DestinationStoreId);

    // ── Order receiving support (TASK-586) ──────────────────────────────────────

    /// <summary>
    /// Shipped orders of the calling client tenant that still need to be received — no
    /// <see cref="MarketplaceOrderReceipt"/> yet, or one still in "draft" (ADR-033 Decision 5,
    /// endpoint a). Consumed by <see cref="MarketplaceOrderReceiptService"/> so the tenant/DTO
    /// mapping logic above stays in one place instead of being duplicated across services.
    /// </summary>
    public async Task<IReadOnlyList<MarketplaceOrderDto>> ListAwaitingReceiptForClientAsync(
        Guid clientTenantId, CancellationToken ct = default)
    {
        var rows = await _orders.ListAwaitingReceiptForClientAsync(clientTenantId, ct);
        return await ToDtosAsync(rows, ct);
    }
}
