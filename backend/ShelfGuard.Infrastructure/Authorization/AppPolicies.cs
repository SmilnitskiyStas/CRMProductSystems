using Microsoft.AspNetCore.Authorization;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Named authorization policy constants and their role assignments.
/// Source of truth: v1-spec.md section 3.2 + v3-spec.md Menu RBAC (TASK-075/076).
///
/// Policy                  | Allowed roles
/// ------------------------|---------------------------------------------------------------
/// ProviderOnly            | provider
/// AtLeastEnterpriseAdmin  | provider, enterprise_admin
/// AtLeastNetworkManager   | + network_manager
/// AtLeastStoreManager     | + store_manager
/// CanReceiveStock         | + storekeeper  (not merchandiser, not cashier)
/// CanViewStock            | all staff roles (except cashier)
/// CanViewAnalytics        | store_manager and above
/// CanAccessPos            | cashier + storekeeper + store_manager + network_manager + enterprise_admin
/// CanManageStore          | store_manager + network_manager + enterprise_admin
/// CanViewNetworkAnalytics | network_manager + enterprise_admin
/// </summary>
public static class AppPolicies
{
    // Policy name constants (used on [Authorize(Policy = ...)] attributes)
    public const string ProviderOnly            = "ProviderOnly";
    public const string AtLeastEnterpriseAdmin  = "AtLeastEnterpriseAdmin";
    public const string AtLeastNetworkManager   = "AtLeastNetworkManager";
    public const string AtLeastStoreManager     = "AtLeastStoreManager";
    public const string CanReceiveStock         = "CanReceiveStock";
    public const string CanViewStock            = "CanViewStock";
    public const string CanViewAnalytics        = "CanViewAnalytics";
    public const string CanAccessPos            = "CanAccessPos";
    public const string CanManageStore          = "CanManageStore";
    public const string CanViewNetworkAnalytics = "CanViewNetworkAnalytics";

    // Role sets per policy — the single source of truth referenced by both registration and tests.
    internal static readonly string[] ProviderOnlyRoles =
        [AppRoles.Provider];

    internal static readonly string[] AtLeastEnterpriseAdminRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin];

    internal static readonly string[] AtLeastNetworkManagerRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager];

    internal static readonly string[] AtLeastStoreManagerRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager, AppRoles.StoreManager];

    // Receipts and transfers: storekeeper can receive/move stock; merchandiser and cashier cannot
    internal static readonly string[] CanReceiveStockRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager, AppRoles.StoreManager, AppRoles.Storekeeper];

    // View stock / add batch: all staff roles except cashier
    internal static readonly string[] CanViewStockRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager, AppRoles.StoreManager, AppRoles.Merchandiser, AppRoles.Storekeeper];

    // Analytics: managers and above (v1-spec.md §3.2)
    internal static readonly string[] CanViewAnalyticsRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager, AppRoles.StoreManager];

    // POS / shifts / sales: cashier + warehouse staff + managers (v3-spec.md TASK-075)
    internal static readonly string[] CanAccessPosRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager, AppRoles.StoreManager, AppRoles.Storekeeper, AppRoles.Cashier];

    // Store management: store_manager and above — no cashier / storekeeper / merchandiser
    internal static readonly string[] CanManageStoreRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager, AppRoles.StoreManager];

    // Network-level analytics: network_manager and above
    internal static readonly string[] CanViewNetworkAnalyticsRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager];

    /// <summary>
    /// Registers all named policies into the AuthorizationOptions.
    /// Call: services.AddAuthorization(AppPolicies.Configure)
    /// </summary>
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(ProviderOnly,            p => p.RequireRole(ProviderOnlyRoles));
        options.AddPolicy(AtLeastEnterpriseAdmin,  p => p.RequireRole(AtLeastEnterpriseAdminRoles));
        options.AddPolicy(AtLeastNetworkManager,   p => p.RequireRole(AtLeastNetworkManagerRoles));
        options.AddPolicy(AtLeastStoreManager,     p => p.RequireRole(AtLeastStoreManagerRoles));
        options.AddPolicy(CanReceiveStock,         p => p.RequireRole(CanReceiveStockRoles));
        options.AddPolicy(CanViewStock,            p => p.RequireRole(CanViewStockRoles));
        options.AddPolicy(CanViewAnalytics,        p => p.RequireRole(CanViewAnalyticsRoles));
        options.AddPolicy(CanAccessPos,            p => p.RequireRole(CanAccessPosRoles));
        options.AddPolicy(CanManageStore,          p => p.RequireRole(CanManageStoreRoles));
        options.AddPolicy(CanViewNetworkAnalytics, p => p.RequireRole(CanViewNetworkAnalyticsRoles));
    }
}
