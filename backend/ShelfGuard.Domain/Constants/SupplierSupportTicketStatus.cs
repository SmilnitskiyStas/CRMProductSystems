namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Lifecycle of a support ticket opened by a client tenant to a supplier
/// tenant (TASK-316). Distinct from platform support tickets (client → provider).
/// </summary>
public static class SupplierSupportTicketStatus
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
