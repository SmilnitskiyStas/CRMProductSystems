using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.MobileConfig;

/// <summary>
/// TASK-547 — end-to-end proof that <c>GET /api/v1/mobile/config/preview</c> is unreachable by a
/// consumer session, exercised through the REAL ASP.NET Core authorization pipeline (a genuine
/// <see cref="IAuthorizationService"/> built from <see cref="AppPolicies.Configure"/> — the exact
/// same policy registration <c>Program.cs</c> uses — not a bespoke role-string comparison). This
/// repo has no <c>WebApplicationFactory</c> HTTP harness (see <c>RlsRoleGuardTests</c> remarks), so
/// this is the closest available integration-style proof: it runs the actual
/// <see cref="AuthorizationOptions"/>/<see cref="IAuthorizationService"/>/built-in
/// <c>RolesAuthorizationRequirement</c> handler chain that a real request would go through, against
/// a <see cref="ClaimsPrincipal"/> shaped exactly like a real consumer-session JWT
/// (<see cref="AppRoles.Consumer"/> role claim, no <c>tenant_id</c> claim — see
/// <see cref="AppRoles.Consumer"/>'s remarks and <c>MobileConfigController</c>'s
/// <c>TenantSessionOverride</c> notes for why a consumer JWT is shaped this way).
///
/// Two things must both hold for the endpoint to actually be safe, so both are asserted:
/// (1) <see cref="MobileConfigPreviewController"/> is decorated with
/// <c>[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]</c> and carries no
/// <c>[AllowAnonymous]</c> anywhere in its metadata (the exact trap
/// <see cref="MobileConfigController"/>'s controller-level <c>[AllowAnonymous]</c> would set for
/// an action added there instead — see that controller's remarks); (2) that named policy itself,
/// evaluated for real, rejects a consumer session and accepts a staff enterprise_admin session.
/// </summary>
public sealed class MobileConfigPreviewAuthorizationTests
{
    private readonly IAuthorizationService _authService;

    public MobileConfigPreviewAuthorizationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(AppPolicies.Configure);
        _authService = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    [Fact]
    public void Controller_declares_AtLeastEnterpriseAdmin_and_carries_no_AllowAnonymous_anywhere()
    {
        var controllerType = typeof(MobileConfigPreviewController);

        var authorize = controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();
        Assert.NotNull(authorize);
        Assert.Equal(AppPolicies.AtLeastEnterpriseAdmin, authorize!.Policy);

        // Not on the controller...
        Assert.Empty(controllerType.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        // ...and not on any of its actions either — either would silently disable the check above.
        foreach (var method in controllerType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Fact]
    public async Task AtLeastEnterpriseAdmin_policy_rejects_a_consumer_session_JWT()
    {
        // Same shape a real consumer-session JWT carries (TASK-405, AppRoles.Consumer remarks):
        // a "consumer" role claim and structurally no tenant_id claim at all.
        var consumerSession = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, AppRoles.Consumer)], authenticationType: "TestAuth"));

        var result = await _authService.AuthorizeAsync(consumerSession, resource: null, AppPolicies.AtLeastEnterpriseAdmin);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AtLeastEnterpriseAdmin_policy_rejects_an_unauthenticated_anonymous_principal()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // no authentication type = unauthenticated

        var result = await _authService.AuthorizeAsync(anonymous, resource: null, AppPolicies.AtLeastEnterpriseAdmin);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Storekeeper)]
    [InlineData(AppRoles.Cashier)]
    [InlineData(AppRoles.StoreManager)]
    [InlineData(AppRoles.NetworkManager)]
    public async Task AtLeastEnterpriseAdmin_policy_rejects_every_staff_role_below_enterprise_admin(string role)
    {
        var lowerStaffSession = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role)], authenticationType: "TestAuth"));

        var result = await _authService.AuthorizeAsync(lowerStaffSession, resource: null, AppPolicies.AtLeastEnterpriseAdmin);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AtLeastEnterpriseAdmin_policy_accepts_a_real_enterprise_admin_session()
    {
        // Positive control — proves the policy setup in this test isn't just failing everything.
        var staffSession = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, AppRoles.EnterpriseAdmin)], authenticationType: "TestAuth"));

        var result = await _authService.AuthorizeAsync(staffSession, resource: null, AppPolicies.AtLeastEnterpriseAdmin);

        Assert.True(result.Succeeded);
    }
}
