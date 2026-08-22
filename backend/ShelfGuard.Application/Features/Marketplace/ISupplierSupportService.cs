using ShelfGuard.Application.Features.Marketplace.Dtos;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Client → supplier support tickets (TASK-317). Unlike marketplace orders,
/// tickets require NO cooperation agreement — підтримка/консультація відкрита
/// всім. Distinct from platform support tickets (client → SaaS provider).
/// </summary>
public interface ISupplierSupportService
{
    // ── Client side ───────────────────────────────────────────────────────────

    Task<(SupplierSupportTicketDto? Ticket, string? Error)> CreateTicketAsync(
        Guid clientTenantId, Guid supplierId, CreateSupportTicketDto request, Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// System-originated ticket (TASK-599, Wave 2) — e.g. auto-opened by
    /// MarketplaceOrderReceiptService.ReceiveAsync when a receipt has discrepancy notes. Unlike
    /// <see cref="CreateTicketAsync"/>, both tenant ids are already known to the caller (no
    /// catalog-supplierId → tenant resolution needed), and <paramref name="actingUserId"/> stands
    /// in for "the system" on both the ticket's CreatedByUserId and the first message's
    /// SenderUserId (SupplierSupportTicketMessage.SenderUserId has no true system/null option).
    ///
    /// Deliberately does NOT call SaveChangesAsync itself — the caller (running this inside an
    /// ITenantSessionOverride block pointed at supplierTenantId) is expected to flush together
    /// with whatever else it writes in that same override, so the ticket and its side effects
    /// commit as one unit. Only adds the ticket+message to the shared DbContext's change tracker.
    /// </summary>
    Task<SupplierSupportTicketDto> CreateSystemTicketAsync(
        Guid clientTenantId, Guid supplierTenantId, Guid marketplaceOrderId,
        string subject, string body, Guid actingUserId, CancellationToken ct = default);

    Task<IReadOnlyList<SupplierSupportTicketDto>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default);

    // ── Both parties (party check inside) ─────────────────────────────────────

    /// <summary>Ticket with messages (oldest first). Caller must be one of the two parties.</summary>
    Task<(SupplierSupportTicketDto? Ticket, string? Error)> GetTicketAsync(
        Guid ticketId, Guid callerTenantId, CancellationToken ct = default);

    Task<(SupportTicketMessageDto? Message, string? Error)> AddMessageAsync(
        Guid ticketId, Guid callerTenantId, Guid callerUserId, string body,
        CancellationToken ct = default);

    // ── Supplier side ─────────────────────────────────────────────────────────

    Task<IReadOnlyList<SupplierSupportTicketDto>> ListForSupplierAsync(
        Guid supplierTenantId, CancellationToken ct = default);

    /// <summary>Status ∈ open | in_progress | resolved | closed.</summary>
    Task<(SupplierSupportTicketDto? Ticket, string? Error)> UpdateStatusAsync(
        Guid supplierTenantId, Guid ticketId, string status, CancellationToken ct = default);
}
