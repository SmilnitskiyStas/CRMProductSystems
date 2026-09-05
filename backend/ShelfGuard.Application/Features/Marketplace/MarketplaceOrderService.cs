using System.Globalization;
using System.Text.Json;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.SupplierInventory;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
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
    public const string OnlyNewCancellableError = "Скасувати замовлення можна лише до його відвантаження.";
    public const string EstimatedDeliveryDaysRequiredError = "Вкажіть орієнтовну кількість днів до доставки.";
    public const string DelayReasonRequiredError = "Вкажіть причину затримки доставки.";
    public const string OnlyShippedCanHaveDelayReasonError = "Причину затримки можна вказати лише для відправленого замовлення.";

    // ── Phase 4 (plan D5): mutable, repeatable delivery-date reschedule ─────────
    public const string OnlyShippedCanRescheduleError = "Змінити дату доставки можна лише для відправленого замовлення.";
    public const string RescheduleDateInPastError = "Дата доставки не може бути в минулому.";
    public const string DestinationStoreRequiredError = "Оберіть магазин-призначення для замовлення.";

    // ── Phase 3 (plan D4): batch-consuming shipment ─────────────────────────────
    public const string OnlyConfirmedCanShipError = "Відвантажити можна лише підтверджене замовлення.";
    public const string SourceWarehouseNotFoundError = "Склад-джерело не знайдено.";
    public const string SourceWarehouseRequiredError = "Оберіть склад, з якого відвантажуєте партії.";
    public const string SupplierInventoryDisabledError =
        "Складський облік постачальника вимкнено — відвантаження партій недоступне.";
    public const string UnknownOrderLineError = "Позицію не знайдено в цьому замовленні.";
    public const string BatchNotFoundError = "Партію не знайдено на складі.";
    public const string BatchWarehouseMismatchError = "Партія належить іншому складу.";
    public const string BatchItemMismatchError = "Партія не відповідає товару позиції замовлення.";
    public const string NoActiveWarehouseError = "У вас ще немає активного складу.";
    public const string StockChangedConcurrentlyError =
        "Залишки щойно змінила інша операція. Оновіть дані та спробуйте ще раз.";

    /// <summary>Module key gating supplier warehouses/batches (plan D7, provider-granted, default-off).</summary>
    private const string SupplierInventoryModuleKey = "supplier_inventory";

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
    private readonly ILocationRepository _locations;
    private readonly IUserRepository _users;
    private readonly ITenantRepository _tenants;
    private readonly ISupplierStockRepository _supplierStock;

    public MarketplaceOrderService(
        IMarketplaceOrderRepository orders,
        ISupplierAgreementRepository agreements,
        IMarketplaceRepository marketplace,
        ISupplierChatRepository tenantNames,
        INotificationRepository notifications,
        ITenantSessionOverride tenantSessionOverride,
        IItemRepository items,
        IItemService itemService,
        ILocationRepository locations,
        IUserRepository users,
        ITenantRepository tenants,
        ISupplierStockRepository supplierStock)
    {
        _orders      = orders;
        _agreements  = agreements;
        _marketplace = marketplace;
        _tenantNames = tenantNames;
        _notifications = notifications;
        _tenantSessionOverride = tenantSessionOverride;
        _items       = items;
        _itemService = itemService;
        _locations   = locations;
        _users       = users;
        _tenants     = tenants;
        _supplierStock = supplierStock;
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

        // TASK-650: snapshot the destination location's region at creation time (like ADR-033 for
        // receipts) — a location's RegionCode may be corrected later, but the delivery-time-by-
        // region metric must reflect where the order actually went. Loaded under the caller's
        // (client) RLS context; a foreign/unknown id resolves to null → DestinationRegionCode
        // stays null, which is acceptable.
        var destination = await _locations.GetByIdAsync(request.DestinationStoreId.Value, ct);

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

            var (plan, planError) = await PlanCatalogOutcomeAsync(clientTenantId, item!, line, ct);
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

        // #4: snapshot the placing client user's display name, resolved under the caller's own
        // (client) RLS context — a supplier session that later reads this order cannot join into
        // the client's users table. Unknown id → null, which is acceptable.
        var creator = await _users.GetByIdAsync(userId, ct);

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
            CreatedByUserName = creator?.FullName,
            DestinationStoreId = request.DestinationStoreId,
            DestinationRegionCode = destination?.RegionCode,
        };

        foreach (var item in orderItems)
        {
            item.OrderId = order.Id;
            order.Items.Add(item);
        }

        await _orders.AddAsync(order, ct);
        await _orders.SaveChangesAsync(ct);

        // #3/#4: the supplier tenant's staff gets a "new order" outbox notification (until now
        // CreateOrderAsync fired nothing at all). notification_queue's tenant_isolation is
        // session-tenant-only and this method runs on the CLIENT session, so the enqueue runs
        // under an explicit override of the SUPPLIER tenant's RLS context — same pattern as the
        // shipped-notification branch in UpdateOrderStatusAsync. order.SupplierTenantId is already
        // trusted here (it came from GetSupplierTenantIdAsync and passed the ACTIVE-agreement
        // gate). Best-effort: a failed enqueue must not fail an already-persisted order, so this
        // is a separate step, not folded into the order insert above.
        await _tenantSessionOverride.ExecuteAsync(order.SupplierTenantId, async () =>
        {
            await EnqueueCreatedNotificationAsync(order, clientTenantId, ct);
            return true;
        }, ct);

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

            // TASK-643/KI-036 defence in depth: IItemRepository.GetByAnyBarcodeAsync carries no
            // app-level TenantId filter by convention (backend-structure.md) and relies on
            // ambient RLS — which the marketplace provider bypass used to defeat, so a foreign
            // tenant's Item could surface here and be echoed back (id, name, image, barcodes) as
            // a "conflict" to a client whose own catalog is empty. clientTenantId is JWT-derived
            // (MarketplaceCooperationController), never taken from the request body.
            var matches = await _items.GetByAnyBarcodeAsync(barcodes, ct);
            var match = matches.FirstOrDefault(m => m.TenantId == clientTenantId);
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

        // TASK-693 (Phase 7, request #1): the client may pull the order any time before it ships.
        // A confirmed order has consumed nothing (supplier SupplierStock is only touched at
        // shipped, Phase 3) so the cancel is byte-for-byte the same as a New cancel — status
        // flips to cancelled with a reason, no stock reversal.
        if (order.Status is not (MarketplaceOrderStatus.New or MarketplaceOrderStatus.Confirmed))
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
        Guid actingUserId, CancellationToken ct = default)
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
            // Phase 3 (plan D4): ONE ship code path. This legacy endpoint is exactly a
            // ShipOrderAsync call with no source warehouse and no allocations — nothing is
            // consumed from supplier_stock and the order simply moves to shipped, the
            // pre-Phase-3 behaviour byte for byte (ETA validation, ShippedAt, the cross-tenant
            // shipped notification). Keeping the two entry points on one implementation is what
            // stops the richer /ship endpoint and this one from drifting apart on the invariants
            // that matter (the confirmed-only gate, and who may write the outbox row).
            //
            // TASK-693: actingUserId (JWT-derived, controller-resolved) flows through as
            // performedByUserId so ShipOrderAsync stamps ShippedByUserId/Name for this entry
            // point too. This path never allocates, so it is never used as
            // SupplierStockMovement.PerformedBy.
            var (shipped, shipError, _) = await ShipOrderAsync(
                supplierTenantId, orderId,
                new ShipOrderRequest(EstimatedDeliveryDays: request.EstimatedDeliveryDays),
                performedByUserId: actingUserId, ct);
            return (shipped, shipError);
        }

        if (request.Status == MarketplaceOrderStatus.Delivered)
            order.DeliveredAt = DateTimeOffset.UtcNow;

        // TASK-693 (Phase 7, request #2): snapshot which supplier employee confirmed the order.
        // The acting user is in the supplier's own tenant (this runs on the supplier session), so
        // a plain repository read resolves the display name — same denormalized-snapshot pattern
        // as CreatedByUserName. Only the confirmed transition captures an actor.
        if (request.Status == MarketplaceOrderStatus.Confirmed && actingUserId != Guid.Empty)
        {
            order.ConfirmedByUserId   = actingUserId;
            order.ConfirmedByUserName = (await _users.GetByIdAsync(actingUserId, ct))?.FullName;
            // TASK-695 (Phase 8): stamp the confirm time next to the actor — the team-performance
            // "avg hours to confirm" KPI measures ConfirmedAt - CreatedAt.
            order.ConfirmedAt         = DateTimeOffset.UtcNow;
        }

        order.Status    = request.Status;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        _orders.Update(order);
        await _orders.SaveChangesAsync(ct);

        return (await ToDtoAsync(order, ct), null);
    }

    // ── Phase 3 (plan D4): batch-consuming shipment ─────────────────────────────

    /// <inheritdoc />
    public async Task<(MarketplaceOrderDto? Order, string? Error, IReadOnlyList<string> Warnings)> ShipOrderAsync(
        Guid supplierTenantId, Guid orderId, ShipOrderRequest request, Guid performedByUserId,
        CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.SupplierTenantId != supplierTenantId)
            return (null, OrderNotFoundError, []);

        // Same gate the AllowedTransitions matrix enforces for the legacy endpoint, restated here
        // because /ship is a second entry point that never consults that table.
        if (order.Status != MarketplaceOrderStatus.Confirmed)
            return (null, OnlyConfirmedCanShipError, []);

        var (days, expectedDate, etaError) = ResolveDeliveryEstimate(request);
        if (etaError is not null)
            return (null, etaError, []);

        var moduleOn = await SupplierInventoryEnabledAsync(supplierTenantId, ct);
        var hasExplicitAllocations =
            request.Lines?.Any(l => l.Allocations is { Count: > 0 }) == true;

        var warnings = new List<string>();
        var consumedStock = false;

        if (request.SourceWarehouseId is not null || hasExplicitAllocations)
        {
            // Allocating is only meaningful with the warehouse module on — otherwise the tenant
            // has no supplier_stock to consume and the request is a client-side mistake, not
            // something to silently drop.
            if (!moduleOn)
                return (null, SupplierInventoryDisabledError, []);

            if (request.SourceWarehouseId is null)
                return (null, SourceWarehouseRequiredError, []);

            var warehouseId = request.SourceWarehouseId.Value;
            if (!await _supplierStock.WarehouseExistsAsync(supplierTenantId, warehouseId, ct))
                return (null, SourceWarehouseNotFoundError, []);

            var allocationError = await AllocateBatchesAsync(
                order, warehouseId, request.Lines, performedByUserId, warnings, ct);
            if (allocationError is not null)
                return (null, allocationError, []);

            order.SourceWarehouseId = warehouseId;
            consumedStock = true;
        }

        order.EstimatedDeliveryDays = days;
        order.ExpectedDeliveryDate  = expectedDate;
        order.ShippedAt             = DateTimeOffset.UtcNow;
        order.Status                = MarketplaceOrderStatus.Shipped;
        order.UpdatedAt             = DateTimeOffset.UtcNow;

        // TASK-693 (Phase 7, request #2): snapshot the supplier employee who shipped it. Covers
        // both entry points — the /ship endpoint (real userId) and the legacy /status {shipped}
        // path, which now forwards its acting user instead of Guid.Empty. Same own-tenant read /
        // denormalized-name pattern as ConfirmedByUserName.
        if (performedByUserId != Guid.Empty)
        {
            order.ShippedByUserId   = performedByUserId;
            order.ShippedByUserName = (await _users.GetByIdAsync(performedByUserId, ct))?.FullName;
        }

        _orders.Update(order);

        if (consumedStock)
        {
            // ONE atomic commit under the SUPPLIER session: the supplier_stock decrements, their
            // ship movements, the marketplace_order_item_batches rows, and the order's own status
            // change. Every one of those tables is writable by the supplier session
            // (marketplace_orders' tenant_isolation is OR-based on both tenants;
            // marketplace_order_item_batches' is keyed on SupplierTenantId), so nothing here
            // needs an override — and it MUST flush before the client-override block below, or
            // the still-pending batch inserts would be flushed with app.tenant_id set to the
            // CLIENT, failing their WITH CHECK with a 42501.
            //
            // Deliberately not routed through SupplierStockService.FefoConsumeAsync even though
            // it implements the same walk: that method commits per call, so a failure on line 3
            // would leave lines 1-2's stock consumed for an order that never shipped, and a
            // retry would consume them a second time. Shipment is one write boundary.
            try
            {
                await _supplierStock.SaveChangesAsync(ct);
            }
            catch (ConcurrencyConflictException)
            {
                // supplier_stock.Quantity carries an xmin token — a concurrent adjust/shipment
                // touched a batch we were allocating. Nothing was written (single transaction);
                // ask the caller to reload and retry rather than overwrite.
                return (null, StockChangedConcurrentlyError, []);
            }
        }

        // TASK-584: mirrors TASK-582's SupplierAgreementService.MarkSignedAsync fix — the
        // notification-queue insert below targets order.ClientTenantId (the recipient), while the
        // ambient DB session here is authenticated as the SUPPLIER tenant (whoever called this
        // endpoint). notification_queue's plain tenant_isolation RLS policy only allows TenantId =
        // session tenant, so an unscoped insert would throw an unhandled Postgres RLS-violation
        // exception (42501) that surfaces to the client as a masked 500/CORS error. Run the
        // enqueue and the SaveChangesAsync tail under an explicit override of the CLIENT tenant's
        // RLS context instead — safe because order.ClientTenantId is already a trusted value at
        // this point (the SupplierTenantId ownership check above already confirmed this order
        // belongs to the calling supplier tenant), and marketplace_orders' own RLS policy is
        // OR-based on both tenants, so the status-change columns flushed by this same call still
        // satisfy their RLS under either tenant.
        //
        // On the allocation path the order row was already committed above, so this call only
        // flushes the outbox row — a failed enqueue can no longer roll back a shipment whose
        // stock has already moved.
        await _tenantSessionOverride.ExecuteAsync(order.ClientTenantId, async () =>
        {
            await EnqueueShippedNotificationAsync(order, ct);
            await _orders.SaveChangesAsync(ct);
            return true;
        }, ct);

        return (await ToDtoAsync(order, ct), null, warnings);
    }

    /// <inheritdoc />
    public async Task<(ShipSuggestionDto? Suggestion, string? Error)> GetShipSuggestionAsync(
        Guid supplierTenantId, Guid orderId, Guid? warehouseId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.SupplierTenantId != supplierTenantId)
            return (null, OrderNotFoundError);

        var (warehouse, warehouseError) = await ResolveWarehouseAsync(supplierTenantId, warehouseId, ct);
        if (warehouseError is not null)
            return (null, warehouseError);

        var lines = new List<ShipSuggestionLineDto>();
        var warnings = new List<string>();

        // Two order lines can reference the same SupplierItem; without this the proposal would
        // hand the same physical batch quantity to both of them.
        var claimed = new Dictionary<Guid, decimal>();

        foreach (var line in order.Items.OrderBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase))
        {
            var allocations = new List<ShipSuggestionAllocationDto>();
            var remaining = line.Qty;

            if (line.SupplierItemId is not null)
            {
                var batches = await _supplierStock.GetFefoOrderedAsync(
                    supplierTenantId, line.SupplierItemId.Value, warehouse!.Id, ct);

                foreach (var batch in batches)
                {
                    if (remaining <= 0) break;

                    var available = batch.Quantity - claimed.GetValueOrDefault(batch.Id);
                    if (available <= 0) continue;

                    var take = Math.Min(available, remaining);
                    claimed[batch.Id] = claimed.GetValueOrDefault(batch.Id) + take;
                    remaining -= take;

                    allocations.Add(new ShipSuggestionAllocationDto(
                        batch.Id, batch.ExpiryDate, batch.BatchNumber, batch.Quantity, take));
                }
            }

            var covered = line.Qty - remaining;
            if (remaining > 0)
                warnings.Add(ShortfallWarning(line.ItemName, covered, line.Qty));

            lines.Add(new ShipSuggestionLineDto(
                line.Id, line.SupplierItemId, line.ItemName, line.Unit,
                line.Qty, covered, remaining, allocations));
        }

        return (new ShipSuggestionDto(order.Id, warehouse!.Id, warehouse.Name, lines, warnings), null);
    }

    /// <summary>
    /// Explicit <paramref name="warehouseId"/> → validated to be an active warehouse Location of
    /// this supplier tenant (a foreign id reads exactly like a missing one — never confirm that
    /// some other tenant owns it). Omitted → the tenant's first active warehouse, which is the
    /// only one most suppliers will ever have.
    /// </summary>
    private async Task<(Location? Warehouse, string? Error)> ResolveWarehouseAsync(
        Guid supplierTenantId, Guid? warehouseId, CancellationToken ct)
    {
        if (warehouseId is not null)
        {
            var explicitWarehouse = await _locations.GetByIdAsync(warehouseId.Value, ct);
            return explicitWarehouse is null
                   || explicitWarehouse.TenantId != supplierTenantId
                   || explicitWarehouse.Type != "warehouse"
                   || !explicitWarehouse.IsActive
                ? (null, SourceWarehouseNotFoundError)
                : (explicitWarehouse, null);
        }

        var owned = await _locations.GetAllAsync(ct);
        var first = owned
            .Where(l => l.TenantId == supplierTenantId && l.IsActive && l.Type == "warehouse")
            .OrderBy(l => l.CreatedAt)
            .FirstOrDefault();

        return first is null ? (null, NoActiveWarehouseError) : (first, null);
    }

    /// <summary>
    /// Covers every order line from <paramref name="warehouseId"/>: explicit allocations when the
    /// caller sent any for that line, auto-FEFO otherwise. Writes the decrements, the <c>ship</c>
    /// movements and the <c>MarketplaceOrderItemBatch</c> rows into the change tracker WITHOUT
    /// saving — the caller commits everything, including the order's status change, in one go.
    ///
    /// An under-covered line is not an error (user decision 2026-09-02: a shortfall ships with a
    /// warning; the uncovered quantity arrives without batch data and the client types the expiry
    /// in by hand). Returning a non-null error means the request itself was malformed — an
    /// unknown order line, or a batch that does not belong to this warehouse/item.
    /// </summary>
    private async Task<string?> AllocateBatchesAsync(
        MarketplaceOrder order, Guid warehouseId, List<ShipLineDto>? lines,
        Guid performedByUserId, List<string> warnings, CancellationToken ct)
    {
        var explicitByLine = (lines ?? [])
            .Where(l => l.Allocations is { Count: > 0 })
            .GroupBy(l => l.OrderItemId)
            .ToDictionary(g => g.Key, g => g.SelectMany(l => l.Allocations!).ToList());

        // Fail loudly on a line id that isn't part of this order rather than silently ignoring it
        // — that shape of mistake would otherwise ship goods with no batch record at all.
        if (explicitByLine.Keys.Any(id => order.Items.All(i => i.Id != id)))
            return UnknownOrderLineError;

        foreach (var line in order.Items)
        {
            decimal covered;

            if (explicitByLine.TryGetValue(line.Id, out var allocations))
            {
                var (explicitCovered, error) = await ApplyExplicitAllocationsAsync(
                    order, line, warehouseId, allocations, performedByUserId, ct);
                if (error is not null) return error;
                covered = explicitCovered;
            }
            else if (line.SupplierItemId is not null)
            {
                covered = await ApplyFefoAsync(order, line, warehouseId, performedByUserId, ct);
            }
            else
            {
                // The supplier catalog entry behind this line was deleted (FK SET NULL) — there
                // is nothing left to FEFO against. Ships uncovered, with a warning.
                covered = 0m;
            }

            if (covered < line.Qty)
                warnings.Add(ShortfallWarning(line.ItemName, covered, line.Qty));
        }

        return null;
    }

    private async Task<(decimal Covered, string? Error)> ApplyExplicitAllocationsAsync(
        MarketplaceOrder order, MarketplaceOrderItem line, Guid warehouseId,
        List<ShipAllocationDto> allocations, Guid performedByUserId, CancellationToken ct)
    {
        var covered = 0m;

        foreach (var allocation in allocations)
        {
            if (allocation.Qty <= 0) continue;

            // Tenant-scoped by the repository AND by RLS; a foreign batch id resolves to null.
            var batch = await _supplierStock.GetByIdAsync(order.SupplierTenantId, allocation.SupplierStockId, ct);
            if (batch is null) return (0m, BatchNotFoundError);
            if (batch.WarehouseId != warehouseId) return (0m, BatchWarehouseMismatchError);
            if (line.SupplierItemId is null || batch.SupplierItemId != line.SupplierItemId.Value)
                return (0m, BatchItemMismatchError);

            // The supplier may ask for more than the batch holds (a stale UI, or a concurrent
            // adjust): clamp instead of failing — the difference simply becomes a shortfall
            // warning, exactly like the auto-FEFO path.
            var take = Math.Min(allocation.Qty, batch.Quantity);
            if (take <= 0) continue;

            await ConsumeBatchAsync(order, line, batch, take, warehouseId, performedByUserId, ct);
            covered += take;
        }

        return (covered, null);
    }

    /// <summary>
    /// Auto-FEFO fallback for a line the caller sent no explicit allocations for: nearest expiry
    /// first, exactly like <c>SupplierStockService.FefoConsumeAsync</c> — but without its internal
    /// SaveChanges, so the whole shipment stays one write boundary (see ShipOrderAsync).
    /// </summary>
    private async Task<decimal> ApplyFefoAsync(
        MarketplaceOrder order, MarketplaceOrderItem line, Guid warehouseId,
        Guid performedByUserId, CancellationToken ct)
    {
        var batches = await _supplierStock.GetFefoOrderedAsync(
            order.SupplierTenantId, line.SupplierItemId!.Value, warehouseId, ct);

        var remaining = line.Qty;
        foreach (var batch in batches)
        {
            if (remaining <= 0) break;

            // The Quantity > 0 filter ran in the DB against the pre-shipment value; a batch an
            // earlier line of this same order already drained is still in the result set.
            if (batch.Quantity <= 0) continue;

            var take = Math.Min(batch.Quantity, remaining);
            await ConsumeBatchAsync(order, line, batch, take, warehouseId, performedByUserId, ct);
            remaining -= take;
        }

        return line.Qty - remaining;
    }

    /// <summary>
    /// Decrements one batch and records the two rows that make the shipment auditable and
    /// re-playable on the client side: a <c>ship</c> <see cref="SupplierStockMovement"/> (supplier
    /// ledger) and a <see cref="MarketplaceOrderItemBatch"/> (the hand-off the client reads to
    /// prefill its receiving draft). Never saves.
    /// </summary>
    private async Task ConsumeBatchAsync(
        MarketplaceOrder order, MarketplaceOrderItem line, SupplierStock batch, decimal take,
        Guid warehouseId, Guid performedByUserId, CancellationToken ct)
    {
        var before = batch.Quantity;
        batch.Quantity -= take;
        batch.Status = StockStatus.Compute(batch.Quantity, batch.ExpiryDate, batch.LastCheckedAt);
        _supplierStock.Update(batch);

        await _supplierStock.AddMovementAsync(new SupplierStockMovement
        {
            TenantId        = order.SupplierTenantId,
            MovementType    = "ship",
            SupplierStockId = batch.Id,
            SupplierItemId  = batch.SupplierItemId,
            FromWarehouseId = warehouseId,
            Quantity        = take,
            QuantityBefore  = before,
            QuantityAfter   = batch.Quantity,
            ReferenceType   = "marketplace_order",
            ReferenceId     = order.Id,
            PerformedBy     = performedByUserId,
        }, ct);

        // ExpiryDate/BatchNumber are SNAPSHOTS, never a live join back to supplier_stock — the
        // project's "expiry_date and batch_number never change on transfer" rule, applied across
        // the tenant boundary.
        await _orders.AddOrderItemBatchAsync(new MarketplaceOrderItemBatch
        {
            OrderItemId      = line.Id,
            OrderId          = order.Id,
            SupplierTenantId = order.SupplierTenantId,
            ClientTenantId   = order.ClientTenantId,
            SupplierStockId  = batch.Id,
            ExpiryDate       = batch.ExpiryDate,
            BatchNumber      = batch.BatchNumber,
            Qty              = take,
        }, ct);
    }

    /// <summary>
    /// The two delivery-estimate forms fill each other in — the legacy status endpoint only ever
    /// sends <c>EstimatedDeliveryDays</c>, the Phase 3 ship modal may send either or both. At
    /// least one is required: the client-facing "shipped" notification quotes the day count, and
    /// Phase 4's in-transit maths needs the date.
    /// </summary>
    private static (int? Days, DateOnly? Expected, string? Error) ResolveDeliveryEstimate(
        ShipOrderRequest request)
    {
        var days = request.EstimatedDeliveryDays;
        var expected = request.ExpectedDeliveryDate;

        if (days is <= 0)
            return (null, null, EstimatedDeliveryDaysRequiredError);
        if (days is null && expected is null)
            return (null, null, EstimatedDeliveryDaysRequiredError);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        days ??= Math.Max(1, expected!.Value.DayNumber - today.DayNumber);
        expected ??= today.AddDays(days.Value);

        return (days, expected, null);
    }

    private async Task<bool> SupplierInventoryEnabledAsync(Guid supplierTenantId, CancellationToken ct)
    {
        // Runs on the supplier's own session, so tenants' tenant_isolation resolves exactly this
        // one row — the same lookup RequireModuleAttribute performs for controller-level gating.
        var tenant = await _tenants.GetByIdAsync(supplierTenantId, ct);
        return tenant is not null && tenant.HasModule(SupplierInventoryModuleKey);
    }

    private static string ShortfallWarning(string itemName, decimal covered, decimal ordered) =>
        $"«{itemName}»: розподілено {FormatQty(covered)} з {FormatQty(ordered)}";

    /// <summary>Trims the numeric(12,3) trailing zeros so "5" reads as "5", not "5.000".</summary>
    private static string FormatQty(decimal value) =>
        value == decimal.Truncate(value)
            ? decimal.Truncate(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);

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

    /// <summary>
    /// Reschedules a shipped order's expected delivery date (supplier-portal expansion Phase 4,
    /// plan D5). Unlike <see cref="SetDelayReasonAsync"/> there is no "already set" guard — the
    /// supplier may push the date as many times as reality demands while the order is still in
    /// transit. Notifies the client tenant the same cross-tenant-outbox way.
    /// </summary>
    public async Task<(MarketplaceOrderDto? Order, string? Error)> SetExpectedDeliveryDateAsync(
        Guid supplierTenantId, Guid orderId, DateOnly date, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.SupplierTenantId != supplierTenantId)
            return (null, OrderNotFoundError);

        if (order.Status != MarketplaceOrderStatus.Shipped)
            return (null, OnlyShippedCanRescheduleError);

        if (date < DateOnly.FromDateTime(DateTime.UtcNow))
            return (null, RescheduleDateInPastError);

        order.ExpectedDeliveryDate = date;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        // Same cross-tenant guard as SetDelayReasonAsync: the notification-queue insert targets
        // order.ClientTenantId while the ambient session is the SUPPLIER tenant, so the enqueue +
        // SaveChanges tail runs under an explicit override of the CLIENT tenant's RLS context.
        // order.ClientTenantId is trusted here — the SupplierTenantId ownership check above
        // confirmed this order belongs to the calling supplier — and marketplace_orders' RLS is
        // OR-based on both tenants, so the ExpectedDeliveryDate column flushed by this same call
        // still satisfies its policy under the client identity.
        _orders.Update(order);
        await _tenantSessionOverride.ExecuteAsync(order.ClientTenantId, async () =>
        {
            await EnqueueDeliveryRescheduledNotificationAsync(order, ct);
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
    /// without writing anything. "link" validates LinkedItemId belongs to this client tenant and
    /// genuinely shares a barcode with the SupplierItem (defence against a forged/stale request).
    /// null/"auto"/"create_new" re-checks the barcode collision authoritatively — never trusts a
    /// stale earlier call to CheckCatalogConflictsAsync — and rejects null/"auto" when a
    /// collision exists, since silently guessing (auto-link or silent duplicate) is exactly what
    /// this feature must not do.
    ///
    /// TASK-643/KI-036: ownership of LinkedItemId, and the collision set, are checked HERE in the
    /// application layer against the JWT-derived <paramref name="clientTenantId"/>. This used to
    /// be delegated to ambient RLS ("a foreign-tenant id resolves to null on GetByIdAsync") —
    /// that assumption was disproved: IItemRepository carries no app-level TenantId filter by
    /// convention, and the marketplace provider bypass leaking into the request made
    /// provider_bypass (PERMISSIVE, WITH CHECK defaulting to USING) resolve — and allow writes to
    /// — every tenant's rows. RLS is still the outer layer; this is the second one, and it does
    /// not depend on any session GUC being correct.
    /// </summary>
    private async Task<(CatalogPlan? Plan, string? Error)> PlanCatalogOutcomeAsync(
        Guid clientTenantId, SupplierItem supplierItem, CreateMarketplaceOrderItemDto line,
        CancellationToken ct)
    {
        var barcodes = supplierItem.Barcodes.Select(b => b.Barcode).ToList();
        var action = string.IsNullOrWhiteSpace(line.CatalogAction) ? "auto" : line.CatalogAction;

        if (action == "link")
        {
            if (line.LinkedItemId is null)
                return (null, LinkedItemRequiredError);

            var linkedItem = await _items.GetByIdAsync(line.LinkedItemId.Value, ct);
            // A foreign-tenant row is reported exactly like a missing one — never confirm that
            // some other tenant owns this id.
            if (linkedItem is null || linkedItem.TenantId != clientTenantId)
                return (null, LinkedItemNotFoundError);

            if (barcodes.Count == 0 || !linkedItem.Barcodes.Intersect(barcodes).Any())
                return (null, LinkedItemBarcodeMismatchError);

            return (new CatalogPlan(supplierItem, IsLink: true, linkedItem), null);
        }

        if (barcodes.Count > 0)
        {
            // Only THIS tenant's catalog can collide with this tenant's catalog — a foreign
            // tenant's row sharing an EAN must never block a legitimate order.
            var collisions = (await _items.GetByAnyBarcodeAsync(barcodes, ct))
                .Where(i => i.TenantId == clientTenantId)
                .ToList();
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
            // TASK-643/KI-036: re-validate ownership at the WRITE, not just at planning time.
            // Pass 1 (planning) and pass 2 (execute) are separated by a whole loop, so the check
            // that guarded the plan is not the check that guards the UPDATE. Cheap, and it closes
            // the cross-tenant write vector even if a future refactor loosens planning.
            if (plan.LinkedItem!.TenantId != clientTenantId)
                return LinkedItemNotFoundError;

            plan.LinkedItem.SourceSupplierItemId = plan.SupplierItem.Id;
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
    /// Supplier-portal expansion (#3): Postgres outbox row (EventType = "marketplace_order.created")
    /// for the SUPPLIER tenant when a client places a new order — mirrors
    /// <see cref="EnqueueShippedNotificationAsync"/> but pointed the other way (recipient is the
    /// supplier's own staff). UserId = null, Channel = "system", Status = "pending"; the worker's
    /// notification-dispatch job resolves it to the supplier tenant's supplier_admin users.
    /// Must be called inside a <see cref="ITenantSessionOverride"/> for the supplier tenant —
    /// notification_queue's tenant_isolation is session-tenant-only and CreateOrderAsync runs on
    /// the client session.
    /// </summary>
    private async Task EnqueueCreatedNotificationAsync(
        MarketplaceOrder order, Guid clientTenantId, CancellationToken ct)
    {
        var clientName = await _tenantNames.GetTenantDisplayNameAsync(clientTenantId, ct)
                         ?? "Замовник";

        var payload = JsonSerializer.Serialize(new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            clientName,
            totalAmount = order.TotalAmount,
            itemCount = order.Items.Count,
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId  = order.SupplierTenantId,
            UserId    = null,
            StoreId   = null,
            Title     = $"Нове замовлення {order.OrderNumber} від «{clientName}»",
            Channel   = "system",
            EventType = "marketplace_order.created",
            Payload   = payload,
            Status    = "pending",
        }, ct);
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

    /// <summary>
    /// Phase 4 (plan D5): Postgres outbox row (EventType = "marketplace_order.delivery_rescheduled")
    /// for the client tenant when the supplier moves a shipped order's expected delivery date.
    /// Mirrors <see cref="EnqueueDelayReasonNotificationAsync"/> — must run inside a client-tenant
    /// <see cref="ITenantSessionOverride"/> (notification_queue's tenant_isolation is session-only).
    /// </summary>
    private async Task EnqueueDeliveryRescheduledNotificationAsync(MarketplaceOrder order, CancellationToken ct)
    {
        var supplierName = await _tenantNames.GetTenantDisplayNameAsync(order.SupplierTenantId, ct)
                           ?? "Постачальник";

        var payload = JsonSerializer.Serialize(new
        {
            orderId = order.Id,
            orderNumber = order.OrderNumber,
            supplierName,
            expectedDeliveryDate = order.ExpectedDeliveryDate,
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId  = order.ClientTenantId,
            UserId    = null,
            StoreId   = null,
            Title     = $"Нова дата доставки для {order.OrderNumber}: {order.ExpectedDeliveryDate:dd.MM.yyyy}",
            Channel   = "system",
            EventType = "marketplace_order.delivery_rescheduled",
            Payload   = payload,
            Status    = "pending",
        }, ct);
    }

    /// <summary>
    /// «MP-{yyyy}-{NNN}» — NNN sequential per supplier via CountForSupplierAsync.
    ///
    /// TASK-645 C1: the count MUST run under the SUPPLIER tenant's RLS context. This method is
    /// called from CreateOrderAsync on the CLIENT session, and marketplace_orders'
    /// tenant_isolation policy is OR-based (<c>"SupplierTenantId" = session OR "ClientTenantId" =
    /// session</c>), so an ambient client session counts only the orders that client is a party to
    /// — making NNN sequential per (supplier, client) pair instead of per supplier, and handing
    /// two different clients of the same supplier the same MP-2026-001. There is no unique index
    /// on OrderNumber, so that would corrupt silently.
    ///
    /// Until TASK-643 this happened to work only because MarketplaceRepository's leaked
    /// session-level <c>app.role='provider'</c> satisfied marketplace_orders' provider_bypass for
    /// the rest of the request (KI-036) — i.e. a customer-visible identifier scheme was
    /// unknowingly resting on the RLS leak. Same ITenantSessionOverride pattern already used for
    /// the cross-tenant notification outbox above; supplierTenantId is a trusted value here (it
    /// came from GetSupplierTenantIdAsync and passed the ACTIVE-agreement gate), the target
    /// policy is OR-based on SupplierTenantId so the supplier identity exposes exactly the
    /// intended rows and nothing more, and no ambient transaction is open at this point (pass 2's
    /// catalog saves have already completed).
    /// </summary>
    private Task<string> NextOrderNumberAsync(Guid supplierTenantId, CancellationToken ct) =>
        _tenantSessionOverride.ExecuteAsync(supplierTenantId, async () =>
        {
            var seq = await _orders.CountForSupplierAsync(supplierTenantId, ct) + 1;
            return $"MP-{DateTime.UtcNow.Year}-{seq:D3}";
        }, ct);

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
            o.CreatedByUserId,
            o.CreatedByUserName,
            o.ConfirmedByUserId,
            o.ConfirmedByUserName,
            o.ShippedByUserId,
            o.ShippedByUserName,
            o.Items
                .OrderBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase)
                .Select(i => new MarketplaceOrderItemDto(
                    i.Id, i.SupplierItemId, i.ItemName, i.Unit, i.Price, i.Qty, i.LineTotal,
                    // Phase 3 (D4): FEFO order, so the client's prefilled receiving sub-rows come
                    // out nearest-expiry-first without the UI having to re-sort them.
                    i.Batches
                        .OrderBy(b => b.ExpiryDate)
                        .ThenBy(b => b.CreatedAt)
                        .Select(b => new MarketplaceOrderItemBatchDto(
                            b.Id, b.ExpiryDate, b.BatchNumber, b.Qty, b.SupplierStockId))
                        .ToList()))
                .ToList(),
            o.DestinationStoreId,
            o.SourceWarehouseId,
            o.ExpectedDeliveryDate);

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

    // ── "New order arrived" badge (supplier-portal expansion #3, Phase 6a) ──────

    public async Task<int> GetUnseenOrderCountForSupplierAsync(
        Guid supplierTenantId, Guid userId, CancellationToken ct = default)
    {
        // The user row is in the supplier's own tenant (this runs on the supplier session), so a
        // plain repository read is enough — no ITenantSessionOverride.
        var user = await _users.GetByIdAsync(userId, ct);

        DateTimeOffset? since = user?.SupplierOrdersLastViewedAt is { } ts
            ? new DateTimeOffset(DateTime.SpecifyKind(ts, DateTimeKind.Utc), TimeSpan.Zero)
            : null;

        return await _orders.CountUnseenForSupplierAsync(supplierTenantId, since, ct);
    }

    public async Task MarkSupplierOrdersSeenAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return;

        user.MarkSupplierOrdersViewed();
        _users.Update(user);
        await _users.SaveChangesAsync(ct);
    }
}
