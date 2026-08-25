namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Lifecycle of a support ticket opened by a consumer
/// (<see cref="Entities.ConsumerAccount"/>) to a tenant (TASK-613). Distinct from
/// <see cref="SupplierSupportTicketStatus"/> (tenant → supplier) and platform support
/// tickets (client → provider) — same string values, three unrelated relationships.
/// </summary>
public static class ConsumerSupportTicketStatus
{
    public const string Open       = "open";
    public const string InProgress = "in_progress";
    public const string Resolved   = "resolved";
    public const string Closed     = "closed";

    public static readonly string[] All =
    [
        Open, InProgress, Resolved, Closed,
    ];
}
