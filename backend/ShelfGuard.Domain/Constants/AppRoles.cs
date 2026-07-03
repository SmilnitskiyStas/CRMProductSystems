namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Role name constants. Must match the values stored in users.role and emitted in JWT claims.
/// Hierarchy (highest → lowest): Provider > EnterpriseAdmin > NetworkManager > StoreManager > Merchandiser / Storekeeper > Cashier
/// </summary>
public static class AppRoles
{
    public const string Provider        = "provider";
    public const string ProviderAdmin   = "provider_admin";
    public const string ProviderAgent   = "provider_agent";
    public const string EnterpriseAdmin = "enterprise_admin";
    public const string NetworkManager  = "network_manager";
    public const string StoreManager    = "store_manager";
    public const string Merchandiser    = "merchandiser";
    public const string Storekeeper     = "storekeeper";
    public const string Cashier         = "cashier";
    /// <summary>v4.1 (ADR-016): self-service supplier tenant admin. Access limited to /api/supplier-cabinet — not part of any tenant-staff policy.</summary>
    public const string SupplierAdmin   = "supplier_admin";

    public static readonly IReadOnlySet<string> ProviderTeamRoles =
        new HashSet<string> { Provider, ProviderAdmin, ProviderAgent };

    /// <summary>All roles that can be assigned to a user, ordered highest to lowest privilege.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Provider, ProviderAdmin, ProviderAgent,
        EnterpriseAdmin, NetworkManager, StoreManager, Merchandiser, Storekeeper, Cashier,
        SupplierAdmin,
    };
}
