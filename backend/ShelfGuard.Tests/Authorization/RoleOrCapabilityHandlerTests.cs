using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

/// <summary>
/// ADR-020 (TASK-346): the OR-composition is only real if RoleOrCapabilityHandler actually
/// succeeds for a role that is NOT in AllowedRoles as long as the capability claim matches —
/// this is the exact scenario the ADR's "blocking discovery" identified as broken for a plain
/// in-body check layered under a role-restrictive class-level policy.
/// </summary>
public sealed class RoleOrCapabilityHandlerTests
{
    private readonly RoleOrCapabilityHandler _sut = new();

    [Fact]
    public async Task Succeeds_when_role_is_in_allowed_roles_even_without_capability_claim()
    {
        var context = MakeContext(
            allowedRoles: [AppRoles.StoreManager], capability: "users.manage",
            role: AppRoles.StoreManager, capabilitiesClaim: null);

        await _sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_when_role_is_below_allowed_roles_but_capability_claim_matches()
    {
        // The exact case AppRoles.Staff (rank 0) exists for: role alone would never pass,
        // capability admits it.
        var context = MakeContext(
            allowedRoles: [AppRoles.StoreManager], capability: "users.manage",
            role: AppRoles.Staff, capabilitiesClaim: "users.manage");

        await _sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_when_role_not_allowed_and_capability_claim_has_a_different_key()
    {
        var context = MakeContext(
            allowedRoles: [AppRoles.StoreManager], capability: "users.manage",
            role: AppRoles.Staff, capabilitiesClaim: "schedules.manage");

        await _sut.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_when_role_not_allowed_and_no_capabilities_claim_present()
    {
        var context = MakeContext(
            allowedRoles: [AppRoles.StoreManager], capability: "users.manage",
            role: AppRoles.Cashier, capabilitiesClaim: null);

        await _sut.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_when_capability_is_one_of_several_comma_joined_values()
    {
        var context = MakeContext(
            allowedRoles: [AppRoles.StoreManager], capability: "suppliers.manage",
            role: AppRoles.Staff, capabilitiesClaim: "suppliers.view,suppliers.manage,orders.manage");

        await _sut.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext MakeContext(
        string[] allowedRoles, string capability, string? role, string? capabilitiesClaim)
    {
        var requirement = new RoleOrCapabilityRequirement(allowedRoles, capability);

        var claims = new List<Claim>();
        if (role is not null)
            claims.Add(new Claim(ClaimTypes.Role, role));
        if (capabilitiesClaim is not null)
            claims.Add(new Claim("capabilities", capabilitiesClaim));

        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        return new AuthorizationHandlerContext([requirement], user, resource: null);
    }
}

public sealed class TenantRoleAuthorizationTests
{
    [Fact]
    public void HasCapability_true_when_claim_contains_the_key()
        => Assert.True(TenantRoleAuthorization.HasCapability(MakeUser("analytics.view,users.manage"), "users.manage"));

    [Fact]
    public void HasCapability_false_when_claim_is_missing()
        => Assert.False(TenantRoleAuthorization.HasCapability(MakeUser(null), "users.manage"));

    [Fact]
    public void HasCapability_false_when_claim_present_but_key_absent()
        => Assert.False(TenantRoleAuthorization.HasCapability(MakeUser("analytics.view"), "users.manage"));

    [Fact]
    public void HasCapability_false_when_claim_is_empty_string()
        => Assert.False(TenantRoleAuthorization.HasCapability(MakeUser(""), "users.manage"));

    private static ClaimsPrincipal MakeUser(string? capabilitiesClaim)
    {
        var claims = new List<Claim>();
        if (capabilitiesClaim is not null)
            claims.Add(new Claim("capabilities", capabilitiesClaim));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
