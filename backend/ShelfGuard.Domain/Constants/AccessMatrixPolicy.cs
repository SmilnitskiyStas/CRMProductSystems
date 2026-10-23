namespace ShelfGuard.Domain.Constants;

public static class AccessMatrixPolicy
{
    public static readonly IReadOnlySet<string> MandatoryMfaRoles = new HashSet<string>(StringComparer.Ordinal)
    { AppRoles.Provider, AppRoles.ProviderAdmin, AppRoles.ProviderAgent, AppRoles.EnterpriseAdmin,
      AppRoles.NetworkManager, AppRoles.StoreManager, AppRoles.SupplierAdmin };

    public static readonly IReadOnlySet<string> PrivilegedPermissions = new HashSet<string>(StringComparer.Ordinal)
    { TenantRoleCapabilities.UsersManage, TenantRoleCapabilities.IntegrationsManage,
      TenantRoleCapabilities.ProductionManage, TenantRoleCapabilities.ProductionComplete, TenantRoleCapabilities.ProductionCancel,
      TenantRoleCapabilities.MarketingAnalyticsExportPii, TenantRoleCapabilities.ConsumerAppManage,
      TenantUserPermissions.LegalEntitiesManage, SupplierPermissions.StaffManagement, SupplierPermissions.ProfileManagement };
}
