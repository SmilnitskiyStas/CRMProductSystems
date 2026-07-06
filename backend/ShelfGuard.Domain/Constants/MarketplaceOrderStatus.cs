namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Lifecycle of a B2B marketplace order placed by a client tenant with a
/// supplier tenant under an active cooperation agreement (TASK-316).
/// </summary>
public static class MarketplaceOrderStatus
{
    public const string New       = "new";
    public const string Confirmed = "confirmed";
    public const string Shipped   = "shipped";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";

    public static readonly string[] All =
    [
        New, Confirmed, Shipped, Delivered, Cancelled,
    ];
}
