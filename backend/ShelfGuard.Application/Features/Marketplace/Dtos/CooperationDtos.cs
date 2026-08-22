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
/// own Item catalog (TASK-597): null/"auto" (default) — auto-provision a new Item unless a
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
/// catalog (TASK-597). Frontend renders this as a "this barcode already exists on item X"
/// comparison card and asks the user to choose a CatalogAction ("link" or "create_new") before
/// resubmitting the order. An empty conflicts list is safe to submit as-is.
/// </summary>
public record MarketplaceOrderConflictDto(
    Guid SupplierItemId,
    MarketplaceOrderConflictingItemDto ExistingItem);

public record MarketplaceOrderItemDto(
    Guid Id,
    Guid? SupplierItemId,
    string ItemName,
    string? Unit,
    decimal Price,
    decimal Qty,
    decimal LineTotal);

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
    IReadOnlyList<MarketplaceOrderItemDto> Items,
    /// <summary>
    /// Read-only (TASK-586, ADR-033 Decision 2). Nullable — orders placed before this column
    /// existed have no value and can never be received through the new client-confirmation flow.
    /// </summary>
    Guid? DestinationStoreId = null);

public record CancelMarketplaceOrderDto(string Reason);

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
    bool IsResolved);

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
    IReadOnlyList<SupportTicketMessageDto>? Messages = null);

public record AddSupportTicketMessageDto(string Body);

public record UpdateSupportTicketStatusDto(string Status);
