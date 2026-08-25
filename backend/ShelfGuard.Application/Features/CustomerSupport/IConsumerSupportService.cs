using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.CustomerSupport.Dtos;

namespace ShelfGuard.Application.Features.CustomerSupport;

/// <summary>
/// Async support-ticket channel from mobile-app consumers to tenant staff (TASK-616) — a
/// simple ticket-header + append-only-message-thread, same shape as
/// <c>ISupplierSupportService</c> but for consumer↔tenant instead of tenant↔supplier. Distinct
/// from ServiceDesk (tenant↔SaaS-provider) and the live chat feature — no real-time transport
/// here, just create/list/reply/status-change.
///
/// Consumer-facing methods and staff-facing methods share the return-tuple convention used by
/// <c>LoyaltyService</c>/<c>ConsumerProfileService</c>: <c>(Dto?, string? Error, int?
/// StatusCode)</c>. Method names are deliberately NOT shared 1:1 between the two sides (e.g.
/// <see cref="GetTicketAsync"/> vs. <see cref="GetTicketForStaffAsync"/>) — both would otherwise
/// have the identical (Guid, Guid, CancellationToken) signature, which C# cannot overload on.
/// </summary>
public interface IConsumerSupportService
{
    // ── Consumer side ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a ticket + its first message in one commit. Best-effort auto-links
    /// <c>CustomerId</c> to the tenant's own CRM record for this consumer — see
    /// <c>ConsumerSupportService</c>'s resolution helper for exactly which existing lookups it
    /// reuses (no new linking mechanism).
    /// </summary>
    Task<(ConsumerSupportTicketDto? Ticket, string? Error, int? StatusCode)> CreateTicketAsync(
        Guid consumerAccountId, Guid tenantId, string subject, string body, CancellationToken ct = default);

    /// <summary>This consumer's own tickets at one tenant, newest first, paged (no messages).</summary>
    Task<PagedResult<ConsumerSupportTicketDto>> GetMyTicketsAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>A single own ticket with its full message thread. 404 both when the ticket
    /// doesn't exist and when it belongs to a different consumer (never discloses which).</summary>
    Task<(ConsumerSupportTicketDto? Ticket, string? Error, int? StatusCode)> GetTicketAsync(
        Guid consumerAccountId, Guid ticketId, CancellationToken ct = default);

    /// <summary>
    /// Appends a consumer follow-up message. If the ticket was Resolved/Closed, this also
    /// reopens it to Open — see the implementation's remarks for the rationale.
    /// </summary>
    Task<(ConsumerSupportTicketMessageDto? Message, string? Error, int? StatusCode)> AddConsumerMessageAsync(
        Guid consumerAccountId, Guid ticketId, string body, CancellationToken ct = default);

    // ── Staff side ────────────────────────────────────────────────────────────

    /// <summary>Every ticket at this tenant, newest first, optionally filtered by status, paged (no messages).</summary>
    Task<PagedResult<ConsumerSupportTicketDto>> GetInboxAsync(
        Guid tenantId, string? status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// A single ticket with its full message thread, staff view. Side effect: marks every
    /// unread consumer message on the ticket as read.
    /// </summary>
    Task<(ConsumerSupportTicketDto? Ticket, string? Error, int? StatusCode)> GetTicketForStaffAsync(
        Guid tenantId, Guid ticketId, CancellationToken ct = default);

    Task<(ConsumerSupportTicketMessageDto? Message, string? Error, int? StatusCode)> AddStaffReplyAsync(
        Guid tenantId, Guid ticketId, Guid staffUserId, string body, CancellationToken ct = default);

    /// <summary>Status ∈ open | in_progress | resolved | closed.</summary>
    Task<(ConsumerSupportTicketDto? Ticket, string? Error, int? StatusCode)> UpdateStatusAsync(
        Guid tenantId, Guid ticketId, Guid staffUserId, string newStatus, CancellationToken ct = default);
}
