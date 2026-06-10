using Microsoft.AspNetCore.Authorization;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Named authorization policy constants and their role assignments.
/// Source of truth: v1-spec.md section 3.2 permissions matrix.
///
/// Policy       | Allowed roles
/// -------------|---------------------------------------------------------------
/// ProviderOnly | provider
/// AtLeastEnterpriseAdmin | provider, enterprise_admin
/// AtLeastNetworkManager  | + network_manager
/// AtLeastStoreManager    | + store_manager
/// CanReceiveStock        | + storekeeper  (not merchandiser)
/// CanViewStock           | all six staff roles
/// </summary>
public static class AppPolicies
{
    // Policy name constants (used on [Authorize(Policy = ...)] attributes)
    public const string ProviderOnly           = "ProviderOnly";
    public const string AtLeastEnterpriseAdmin = "AtLeastEnterpriseAdmin";
    public const string AtLeastNetworkManager  = "AtLeastNetworkManager";
    public const string AtLeastStoreManager    = "AtLeastStoreManager";
    public const string CanReceiveStock        = "CanReceiveStock";
    public const string CanViewStock           = "CanViewStock";
    public const string CanViewAnalytics       = "CanViewAnalytics";

    // Role sets per policy — the single source of truth referenced by both registration and tests.
    internal static readonly string[] ProviderOnlyRoles =
        [AppRoles.Provider];

    internal static readonly string[] AtLeastEnterpriseAdminRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin];

    internal static readonly string[] AtLeastNetworkManagerRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager];

    internal static readonly string[] AtLeastStoreManagerRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager, AppRoles.StoreManager];

    // Receipts and transfers: storekeeper can receive/move stock; merchandiser cannot
    internal static readonly string[] CanReceiveStockRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager, AppRoles.StoreManager, AppRoles.Storekeeper];

    // View stock / add batch: all staff roles
    internal static readonly string[] CanViewStockRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager, AppRoles.StoreManager, AppRoles.Merchandiser, AppRoles.Storekeeper];

    // Analytics: managers and above (v1-spec.md §3.2)
    internal static readonly string[] CanViewAnalyticsRoles =
        [AppRoles.Provider, AppRoles.EnterpriseAdmin, AppRoles.NetworkManager, AppRoles.StoreManager];

    /// <summary>
    /// Registers all named policies into the AuthorizationOptions.
    /// Call: services.AddAuthorization(AppPolicies.Configure)
    /// </summary>
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(ProviderOnly,           p => p.RequireRole(ProviderOnlyRoles));
        options.AddPolicy(AtLeastEnterpriseAdmin, p => p.RequireRole(AtLeastEnterpriseAdminRoles));
        options.AddPolicy(AtLeastNetworkManager,  p => p.RequireRole(AtLeastNetworkManagerRoles));
        options.AddPolicy(AtLeastStoreManager,    p => p.RequireRole(AtLeastStoreManagerRoles));
        options.AddPolicy(CanReceiveStock,        p => p.RequireRole(CanReceiveStockRoles));
        options.AddPolicy(CanViewStock,           p => p.RequireRole(CanViewStockRoles));
        options.AddPolicy(CanViewAnalytics,       p => p.RequireRole(CanViewAnalyticsRoles));
    }
}
