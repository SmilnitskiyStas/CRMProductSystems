using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Authorization;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

public sealed class RequireRecentMfaIfEnrolledTests
{
    private readonly FixedTime clock = new();
    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly User user = User.Create(null, "synthetic@example.test", "Synthetic", "hash", "provider");

    private async Task<IActionResult?> Run(bool enrolled, long? mfaOffset, bool impersonated = false, bool lookupFails = false)
    {
        if (enrolled) { user.SetPendingTotpSecret("SYNTHETICSECRET"); user.EnableTotp(["h"]); }
        if (lookupFails) users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns<User?>(_ => throw new InvalidOperationException("db down"));
        else users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).AddSingleton(users).BuildServiceProvider();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()) };
        if (mfaOffset != null) claims.Add(new("mfa_at", (clock.GetUtcNow().ToUnixTimeSeconds() + mfaOffset).ToString()!));
        if (impersonated) claims.Add(new("impersonated", "true"));
        var http = new DefaultHttpContext { RequestServices = services, User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        await new RequireRecentMfaIfEnrolledAttribute().OnAuthorizationAsync(context);
        return context.Result;
    }

    private static string Code(IActionResult? r) => Assert.IsType<ObjectResult>(r).Value!.GetType().GetProperty("code")!.GetValue(Assert.IsType<ObjectResult>(r).Value)!.ToString()!;

    [Fact] public async Task Not_enrolled_without_mfa_passes() => Assert.Null(await Run(false, null));

    [Fact]
    public async Task Enrolled_without_fresh_mfa_is_403_step_up()
    {
        var r = await Run(true, -301);
        Assert.Equal(403, Assert.IsType<ObjectResult>(r).StatusCode);
        Assert.Equal("mfa_step_up_required", Code(r));
    }

    [Fact] public async Task Enrolled_without_mfa_claim_is_403() => Assert.Equal(403, Assert.IsType<ObjectResult>(await Run(true, null)).StatusCode);

    [Fact] public async Task Enrolled_with_fresh_mfa_passes() => Assert.Null(await Run(true, -10));

    [Fact] public async Task Enrolled_with_future_mfa_is_403() => Assert.Equal(403, Assert.IsType<ObjectResult>(await Run(true, 60)).StatusCode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Impersonated_without_fresh_mfa_is_403_regardless_of_2fa(bool enrolled)
    {
        var r = await Run(enrolled, null, impersonated: true);
        Assert.Equal(403, Assert.IsType<ObjectResult>(r).StatusCode);
        Assert.Equal("mfa_reauthentication_required", Code(r));
    }

    [Fact]
    public async Task Lookup_failure_fails_closed()
    {
        var r = await Run(false, null, lookupFails: true);
        Assert.Equal(403, Assert.IsType<ObjectResult>(r).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_is_401()
    {
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        await new RequireRecentMfaIfEnrolledAttribute().OnAuthorizationAsync(context);
        Assert.IsType<UnauthorizedResult>(context.Result);
    }

    [Fact]
    public void ProviderController_actions_use_enrolled_variant_except_Impersonate()
    {
        var actions = typeof(ProviderController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<RequireRecentMfaIfEnrolledAttribute>().Any()).ToList();
        Assert.Equal(13, actions.Count);
        Assert.Empty(typeof(ProviderController).GetMethods().Where(m => m.GetCustomAttributes<RequireRecentMfaAttribute>().Any()).Select(m => m.Name).Except(["Impersonate"]));
        Assert.NotEmpty(typeof(ProviderController).GetMethod("Impersonate")!.GetCustomAttributes<RequireRecentMfaAttribute>());
    }

    [Fact]
    public void Other_controllers_keep_strict_attribute() =>
        Assert.NotEmpty(typeof(ProviderRolesController).GetMethods().SelectMany(m => m.GetCustomAttributes<RequireRecentMfaAttribute>()));

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    }
}
