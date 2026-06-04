namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Role name constants. Must match the values stored in users.role and emitted in JWT claims.
/// Hierarchy (highest → lowest): Provider > EnterpriseAdmin > NetworkManager > StoreManager > Merchandiser / Storekeeper > Cashier
/// </summary>
public static class AppRoles
{
    public const string Provider        = "provider";
    public const string EnterpriseAdmin = "enterprise_admin";
    public const string NetworkManager  = "network_manager";
    public const string StoreManager    = "store_manager";
    public const string Merchandiser    = "merchandiser";
    public const string Storekeeper     = "storekeeper";
    public const string Cashier         = "cashier";

    /// <summary>All roles that can be assigned to a user, ordered highest to lowest privilege.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Provider, EnterpriseAdmin, NetworkManager, StoreManager, Merchandiser, Storekeeper, Cashier,
    };
}
