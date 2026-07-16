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
///
/// ADR-020 (TASK-346) — role-OR-capability policies. Each one OR's the SAME role array the
/// controller already enforced (zero behavior change for existing roles) with one
/// TenantRoleCapabilities key, via RoleOrCapabilityRequirement/Handler instead of RequireRole.
///
/// Policy                          | Base roles (unchanged)     | Capability (OR)
/// ---------------------------------|----------------------------|--------------------------
/// SchedulesManageOrCapability      | AtLeastStoreManagerRoles   | schedules.manage
/// AnalyticsViewOrCapability        | CanViewAnalyticsRoles      | analytics.view
/// IntegrationsViewOrCapability     | AtLeastStoreManagerRoles   | integrations.view
/// IntegrationsManageOrCapability   | AtLeastStoreManagerRoles   | integrations.manage
/// OrdersManageOrCapability         | AtLeastStoreManagerRoles   | orders.manage
/// SuppliersViewOrCapability        | AtLeastStoreManagerRoles   | suppliers.view
/// SuppliersManageOrCapability      | AtLeastNetworkManagerRoles | suppliers.manage
/// ReceiptsViewOrCapability         | CanReceiveStockRoles       | receipts.view
/// AiOrdersViewOrCapability         | AtLeastStoreManagerRoles   | ai_orders.view
/// AiOrdersManageOrCapability       | AtLeastStoreManagerRoles   | ai_orders.manage
/// StoreManagerOrUsersManage        | AtLeastStoreManagerRoles   | users.manage
///
/// TASK-352 (Block 1 audit, user decision): Invite/Update/Deactivate on UsersController were
/// split across two floors (Invite/Deactivate = AtLeastEnterpriseAdmin, Update =
/// AtLeastStoreManager) — narrower than v1-spec.md §3.2, which grants "staff management" to
/// network_manager and store_manager too. User decided to widen Invite/Deactivate to match
/// Update and the spec. The former `EnterpriseAdminOrUsersManage` policy (AtLeastEnterpriseAdmin
/// + users.manage) is removed — all three actions now share `StoreManagerOrUsersManage`, so a
/// second, narrower policy for the same capability no longer serves a purpose.
/// </summary>
public static class AppPolicies
{
    // Policy name constants (used on [Authorize(Policy = ...)] attributes)
    public const string ProviderOnly            = "ProviderOnly";
    public const string ProviderTeamMember      = "ProviderTeamMember";
    public const string ProviderCanInvite       = "ProviderCanInvite";
    public const string AtLeastEnterpriseAdmin  = "AtLeastEnterpriseAdmin";
    public const string AtLeastNetworkManager   = "AtLeastNetworkManager";
    public const string AtLeastStoreManager     = "AtLeastStoreManager";
    public const string CanReceiveStock         = "CanReceiveStock";
    public const string CanViewStock            = "CanViewStock";
    public const string CanViewAnalytics        = "CanViewAnalytics";
    public const string CanAccessPos            = "CanAccessPos";
    public const string CanManageStore          = "CanManageStore";
    public const string CanViewNetworkAnalytics = "CanViewNetworkAnalytics";
    public const string SupplierCabinet         = "SupplierCabinet";

    // ── ADR-020 (TASK-346): role-or-capability policies ──────────────────────
    public const string SchedulesManageOrCapability   = "SchedulesManageOrCapability";
    public const string AnalyticsViewOrCapability      = "AnalyticsViewOrCapability";
    public const string IntegrationsViewOrCapability   = "IntegrationsViewOrCapability";
    public const string IntegrationsManageOrCapability = "IntegrationsManageOrCapability";
    public const string OrdersManageOrCapability        = "OrdersManageOrCapability";
    public const string SuppliersViewOrCapability       = "SuppliersViewOrCapability";
    public const string SuppliersManageOrCapability     = "SuppliersManageOrCapability";
    public const string ReceiptsViewOrCapability        = "ReceiptsViewOrCapability";
    public const string AiOrdersViewOrCapability        = "AiOrdersViewOrCapability";
    public const string AiOrdersManageOrCapability      = "AiOrdersManageOrCapability";

    /// <summary>
    /// UsersController's "users.manage" capability unlocks Invite/Update/Deactivate. All three
    /// actions share this one floor (AtLeastStoreManagerRoles) — TASK-352 widened
    /// Invite/Deactivate from the previous, narrower AtLeastEnterpriseAdmin floor to match
    /// Update and v1-spec.md §3.2 (network_manager/store_manager also manage staff).
    /// </summary>
    public const string StoreManagerOrUsersManage    = "StoreManagerOrUsersManage";

    // Role sets per policy — the single source of truth referenced by both registration and tests.
    internal static readonly string[] ProviderOnlyRoles =
        [AppRoles.Provider];

    internal static readonly string[] ProviderTeamMemberRoles =
        [AppRoles.Provider, AppRoles.ProviderAdmin, AppRoles.ProviderAgent];

    internal static readonly string[] ProviderCanInviteRoles =
        [AppRoles.Provider, AppRoles.ProviderAdmin];

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

    // v4.1 supplier self-service cabinet (ADR-016): supplier_admin ONLY —
    // supplier_admin is deliberately absent from every tenant-staff policy above.
    internal static readonly string[] SupplierCabinetRoles =
        [AppRoles.SupplierAdmin];

    /// <summary>
    /// Registers all named policies into the AuthorizationOptions.
    /// Call: services.AddAuthorization(AppPolicies.Configure)
    /// </summary>
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(ProviderOnly,            p => p.RequireRole(ProviderOnlyRoles));
        options.AddPolicy(ProviderTeamMember,      p => p.RequireRole(ProviderTeamMemberRoles));
        options.AddPolicy(ProviderCanInvite,       p => p.RequireRole(ProviderCanInviteRoles));
        options.AddPolicy(AtLeastEnterpriseAdmin,  p => p.RequireRole(AtLeastEnterpriseAdminRoles));
        options.AddPolicy(AtLeastNetworkManager,   p => p.RequireRole(AtLeastNetworkManagerRoles));
        options.AddPolicy(AtLeastStoreManager,     p => p.RequireRole(AtLeastStoreManagerRoles));
        options.AddPolicy(CanReceiveStock,         p => p.RequireRole(CanReceiveStockRoles));
        options.AddPolicy(CanViewStock,            p => p.RequireRole(CanViewStockRoles));
        options.AddPolicy(CanViewAnalytics,        p => p.RequireRole(CanViewAnalyticsRoles));
        options.AddPolicy(CanAccessPos,            p => p.RequireRole(CanAccessPosRoles));
        options.AddPolicy(CanManageStore,          p => p.RequireRole(CanManageStoreRoles));
        options.AddPolicy(CanViewNetworkAnalytics, p => p.RequireRole(CanViewNetworkAnalyticsRoles));
        options.AddPolicy(SupplierCabinet,         p => p.RequireRole(SupplierCabinetRoles));

        // ── ADR-020 (TASK-346): role-or-capability policies ──────────────────
        options.AddPolicy(SchedulesManageOrCapability, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(AtLeastStoreManagerRoles, TenantRoleCapabilities.SchedulesManage)));
        options.AddPolicy(AnalyticsViewOrCapability, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(CanViewAnalyticsRoles, TenantRoleCapabilities.AnalyticsView)));
        options.AddPolicy(IntegrationsViewOrCapability, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(AtLeastStoreManagerRoles, TenantRoleCapabilities.IntegrationsView)));
        options.AddPolicy(IntegrationsManageOrCapability, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(AtLeastStoreManagerRoles, TenantRoleCapabilities.IntegrationsManage)));
        options.AddPolicy(OrdersManageOrCapability, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(AtLeastStoreManagerRoles, TenantRoleCapabilities.OrdersManage)));
        options.AddPolicy(SuppliersViewOrCapability, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(AtLeastStoreManagerRoles, TenantRoleCapabilities.SuppliersView)));
        options.AddPolicy(SuppliersManageOrCapability, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(AtLeastNetworkManagerRoles, TenantRoleCapabilities.SuppliersManage)));
        options.AddPolicy(ReceiptsViewOrCapability, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(CanReceiveStockRoles, TenantRoleCapabilities.ReceiptsView)));
        options.AddPolicy(AiOrdersViewOrCapability, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(AtLeastStoreManagerRoles, TenantRoleCapabilities.AiOrdersView)));
        options.AddPolicy(AiOrdersManageOrCapability, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(AtLeastStoreManagerRoles, TenantRoleCapabilities.AiOrdersManage)));
        options.AddPolicy(StoreManagerOrUsersManage, p => p.AddRequirements(
            new RoleOrCapabilityRequirement(AtLeastStoreManagerRoles, TenantRoleCapabilities.UsersManage)));
    }
}
