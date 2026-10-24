using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Authorization;

public sealed class AccessMatrixPolicyTests
{
    [Theory]
    [InlineData("provider")]
    [InlineData("provider_admin")]
    [InlineData("provider_agent")]
    [InlineData("enterprise_admin")]
    [InlineData("network_manager")]
    [InlineData("store_manager")]
    [InlineData("supplier_admin")]
    public async Task Privileged_default_roles_retain_the_existing_MFA_disable_restriction(string role)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=synthetic;Username=synthetic;Password=synthetic").Options);
        var policy = new LiveMfaPolicy(db, Substitute.For<ITenantSessionOverride>());
        Assert.True(await policy.IsRequiredAsync(User.Create(null, "synthetic@test.example", "Synthetic", "hash", role)));
    }
    [Theory]
    [InlineData(null, false)]
    [InlineData(-301, false)]
    [InlineData(-300, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void Critical_actions_require_MFA_no_older_than_five_minutes_and_reject_future_claims(int? seconds, bool allowed)
    {
        var clock = new FixedTime();
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        var claims = new List<Claim>(); if (seconds != null) claims.Add(new("mfa_at", (clock.GetUtcNow().ToUnixTimeSeconds() + seconds).ToString()!));
        var http = new DefaultHttpContext { RequestServices = services, User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        new RequireRecentMfaAttribute().OnAuthorization(context);
        if (allowed) Assert.Null(context.Result); else Assert.Equal(403, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }
    [Theory]
    [InlineData("staff", false)]
    [InlineData("storekeeper", false)]
    [InlineData("cashier", false)]
    [InlineData("merchandiser", false)]
    [InlineData("enterprise_admin", true)]
    [InlineData("network_manager", true)]
    [InlineData("store_manager", true)]
    public void Production_writes_and_AI_have_explicit_manager_defaults(string role, bool expected)
    {
        var scope = new WorkAccessScope(Guid.NewGuid(), Guid.NewGuid(), role, false, [], new HashSet<string>(), new Dictionary<string, bool>());
        Assert.Equal(expected, scope.Has("production.manage")); Assert.Equal(expected, scope.Has("ai_assistant.use"));
    }
    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    }
}
