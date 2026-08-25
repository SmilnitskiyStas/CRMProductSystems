using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Features.CustomerSupport;
using ShelfGuard.Application.Features.CustomerSupport.Dtos;

namespace ShelfGuard.Infrastructure.Realtime;

/// <summary>
/// <see cref="IConsumerSupportRealtimeNotifier"/> backed by
/// <c>IHubContext&lt;ConsumerSupportHub&gt;</c> (TASK-625) — the one place in the codebase that
/// touches <c>IHubContext</c> for this feature, so <c>ConsumerSupportService</c> stays free of a
/// direct SignalR dependency.
///
/// Both methods swallow and log their own exceptions rather than letting them propagate: per
/// the interface doc, a publish failure must never turn an already-committed REST write into a
/// failed HTTP response — SignalR here is a delivery convenience, not the system of record
/// (spec §5). The message/status payload shape is a plain anonymous object; SignalR's default
/// JSON Hub Protocol serializes with the same camelCase <c>System.Text.Json</c> convention the
/// REST API already uses, so the wire payload matches the API contract's documented shape
/// without a dedicated envelope DTO.
/// </summary>
public sealed class ConsumerSupportRealtimeNotifier : IConsumerSupportRealtimeNotifier
{
    private readonly IHubContext<ConsumerSupportHub> _hub;
    private readonly ILogger<ConsumerSupportRealtimeNotifier> _logger;

    public ConsumerSupportRealtimeNotifier(
        IHubContext<ConsumerSupportHub> hub, ILogger<ConsumerSupportRealtimeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task MessageCreatedAsync(
        Guid ticketId, ConsumerSupportTicketMessageDto message, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.Group(ConsumerSupportGroups.TicketGroup(ticketId))
                .SendAsync("SupportMessageCreated", new { ticketId, message }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish SupportMessageCreated for ticket {TicketId}, message {MessageId}.",
                ticketId, message.Id);
        }
    }

    public async Task StatusChangedAsync(
        Guid ticketId, string status, DateTimeOffset updatedAt, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.Group(ConsumerSupportGroups.TicketGroup(ticketId))
                .SendAsync("SupportTicketStatusChanged", new { ticketId, status, updatedAt }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish SupportTicketStatusChanged for ticket {TicketId} (status {Status}).",
                ticketId, status);
        }
    }
}
