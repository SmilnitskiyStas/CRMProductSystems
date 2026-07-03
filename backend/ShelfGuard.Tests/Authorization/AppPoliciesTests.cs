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

    // ── helper ──────────────────────────────────────────────────────────────

    private IEnumerable<string> RolesFor(string policyName)
    {
        var policy = _options.GetPolicy(policyName)
            ?? throw new InvalidOperationException($"Policy '{policyName}' not registered.");

        return policy.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .SelectMany(r => r.AllowedRoles);
    }
}
