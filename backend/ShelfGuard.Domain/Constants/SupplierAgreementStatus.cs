namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Lifecycle of a supplier↔client cooperation agreement (TASK-316).
/// pending → approved → awaiting_signature → active; pending → rejected;
/// active → terminated. Only rejected/terminated agreements free the
/// (supplier, client) pair for a new request (partial unique index).
/// </summary>
public static class SupplierAgreementStatus
{
    public const string Pending           = "pending";
    public const string Approved          = "approved";
    public const string Rejected          = "rejected";
    public const string AwaitingSignature = "awaiting_signature";
    public const string Active            = "active";
    public const string Terminated        = "terminated";

    public static readonly string[] All =
    [
        Pending, Approved, Rejected, AwaitingSignature, Active, Terminated,
    ];
}
