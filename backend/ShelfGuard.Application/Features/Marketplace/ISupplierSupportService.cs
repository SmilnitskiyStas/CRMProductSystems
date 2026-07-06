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
