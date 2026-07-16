using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

/// <summary>
/// Verifies that each named policy's allowed-role set matches the v1-spec.md section 3.2 matrix.
/// Tests use the same Configure() method that Program.cs uses — no duplication.
/// </summary>
public sealed class AppPoliciesTests
{
    private readonly AuthorizationOptions _options = new();

    public AppPoliciesTests() => AppPolicies.Configure(_options);

    // ── ProviderOnly ────────────────────────────────────────────────────────

    [Fact]
    public void ProviderOnly_allows_provider()
        => Assert.Contains(AppRoles.Provider, RolesFor(AppPolicies.ProviderOnly));

    [Theory]
    [InlineData(AppRoles.EnterpriseAdmin)]
    [InlineData(AppRoles.NetworkManager)]
    [InlineData(AppRoles.StoreManager)]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Storekeeper)]
    [InlineData(AppRoles.Cashier)]
    public void ProviderOnly_denies_all_other_roles(string role)
        => Assert.DoesNotContain(role, RolesFor(AppPolicies.ProviderOnly));

    // ── AtLeastEnterpriseAdmin ──────────────────────────────────────────────

    [Theory]
    [InlineData(AppRoles.Provider)]
    [InlineData(AppRoles.EnterpriseAdmin)]
    public void AtLeastEnterpriseAdmin_allows_correct_roles(string role)
        => Assert.Contains(role, RolesFor(AppPolicies.AtLeastEnterpriseAdmin));

    [Theory]
    [InlineData(AppRoles.NetworkManager)]
    [InlineData(AppRoles.StoreManager)]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Storekeeper)]
    public void AtLeastEnterpriseAdmin_denies_lower_roles(string role)
        => Assert.DoesNotContain(role, RolesFor(AppPolicies.AtLeastEnterpriseAdmin));

    // ── AtLeastNetworkManager ───────────────────────────────────────────────

    [Theory]
    [InlineData(AppRoles.Provider)]
    [InlineData(AppRoles.EnterpriseAdmin)]
    [InlineData(AppRoles.NetworkManager)]
    public void AtLeastNetworkManager_allows_correct_roles(string role)
        => Assert.Contains(role, RolesFor(AppPolicies.AtLeastNetworkManager));

    [Theory]
    [InlineData(AppRoles.StoreManager)]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Storekeeper)]
    public void AtLeastNetworkManager_denies_lower_roles(string role)
        => Assert.DoesNotContain(role, RolesFor(AppPolicies.AtLeastNetworkManager));

    // ── AtLeastStoreManager ─────────────────────────────────────────────────

    [Theory]
    [InlineData(AppRoles.Provider)]
    [InlineData(AppRoles.EnterpriseAdmin)]
    [InlineData(AppRoles.NetworkManager)]
    [InlineData(AppRoles.StoreManager)]
    public void AtLeastStoreManager_allows_correct_roles(string role)
        => Assert.Contains(role, RolesFor(AppPolicies.AtLeastStoreManager));

    [Theory]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Storekeeper)]
    public void AtLeastStoreManager_denies_merchandiser_and_storekeeper(string role)
        => Assert.DoesNotContain(role, RolesFor(AppPolicies.AtLeastStoreManager));

    // ── CanReceiveStock ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(AppRoles.Provider)]
    [InlineData(AppRoles.EnterpriseAdmin)]
    [InlineData(AppRoles.NetworkManager)]
    [InlineData(AppRoles.StoreManager)]
    [InlineData(AppRoles.Storekeeper)]
    public void CanReceiveStock_allows_correct_roles(string role)
        => Assert.Contains(role, RolesFor(AppPolicies.CanReceiveStock));

    [Theory]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Cashier)]
    public void CanReceiveStock_denies_merchandiser_and_cashier(string role)
        => Assert.DoesNotContain(role, RolesFor(AppPolicies.CanReceiveStock));

    // ── CanViewStock ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AppRoles.Provider)]
    [InlineData(AppRoles.EnterpriseAdmin)]
    [InlineData(AppRoles.NetworkManager)]
    [InlineData(AppRoles.StoreManager)]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Storekeeper)]
    public void CanViewStock_allows_all_staff_roles(string role)
        => Assert.Contains(role, RolesFor(AppPolicies.CanViewStock));

    // ── CanAccessPos ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AppRoles.Provider)]
    [InlineData(AppRoles.EnterpriseAdmin)]
    [InlineData(AppRoles.NetworkManager)]
    [InlineData(AppRoles.StoreManager)]
    [InlineData(AppRoles.Storekeeper)]
    [InlineData(AppRoles.Cashier)]
    public void CanAccessPos_allows_correct_roles(string role)
        => Assert.Contains(role, RolesFor(AppPolicies.CanAccessPos));

    [Fact]
    public void CanAccessPos_denies_merchandiser()
        => Assert.DoesNotContain(AppRoles.Merchandiser, RolesFor(AppPolicies.CanAccessPos));

    // ── CanManageStore ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(AppRoles.Provider)]
    [InlineData(AppRoles.EnterpriseAdmin)]
    [InlineData(AppRoles.NetworkManager)]
    [InlineData(AppRoles.StoreManager)]
    public void CanManageStore_allows_correct_roles(string role)
        => Assert.Contains(role, RolesFor(AppPolicies.CanManageStore));

    [Theory]
    [InlineData(AppRoles.Storekeeper)]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Cashier)]
    public void CanManageStore_denies_lower_roles(string role)
        => Assert.DoesNotContain(role, RolesFor(AppPolicies.CanManageStore));

    // ── CanViewNetworkAnalytics ─────────────────────────────────────────────

    [Theory]
    [InlineData(AppRoles.Provider)]
    [InlineData(AppRoles.EnterpriseAdmin)]
    [InlineData(AppRoles.NetworkManager)]
    public void CanViewNetworkAnalytics_allows_correct_roles(string role)
        => Assert.Contains(role, RolesFor(AppPolicies.CanViewNetworkAnalytics));

    [Theory]
    [InlineData(AppRoles.StoreManager)]
    [InlineData(AppRoles.Storekeeper)]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Cashier)]
    public void CanViewNetworkAnalytics_denies_lower_roles(string role)
        => Assert.DoesNotContain(role, RolesFor(AppPolicies.CanViewNetworkAnalytics));

    // ── SupplierCabinet (v4.1, ADR-016) ─────────────────────────────────────

    [Fact]
    public void SupplierCabinet_allows_only_supplier_admin()
    {
        var roles = RolesFor(AppPolicies.SupplierCabinet).ToList();
        Assert.Equal(new[] { AppRoles.SupplierAdmin }, roles);
    }

    // supplier_admin must be excluded from every tenant-staff policy —
    // it can only reach /api/supplier-cabinet (403 on /api/stock, /api/pos, etc.)
    [Theory]
    [InlineData(AppPolicies.ProviderOnly)]
    [InlineData(AppPolicies.ProviderTeamMember)]
    [InlineData(AppPolicies.AtLeastEnterpriseAdmin)]
    [InlineData(AppPolicies.AtLeastNetworkManager)]
    [InlineData(AppPolicies.AtLeastStoreManager)]
    [InlineData(AppPolicies.CanReceiveStock)]
    [InlineData(AppPolicies.CanViewStock)]
    [InlineData(AppPolicies.CanViewAnalytics)]
    [InlineData(AppPolicies.CanAccessPos)]
    [InlineData(AppPolicies.CanManageStore)]
    [InlineData(AppPolicies.CanViewNetworkAnalytics)]
    public void SupplierAdmin_is_denied_by_all_tenant_staff_policies(string policyName)
        => Assert.DoesNotContain(AppRoles.SupplierAdmin, RolesFor(policyName));

    // ── All policies are registered ─────────────────────────────────────────

    [Theory]
    [InlineData(AppPolicies.ProviderOnly)]
    [InlineData(AppPolicies.AtLeastEnterpriseAdmin)]
    [InlineData(AppPolicies.AtLeastNetworkManager)]
    [InlineData(AppPolicies.AtLeastStoreManager)]
    [InlineData(AppPolicies.CanReceiveStock)]
    [InlineData(AppPolicies.CanViewStock)]
    [InlineData(AppPolicies.CanAccessPos)]
    [InlineData(AppPolicies.CanManageStore)]
    [InlineData(AppPolicies.CanViewNetworkAnalytics)]
    [InlineData(AppPolicies.SupplierCabinet)]
    public void All_policies_are_registered(string policyName)
        => Assert.NotNull(_options.GetPolicy(policyName));

    // ── ADR-020 (TASK-346): role-or-capability policies ─────────────────────
    // Each policy must carry exactly the capability it claims AND the exact pre-existing
    // role array of the action(s) it protects — the whole point is zero regression for
    // every role that isn't relying on the new capability path.

    [Theory]
    [InlineData(AppPolicies.SchedulesManageOrCapability, TenantRoleCapabilities.SchedulesManage)]
    [InlineData(AppPolicies.AnalyticsViewOrCapability, TenantRoleCapabilities.AnalyticsView)]
    [InlineData(AppPolicies.IntegrationsViewOrCapability, TenantRoleCapabilities.IntegrationsView)]
    [InlineData(AppPolicies.IntegrationsManageOrCapability, TenantRoleCapabilities.IntegrationsManage)]
    [InlineData(AppPolicies.OrdersManageOrCapability, TenantRoleCapabilities.OrdersManage)]
    [InlineData(AppPolicies.SuppliersViewOrCapability, TenantRoleCapabilities.SuppliersView)]
    [InlineData(AppPolicies.SuppliersManageOrCapability, TenantRoleCapabilities.SuppliersManage)]
    [InlineData(AppPolicies.ReceiptsViewOrCapability, TenantRoleCapabilities.ReceiptsView)]
    [InlineData(AppPolicies.AiOrdersViewOrCapability, TenantRoleCapabilities.AiOrdersView)]
    [InlineData(AppPolicies.AiOrdersManageOrCapability, TenantRoleCapabilities.AiOrdersManage)]
    [InlineData(AppPolicies.StoreManagerOrUsersManage, TenantRoleCapabilities.UsersManage)]
    public void RoleOrCapabilityPolicy_carries_expected_capability(string policyName, string expectedCapability)
        => Assert.Equal(expectedCapability, RequirementFor(policyName).Capability);

    [Fact]
    public void SchedulesManageOrCapability_base_roles_match_AtLeastStoreManager()
        => AssertSameRoles(AppPolicies.AtLeastStoreManagerRoles, AppPolicies.SchedulesManageOrCapability);

    [Fact]
    public void AnalyticsViewOrCapability_base_roles_match_CanViewAnalytics()
        => AssertSameRoles(AppPolicies.CanViewAnalyticsRoles, AppPolicies.AnalyticsViewOrCapability);

    [Fact]
    public void IntegrationsPolicies_base_roles_match_AtLeastStoreManager()
    {
        AssertSameRoles(AppPolicies.AtLeastStoreManagerRoles, AppPolicies.IntegrationsViewOrCapability);
        AssertSameRoles(AppPolicies.AtLeastStoreManagerRoles, AppPolicies.IntegrationsManageOrCapability);
    }

    [Fact]
    public void OrdersManageOrCapability_base_roles_match_AtLeastStoreManager()
        => AssertSameRoles(AppPolicies.AtLeastStoreManagerRoles, AppPolicies.OrdersManageOrCapability);

    [Fact]
    public void SuppliersViewOrCapability_base_roles_match_AtLeastStoreManager()
        => AssertSameRoles(AppPolicies.AtLeastStoreManagerRoles, AppPolicies.SuppliersViewOrCapability);

    // Create/Update/Delete were AtLeastNetworkManager (tighter than the class-level
    // AtLeastStoreManager they used to be AND-combined with) — must stay tighter, not loosen
    // to StoreManager, when the class-level gate is removed.
    [Fact]
    public void SuppliersManageOrCapability_base_roles_match_AtLeastNetworkManager_excludes_StoreManager()
    {
        var roles = RequirementFor(AppPolicies.SuppliersManageOrCapability).AllowedRoles;
        Assert.DoesNotContain(AppRoles.StoreManager, roles);
        AssertSameRoles(AppPolicies.AtLeastNetworkManagerRoles, AppPolicies.SuppliersManageOrCapability);
    }

    [Fact]
    public void ReceiptsViewOrCapability_base_roles_match_CanReceiveStock()
        => AssertSameRoles(AppPolicies.CanReceiveStockRoles, AppPolicies.ReceiptsViewOrCapability);

    [Fact]
    public void AiOrdersPolicies_base_roles_match_AtLeastStoreManager()
    {
        AssertSameRoles(AppPolicies.AtLeastStoreManagerRoles, AppPolicies.AiOrdersViewOrCapability);
        AssertSameRoles(AppPolicies.AtLeastStoreManagerRoles, AppPolicies.AiOrdersManageOrCapability);
    }

    // TASK-352 (Block 1 audit, user decision): Invite/Deactivate were previously
    // AtLeastEnterpriseAdmin-only (narrower than v1-spec.md §3.2, which grants staff
    // management to network_manager/store_manager too) — now widened to share the same
    // AtLeastStoreManagerRoles floor as Update, via this one policy for all three actions.
    [Fact]
    public void StoreManagerOrUsersManage_base_roles_match_AtLeastStoreManager()
        => AssertSameRoles(AppPolicies.AtLeastStoreManagerRoles, AppPolicies.StoreManagerOrUsersManage);

    [Fact]
    public void StoreManagerOrUsersManage_base_roles_include_StoreManager_and_NetworkManager()
    {
        var roles = RequirementFor(AppPolicies.StoreManagerOrUsersManage).AllowedRoles;
        Assert.Contains(AppRoles.StoreManager, roles);
        Assert.Contains(AppRoles.NetworkManager, roles);
        Assert.Contains(AppRoles.EnterpriseAdmin, roles);
    }

    [Theory]
    [InlineData(AppPolicies.SchedulesManageOrCapability)]
    [InlineData(AppPolicies.AnalyticsViewOrCapability)]
    [InlineData(AppPolicies.IntegrationsViewOrCapability)]
    [InlineData(AppPolicies.IntegrationsManageOrCapability)]
    [InlineData(AppPolicies.OrdersManageOrCapability)]
    [InlineData(AppPolicies.SuppliersViewOrCapability)]
    [InlineData(AppPolicies.SuppliersManageOrCapability)]
    [InlineData(AppPolicies.ReceiptsViewOrCapability)]
    [InlineData(AppPolicies.AiOrdersViewOrCapability)]
    [InlineData(AppPolicies.AiOrdersManageOrCapability)]
    [InlineData(AppPolicies.StoreManagerOrUsersManage)]
    public void RoleOrCapabilityPolicy_is_registered(string policyName)
        => Assert.NotNull(_options.GetPolicy(policyName));

    // ── helper ──────────────────────────────────────────────────────────────

    private IEnumerable<string> RolesFor(string policyName)
    {
        var policy = _options.GetPolicy(policyName)
            ?? throw new InvalidOperationException($"Policy '{policyName}' not registered.");

        return policy.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .SelectMany(r => r.AllowedRoles);
    }

    private RoleOrCapabilityRequirement RequirementFor(string policyName)
    {
        var policy = _options.GetPolicy(policyName)
            ?? throw new InvalidOperationException($"Policy '{policyName}' not registered.");

        return policy.Requirements.OfType<RoleOrCapabilityRequirement>().Single();
    }

    private void AssertSameRoles(IReadOnlyCollection<string> expected, string policyName) =>
        Assert.Equal(
            expected.OrderBy(r => r, StringComparer.Ordinal),
            RequirementFor(policyName).AllowedRoles.OrderBy(r => r, StringComparer.Ordinal));
}
