namespace ShelfGuard.Infrastructure.Realtime;

/// <summary>
/// SignalR group naming for <see cref="ConsumerSupportHub"/> (TASK-625) — one group per ticket,
/// so a <c>SupportMessageCreated</c>/<c>SupportTicketStatusChanged</c> event reaches only
/// connections that explicitly called <c>JoinTicket</c> on that specific ticket (the consumer
/// who opened it, plus whichever staff members currently have it open) — never a tenant-wide or
/// consumer-wide broadcast. Shared between the Hub itself (Join/LeaveTicket) and
/// <see cref="ConsumerSupportRealtimeNotifier"/> (publish) so the name can never drift between
/// the two sides.
/// </summary>
internal static class ConsumerSupportGroups
{
    public static string TicketGroup(Guid ticketId) => $"consumer-support-ticket:{ticketId:D}";
}
