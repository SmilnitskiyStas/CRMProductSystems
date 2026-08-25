using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Authorization;

namespace ShelfGuard.Infrastructure.Realtime;

/// <summary>
/// Realtime transport for consumer↔tenant support ticket threads (TASK-625), sitting on top of
/// the REST channel TASK-616 built (<c>ConsumerSupportController</c>/
/// <c>CustomerSupportInboxController</c>). SignalR delivers events only — message creation
/// stays exclusively on the REST POST endpoints (see <see cref="IConsumerSupportRealtimeNotifier"/>
/// for the publish side); this Hub's only job is group membership (<see cref="JoinTicket"/>/
/// <see cref="LeaveTicket"/>) gated by the caller's own JWT-derived identity.
///
/// <c>[Authorize]</c> with no policy — deliberately accepts BOTH a consumer session
/// (<c>consumer_account_id</c> claim, role "consumer") and a staff session (<c>tenant_id</c> +
/// role claim), same as <c>ConsumerSupportController</c>'s own bare <c>[Authorize]</c> — the
/// specific-identity check happens in <see cref="JoinTicket"/>, not at the class level, because
/// the two caller kinds need two different ownership checks.
///
/// Mapped at <c>/api/hubs/consumer-support</c> in <c>Program.cs</c> — see that file's
/// <c>OnMessageReceived</c> JWT event for how the SignalR JS/mobile client's
/// <c>?access_token=</c> query-string fallback is restricted to exactly this path.
/// </summary>
[Authorize]
public sealed class ConsumerSupportHub : Hub
{
    private readonly IConsumerSupportTicketRepository _tickets;
    private readonly ILogger<ConsumerSupportHub> _logger;

    public ConsumerSupportHub(IConsumerSupportTicketRepository tickets, ILogger<ConsumerSupportHub> logger)
    {
        _tickets = tickets;
        _logger = logger;
    }

    /// <summary>
    /// Adds this connection to <c>consumer-support-ticket:{ticketId}</c> — but only after
    /// verifying the caller actually owns (consumer) or is staff of (tenant) this ticket.
    /// <paramref name="ticketId"/> is the only client-supplied input; ownership is always
    /// resolved from the JWT (<see cref="ResolveConsumerAccountId"/>/<see cref="ResolveTenantId"/>)
    /// plus the ticket's own <see cref="Domain.Entities.ConsumerSupportTicket.ConsumerAccountId"/>/
    /// <see cref="Domain.Entities.ConsumerSupportTicket.TenantId"/> — never trusted from the
    /// caller, per spec §2. A denied caller is never added to the group and gets a
    /// <see cref="HubException"/> instead (SignalR's standard way to surface an application-level
    /// error back to the client without tearing down the connection).
    /// </summary>
    public async Task JoinTicket(Guid ticketId)
    {
        var (allowed, reason) = await CanAccessTicketAsync(ticketId);
        if (!allowed)
        {
            _logger.LogWarning(
                "SignalR JoinTicket denied — connection {ConnectionId}, ticket {TicketId}: {Reason}",
                Context.ConnectionId, ticketId, reason);
            throw new HubException("Access denied.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ConsumerSupportGroups.TicketGroup(ticketId));
    }

    /// <summary>
    /// Explicit group exit for a dialog the user closed while still connected. Disconnect
    /// already drops every group membership automatically (spec §2) — this exists purely for
    /// "still connected, just navigated away from this ticket" so a long-lived mobile connection
    /// doesn't keep accumulating group memberships for every ticket ever opened in the session.
    /// No ownership check needed: removing a connection from a group it doesn't happen to be in
    /// is a harmless no-op in SignalR.
    /// </summary>
    public Task LeaveTicket(Guid ticketId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ConsumerSupportGroups.TicketGroup(ticketId));

    // ── Access control ───────────────────────────────────────────────────────

    private async Task<(bool Allowed, string Reason)> CanAccessTicketAsync(Guid ticketId)
    {
        var consumerAccountId = ResolveConsumerAccountId();
        var tenantId = ResolveTenantId();

        if (consumerAccountId is null && tenantId is null)
            return (false, "Connection carries neither a consumer_account_id nor a tenant_id claim.");

        var ticket = await _tickets.GetByIdAsync(ticketId);
        if (ticket is null)
            return (false, "Ticket not found.");

        // Consumer session: must be the ticket's own opener.
        if (consumerAccountId is Guid cid)
        {
            return ticket.ConsumerAccountId == cid
                ? (true, string.Empty)
                : (false, "Ticket belongs to a different consumer.");
        }

        // Staff session: same authorization floor as CustomerSupportInboxController
        // (AppPolicies.AtLeastStoreManager) — a cashier/storekeeper/merchandiser token is valid
        // JWT-wise but has no REST access to this inbox, and the Hub must not grant a wider
        // surface than REST already does. AtLeastStoreManagerRoles is `internal` (same assembly).
        var tid = tenantId!.Value;
        var hasFloorRole = AppPolicies.AtLeastStoreManagerRoles.Any(role => Context.User!.IsInRole(role));
        if (!hasFloorRole)
            return (false, "Staff role below the AtLeastStoreManager support-inbox floor.");

        return ticket.TenantId == tid
            ? (true, string.Empty)
            : (false, "Ticket belongs to a different tenant.");
    }

    /// <summary>Mirrors ConsumerSupportController.ResolveConsumerAccountId exactly.</summary>
    private Guid? ResolveConsumerAccountId()
    {
        var claim = Context.User?.FindFirst("consumer_account_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>Mirrors CustomerSupportInboxController.ResolveTenantId exactly.</summary>
    private Guid? ResolveTenantId()
    {
        var claim = Context.User?.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? id : null;
    }
}
