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

/// <summary>Client → supplier cooperation request body.</summary>
public record SubmitCooperationRequestDto(string? Message);

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

public record CreateMarketplaceOrderItemDto(Guid SupplierItemId, decimal Qty);

public record CreateMarketplaceOrderDto(
    List<CreateMarketplaceOrderItemDto> Items,
    string? Comment);

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
    IReadOnlyList<MarketplaceOrderItemDto> Items);

public record CancelMarketplaceOrderDto(string Reason);

/// <summary>Supplier-side status change. Reason is required when Status = cancelled.</summary>
public record UpdateMarketplaceOrderStatusDto(string Status, string? Reason = null);

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
