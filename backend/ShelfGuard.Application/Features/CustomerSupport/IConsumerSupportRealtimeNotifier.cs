using ShelfGuard.Application.Features.CustomerSupport.Dtos;

namespace ShelfGuard.Application.Features.CustomerSupport;

/// <summary>
/// Publishes realtime SignalR events for consumer support tickets (TASK-625) so that
/// <see cref="ConsumerSupportService"/> never depends on
/// <c>Microsoft.AspNetCore.SignalR.IHubContext</c> directly — CLAUDE.md's "AI integrations are
/// isolated" layering spirit, applied here to SignalR: the concrete implementation
/// (<c>ConsumerSupportRealtimeNotifier</c>, backed by
/// <c>IHubContext&lt;ConsumerSupportHub&gt;</c>) lives in
/// <c>ShelfGuard.Infrastructure/Realtime</c>; this interface is all the Application layer knows
/// about.
///
/// Both methods are strictly post-commit, best-effort notifications — never the system of
/// record (spec §5: "backend не повинен вважати SignalR гарантованим сховищем повідомлень"). A
/// failure to publish (no connected clients, transient Hub error, etc.) must never turn an
/// already-committed REST write into a failed HTTP response, so the concrete implementation
/// swallows and logs its own exceptions — callers may await these without an extra try/catch.
/// Call both only AFTER the triggering <c>SaveChangesAsync</c> has returned successfully.
/// </summary>
public interface IConsumerSupportRealtimeNotifier
{
    /// <summary>
    /// Publishes <c>SupportMessageCreated</c> to SignalR group
    /// <c>consumer-support-ticket:{ticketId}</c>. <paramref name="message"/> must be the exact
    /// same <see cref="ConsumerSupportTicketMessageDto"/> already returned in the triggering
    /// HTTP response — <c>message.Id</c> is how the mobile client de-duplicates an event that
    /// may arrive back to the sender itself.
    /// </summary>
    Task MessageCreatedAsync(
        Guid ticketId, ConsumerSupportTicketMessageDto message, CancellationToken ct = default);

    /// <summary>
    /// Publishes <c>SupportTicketStatusChanged</c> to the same group. <paramref name="status"/>
    /// is one of <see cref="ShelfGuard.Domain.Constants.ConsumerSupportTicketStatus"/>.
    /// </summary>
    Task StatusChangedAsync(
        Guid ticketId, string status, DateTimeOffset updatedAt, CancellationToken ct = default);
}
