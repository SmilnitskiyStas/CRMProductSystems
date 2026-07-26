namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Role name constants. Must match the values stored in users.role and emitted in JWT claims.
/// Hierarchy (highest → lowest): Provider > EnterpriseAdmin > NetworkManager > StoreManager > Merchandiser / Storekeeper > Cashier > Staff
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
    /// <summary>
    /// v4.5 (ADR-020): minimal base tier, rank 0 — below Cashier. For users whose job is
    /// entirely described by an assigned TenantRole capability template (HR, accountant,
    /// purchasing) with no operational (POS/stock) needs. Grants nothing beyond bare auth
    /// by itself — not added to any existing AppPolicies role array; access comes only
    /// from the user's TenantRoleId capabilities.
    /// </summary>
    public const string Staff           = "staff";
    /// <summary>v4.1 (ADR-016): self-service supplier tenant admin. Access limited to /api/supplier-cabinet — not part of any tenant-staff policy.</summary>
    public const string SupplierAdmin   = "supplier_admin";

    /// <summary>
    /// TASK-405 (Loyalty Фаза 0): end-user of the consumer loyalty wallet, authenticated
    /// against <see cref="Entities.ConsumerAccount"/> — never a <see cref="Entities.User"/>
    /// row. Emitted only in a consumer-session JWT's role claim (no tenant_id claim
    /// alongside it). Deliberately NOT part of <see cref="All"/> (which enumerates roles a
    /// staff <see cref="Entities.User"/> can be assigned) or any tenant-staff
    /// <c>AppPolicies</c> role array — a consumer session must never pass a staff
    /// role/policy check.
    /// </summary>
    public const string Consumer        = "consumer";

    public static readonly IReadOnlySet<string> ProviderTeamRoles =
        new HashSet<string> { Provider, ProviderAdmin, ProviderAgent };

    /// <summary>All roles that can be assigned to a user, ordered highest to lowest privilege.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Provider, ProviderAdmin, ProviderAgent,
        EnterpriseAdmin, NetworkManager, StoreManager, Merchandiser, Storekeeper, Cashier, Staff,
        SupplierAdmin,
    };
}
