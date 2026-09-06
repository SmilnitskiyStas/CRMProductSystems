namespace ShelfGuard.Application.Features.Marketplace.Dtos;

// ═══════════════════════════════════════════════════════════════════════════
// TASK-317 — Supplier cooperation: agreements, contract settings, marketplace
// orders, supplier support tickets. Kept separate from MarketplaceDtos.cs.
// ═══════════════════════════════════════════════════════════════════════════

// ── Cooperation agreements ───────────────────────────────────────────────────

/// <summary>
/// A supplier↔client cooperation agreement as seen by either party.
/// RejectionReason doubles as the termination reason when Status = terminated.
/// </summary>
public record CooperationAgreementDto(
    Guid Id,
    Guid SupplierTenantId,
    Guid ClientTenantId,
    string SupplierName,
    string ClientName,
    string Status,
    string? RequestMessage,
    string? RejectionReason,
    string? ContractNumber,
    bool HasContractFile,
    string? VchasnoDocumentId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? SignedAt,
    DateTimeOffset? TerminatedAt,
    string? SigningMethod,
    string? SigningEmail,
    string? SupplierLegalAddress);

/// <summary>
/// Client → supplier cooperation request body. ClientLegalEntityId (TASK-327)
/// optionally selects which of the client's own registered legal entities
/// (ТОВ/ФОП) is requesting cooperation — validated to belong to the caller's
/// tenant in the service layer.
/// </summary>
public record SubmitCooperationRequestDto(string? Message, Guid? ClientLegalEntityId = null);

public record RejectCooperationRequestDto(string Reason);

public record TerminateAgreementDto(string? Reason);

/// <summary>Client's chosen contract signing method: "physical" or "vchasno" (requires Email).</summary>
public record ChooseSigningMethodDto(string Method, string? Email);

// ── Supplier contract settings (requisites) ──────────────────────────────────

public record SupplierContractSettingsDto(
    string LegalName,
    string? Edrpou,
    string? Iban,
    string? BankName,
    string? LegalAddress,
    string? DirectorName,
    string? Phone,
    string? Email,
    string? ServiceName,
    string? ServiceDescription,
    string? SignatureImageUrl,
    string? StampImageUrl,
    bool IsVatPayer,
    DateTimeOffset UpdatedAt);

/// <summary>Full-replace upsert of the supplier's contract requisites (images are uploaded separately).</summary>
public record UpsertContractSettingsDto(
    string LegalName,
    string? Edrpou,
    string? Iban,
    string? BankName,
    string? LegalAddress,
    string? DirectorName,
    string? Phone,
    string? Email,
    string? ServiceName,
    string? ServiceDescription,
    bool IsVatPayer = false);

// ── Marketplace orders ───────────────────────────────────────────────────────

/// <summary>
/// CatalogAction resolves a barcode collision between this line's SupplierItem and the client's
/// own Item catalog (TASK-598): null/"auto" (default) — auto-provision a new Item unless a
/// collision is found, in which case the whole order is rejected; "link" — attach provenance
/// (SourceSupplierItemId) to an existing client Item instead of creating a new one, requires
/// LinkedItemId; "create_new" — create a new Item anyway even though a collision exists (the
/// user explicitly chose to keep them separate). See
/// <see cref="MarketplaceOrderService.CheckCatalogConflictsAsync"/> for the pre-flight check that
/// tells the frontend which lines need a CatalogAction before the real order is submitted.
/// </summary>
public record CreateMarketplaceOrderItemDto(
    Guid SupplierItemId,
    decimal Qty,
    string? CatalogAction = null,
    Guid? LinkedItemId = null);

/// <summary>
/// DestinationStoreId is required for every new order (TASK-586, ADR-033 Decision 2) — validated
/// in <see cref="MarketplaceOrderService.CreateOrderAsync"/>, not enforced at the DB level (the
/// column stays nullable so historical pre-migration orders remain valid rows).
/// </summary>
public record CreateMarketplaceOrderDto(
    List<CreateMarketplaceOrderItemDto> Items,
    string? Comment,
    Guid? DestinationStoreId = null);

/// <summary>Request body for the conflicts pre-flight check — same item shape as the real order.</summary>
public record CheckMarketplaceOrderConflictsDto(List<CreateMarketplaceOrderItemDto> Items);

/// <summary>The client's own existing Item that collides with a supplier item's barcode.</summary>
public record MarketplaceOrderConflictingItemDto(
    Guid Id,
    string Name,
    string? ImageUrl,
    List<string> Barcodes);

/// <summary>
/// One order line whose SupplierItem shares a barcode with an Item already in the client's
/// catalog (TASK-598). Frontend renders this as a "this barcode already exists on item X"
/// comparison card and asks the user to choose a CatalogAction ("link" or "create_new") before
/// resubmitting the order. An empty conflicts list is safe to submit as-is.
/// </summary>
public record MarketplaceOrderConflictDto(
    Guid SupplierItemId,
    MarketplaceOrderConflictingItemDto ExistingItem);

/// <summary>
/// One catalog auto-merge recorded on an order (TASK-697): a supplier barcode set was silently
/// merged into the client's own already-linked <c>Item</c> at order-creation time (case 2 — the
/// Item whose <c>SourceSupplierItemId</c> already points at this supplier item). No modal is
/// shown; the client is informed via a toast right after checkout and a permanent "catalog
/// changes" row on the order. <see cref="AddedBarcodes"/> lists the barcodes newly added to the
/// Item; <see cref="PrimaryChanged"/> / <see cref="NewPrimaryBarcode"/> report whether the
/// supplier's primary barcode became the Item's new primary (<c>Barcodes[0]</c>). No existing
/// barcode is ever dropped.
/// </summary>
public record MarketplaceOrderCatalogChangeDto(
    Guid ItemId,
    string ItemName,
    IReadOnlyList<string> AddedBarcodes,
    bool PrimaryChanged,
    string? NewPrimaryBarcode);

/// <summary>
/// One batch the supplier allocated to an order line at ship time (Phase 3, plan D4). The
/// warehouse these came from is the order's <see cref="MarketplaceOrderDto.SourceWarehouseId"/>
/// — one source warehouse per order, so it is not repeated per batch. Empty list for legacy
/// orders and for shipments made while the supplier's <c>supplier_inventory</c> module is off.
/// </summary>
public record MarketplaceOrderItemBatchDto(
    Guid Id,
    DateOnly ExpiryDate,
    string? BatchNumber,
    decimal Qty,
    /// <summary>Source supplier_stock row; null once that batch row has been purged.</summary>
    Guid? SupplierStockId);

public record MarketplaceOrderItemDto(
    Guid Id,
    Guid? SupplierItemId,
    string ItemName,
    string? Unit,
    decimal Price,
    decimal Qty,
    decimal LineTotal,
    /// <summary>
    /// Supplier-allocated batches for this line (Phase 3, plan D4). Always present, possibly
    /// empty. Both parties can read it: the supplier through the table's own
    /// <c>tenant_isolation</c>, the client through the inverted <c>client_read</c> policy.
    /// </summary>
    IReadOnlyList<MarketplaceOrderItemBatchDto> Batches);

public record MarketplaceOrderDto(
    Guid Id,
    string OrderNumber,
    Guid AgreementId,
    Guid SupplierTenantId,
    Guid ClientTenantId,
    string SupplierName,
    string ClientName,
    string Status,
    string? Comment,
    string? CancelReason,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ShippedAt,
    int? EstimatedDeliveryDays,
    DateTimeOffset? DeliveredAt,
    string? DelayReason,
    /// <summary>
    /// Client-side user who placed the order + their denormalized display name (supplier-portal
    /// expansion, plan #4). CreatedByUserName is a snapshot taken at creation — a supplier session
    /// can render "who ordered" without a cross-tenant users join. Both nullable — seed data and
    /// orders placed before the column existed have no value.
    /// </summary>
    Guid? CreatedByUserId,
    string? CreatedByUserName,
    /// <summary>
    /// Supplier-side employees who confirmed / shipped the order + their denormalized name
    /// snapshots (TASK-693, Phase 7). Same cross-tenant-safe pattern as CreatedByUserName — a
    /// client session reads the name without a join into the supplier's users table. All nullable:
    /// not-yet-confirmed / not-yet-shipped orders, and orders processed before the columns existed.
    /// </summary>
    Guid? ConfirmedByUserId,
    string? ConfirmedByUserName,
    Guid? ShippedByUserId,
    string? ShippedByUserName,
    IReadOnlyList<MarketplaceOrderItemDto> Items,
    /// <summary>
    /// Barcodes auto-merged into the client's own already-linked Items when this order was placed
    /// (TASK-697, case 2). Always present, possibly empty. Drives the post-checkout toast and the
    /// permanent "catalog changes" row in the order detail view.
    /// </summary>
    IReadOnlyList<MarketplaceOrderCatalogChangeDto> CatalogChanges,
    /// <summary>
    /// Read-only (TASK-586, ADR-033 Decision 2). Nullable — orders placed before this column
    /// existed have no value and can never be received through the new client-confirmation flow.
    /// </summary>
    Guid? DestinationStoreId = null,
    /// <summary>
    /// Supplier warehouse the order was picked from (Phase 3, plan D4). One source warehouse per
    /// order. Null for legacy orders and for shipments made with the supplier's
    /// <c>supplier_inventory</c> module off.
    /// </summary>
    Guid? SourceWarehouseId = null,
    /// <summary>
    /// Supplier-set expected delivery date. Filled at ship time from the request, or derived as
    /// <c>ShippedAt + EstimatedDeliveryDays</c>. Phase 4 adds the reschedule endpoint.
    /// </summary>
    DateOnly? ExpectedDeliveryDate = null);

public record CancelMarketplaceOrderDto(string Reason);

/// <summary>
/// "New order arrived" badge payload (supplier-portal expansion #3, Phase 6a) —
/// <c>GET /api/supplier-cabinet/orders/unseen-count</c>.
/// </summary>
public record UnseenOrdersCountDto(int Count);

// ── Batch-consuming shipment (Phase 3, plan D4) ──────────────────────────────

/// <summary>One <c>supplier_stock</c> batch and how much of it goes onto an order line.</summary>
public record ShipAllocationDto(Guid SupplierStockId, decimal Qty);

/// <summary>
/// Per-order-line allocation plan. An empty/omitted <see cref="Allocations"/> list means
/// "decide for me" — the service auto-FEFOs that line from the source warehouse. Explicit
/// allocations always win over auto-FEFO.
/// </summary>
public record ShipLineDto(Guid OrderItemId, List<ShipAllocationDto>? Allocations = null);

/// <summary>
/// Supplier-side ship request (Phase 3, plan D4). Every field is optional so that the legacy
/// <c>POST /orders/{id}/status {status:"shipped"}</c> path maps onto the very same service
/// method: with no <see cref="SourceWarehouseId"/> nothing is consumed and the order simply
/// moves to shipped, exactly as before.
///
/// <see cref="EstimatedDeliveryDays"/> and <see cref="ExpectedDeliveryDate"/> fill each other in
/// — supply either one (at least one is required).
/// </summary>
public record ShipOrderRequest(
    Guid? SourceWarehouseId = null,
    DateOnly? ExpectedDeliveryDate = null,
    int? EstimatedDeliveryDays = null,
    List<ShipLineDto>? Lines = null);

/// <summary>
/// Ship result. <see cref="Warnings"/> lists the lines the supplier could not fully cover from
/// stock — a shortfall is deliberately allowed (user decision 2026-09-02): the goods still ship,
/// the uncovered quantity simply arrives without batch data and the client types the expiry in
/// by hand, exactly as before Phase 3.
/// </summary>
public record ShipOrderResultDto(
    MarketplaceOrderDto Order,
    IReadOnlyList<string> Warnings);

/// <summary>
/// One usable batch offered for a ship-suggestion line — either FEFO-picked (<see cref="Qty"/>
/// &gt; 0) or merely offered so the supplier can split the line into it (<see cref="Qty"/> = 0).
/// </summary>
public record ShipSuggestionAllocationDto(
    Guid SupplierStockId,
    DateOnly ExpiryDate,
    string? BatchNumber,
    /// <summary>Quantity currently on that batch — the editable cap for this pick.</summary>
    decimal Available,
    /// <summary>Proposed quantity to take from this batch — 0 for a batch FEFO did not need.</summary>
    decimal Qty);

public record ShipSuggestionLineDto(
    Guid OrderItemId,
    Guid? SupplierItemId,
    string ItemName,
    string? Unit,
    decimal Qty,
    /// <summary>Sum of the FEFO-proposed allocation quantities — less than <see cref="Qty"/> when stock is short.</summary>
    decimal Covered,
    decimal Shortfall,
    /// <summary>Every usable batch for this item in the warehouse — FEFO picks carry a non-zero Qty, the rest Qty = 0.</summary>
    IReadOnlyList<ShipSuggestionAllocationDto> Allocations);

/// <summary>
/// Editable FEFO proposal for shipping an order out of one warehouse (Phase 3, plan D4). The
/// supplier UI renders it, lets the user adjust quantities/batches, and posts the result back as
/// <see cref="ShipOrderRequest.Lines"/>.
/// </summary>
public record ShipSuggestionDto(
    Guid OrderId,
    Guid? WarehouseId,
    string? WarehouseName,
    IReadOnlyList<ShipSuggestionLineDto> Lines,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Supplier-side status change. Reason is required when Status = cancelled.
/// EstimatedDeliveryDays (whole days, must be &gt; 0) is required when Status = shipped
/// (TASK-584) — the client sees it as the order's ETA.
/// </summary>
public record UpdateMarketplaceOrderStatusDto(string Status, string? Reason = null, int? EstimatedDeliveryDays = null);

/// <summary>
/// Supplier-entered explanation when a shipped order's delivery is running past its
/// estimated window (TASK-585). Only allowed while the order is still status = shipped.
/// </summary>
public record SetOrderDelayReasonDto(string Reason);

/// <summary>
/// Supplier-set new expected delivery date for a shipped order (supplier-portal expansion
/// Phase 4, plan D5). Repeatable while the order is still status = shipped; must not be in the
/// past.
/// </summary>
public record SetOrderExpectedDeliveryDateDto(DateOnly ExpectedDeliveryDate);

// ── Marketplace order receiving (TASK-586, ADR-033) ──────────────────────────
// Client-confirmed receipt of a shipped MarketplaceOrder — replaces the supplier's one-click
// Shipped -> Delivered transition. See ADR-033 Decision 5 for the full endpoint contract.

public record MarketplaceOrderReceiptItemDto(
    Guid Id,
    Guid MarketplaceOrderItemId,
    Guid? ProductId,
    /// <summary>Snapshot of the ordered item's name — shown before ProductId resolves.</summary>
    string ItemNameSnapshot,
    /// <summary>Resolved product name once ProductId is set; null until scanned.</summary>
    string? ProductName,
    decimal QuantityOrdered,
    decimal? QuantityReceived,
    DateOnly? ExpiryDate,
    string? BatchNumber,
    string? DiscrepancyNotes,
    /// <summary>True once ProductId, QuantityReceived, and ExpiryDate are all set — the exact
    /// per-item condition the finalize gate checks. Lets callers show per-item progress without
    /// re-implementing the gate logic client-side.</summary>
    bool IsResolved,
    /// <summary>
    /// Frozen purchase price from the order line (MarketplaceOrderItem.Price) — always available,
    /// unrelated to scan/receive progress (TASK-599, Wave 2).
    /// </summary>
    decimal Price,
    /// <summary>
    /// Reference photo so the employee can visually confirm the physical item before/while
    /// scanning (TASK-599, Wave 2). Once ProductId resolves, this is the client's own catalog
    /// Item.ImageUrl (may be null if that item has no photo — no fallback once scanned). Before
    /// that, it falls back to the order line's linked SupplierItem's primary image (Kind ==
    /// "main", else the lowest SortOrder) — null when neither is available.
    /// </summary>
    string? ReferenceImageUrl,
    /// <summary>
    /// The supplier-shipped batch this sub-row was prefilled from (Phase 3, plan D4). Non-null
    /// means ExpiryDate/BatchNumber/QuantityOrdered arrived from the supplier's allocation and
    /// the employee only has to scan the product and confirm the count. Null on legacy /
    /// module-off orders, where a line still produces exactly one blank receipt item.
    /// </summary>
    Guid? SourceOrderItemBatchId = null);

public record MarketplaceOrderReceiptDto(
    Guid Id,
    Guid MarketplaceOrderId,
    Guid ClientTenantId,
    Guid SupplierTenantId,
    Guid DestinationStoreId,
    string DestinationStoreName,
    /// <summary>"draft" | "received".</summary>
    string Status,
    Guid? CreatedByUserId,
    Guid? ReceivedByUserId,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<MarketplaceOrderReceiptItemDto> Items);

/// <summary>
/// Per-item scan/count update (endpoint d, ADR-033 Decision 5) — one PUT per physical item,
/// not a bulk payload (deliberate deviation from ReceiptsController's UpdateItemsRequest shape,
/// matching the mobile scan-one-commit-one UX). Field semantics mirror
/// Receipts.Dtos.UpdateItemPayload exactly: QuantityReceived/DiscrepancyNotes overwrite directly
/// (omit = clear), ProductId/ExpiryDate/BatchNumber merge with the existing value when omitted
/// (send null to leave alone, not to clear).
/// </summary>
public record UpdateMarketplaceOrderReceiptItemRequest(
    Guid? ProductId,
    decimal? QuantityReceived,
    DateOnly? ExpiryDate,
    string? BatchNumber,
    string? DiscrepancyNotes);

// ── Supplier support tickets ─────────────────────────────────────────────────

public record CreateSupportTicketDto(string Subject, string Message);

public record SupportTicketMessageDto(
    Guid Id,
    Guid TicketId,
    Guid SenderTenantId,
    Guid SenderUserId,
    string Body,
    bool IsRead,
    DateTimeOffset CreatedAt);

/// <summary>
/// Ticket summary/detail. Messages is null in list responses and populated in
/// single-ticket (GetTicket) responses, oldest first.
/// </summary>
public record SupplierSupportTicketDto(
    Guid Id,
    Guid SupplierTenantId,
    Guid ClientTenantId,
    string SupplierName,
    string ClientName,
    string Subject,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SupportTicketMessageDto>? Messages = null,
    /// <summary>
    /// Resolved order number (MarketplaceOrder.OrderNumber) when this ticket was auto-opened from
    /// a receipt discrepancy (TASK-599, Wave 2). Null for a regular, manually-opened ticket.
    /// </summary>
    string? OrderNumber = null);

public record AddSupportTicketMessageDto(string Body);

public record UpdateSupportTicketStatusDto(string Status);
