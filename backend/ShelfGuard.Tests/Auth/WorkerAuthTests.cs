using ShelfGuard.Application.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using ShelfGuard.Api.Infrastructure;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Services;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class WorkerAuthTests
{
    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher hasher = Substitute.For<IPasswordHasher>();
    private readonly IWorkerTokenIssuer issuer = Substitute.For<IWorkerTokenIssuer>();
    private readonly IActivityLogRepository audit = Substitute.For<IActivityLogRepository>();
    private readonly AuthTenantScope scope = new();
    private readonly User user = User.Create(Guid.NewGuid(), "worker@synthetic.example", "Synthetic", "hash", "network_manager");
    private WorkerAuthService Service(string? configured = null) => new(users, hasher, issuer, audit, scope, new(configured ?? user.Email));
    public WorkerAuthTests()
    {
        users.GetByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        hasher.Verify("synthetic-password", "hash").Returns(true);
        issuer.GenerateWorkerToken(user).Returns(new WorkerGrant("synthetic-token", user.TenantId!.Value, DateTime.UtcNow.AddMinutes(5)));
    }
    [Fact]
    public async Task Explicit_service_grant_keeps_human_MFA_unverified_and_uses_own_tenant_for_audit()
    {
        audit.SaveChangesAsync(default).Returns(_ => { Assert.Equal(user.TenantId, scope.TenantId); return Task.CompletedTask; });
        var result = await Service().LoginAsync(user.Email, "synthetic-password");
        Assert.NotNull(result); Assert.False(user.TotpEnabled); Assert.Equal(0, user.AuthVersion); Assert.Null(scope.TenantId);
        await audit.Received().LogAsync(Arg.Is<ActivityLog>(a => a.Action == "worker.token_issued" && a.TenantId == user.TenantId), default);
    }
    [Theory]
    [InlineData("")]
    [InlineData("other@synthetic.example")]
    public async Task Disabled_or_different_configured_identity_cannot_mint_tokens(string configured)
    {
        Assert.Null(await Service(configured).LoginAsync(user.Email, "synthetic-password"));
        issuer.DidNotReceive().GenerateWorkerToken(Arg.Any<User>());
    }
    [Fact]
    public async Task Invalid_password_counts_toward_shared_lockout_and_issues_no_token()
    {
        users.SaveChangesAsync(default).Returns(_ => { Assert.Equal(user.TenantId, scope.TenantId); return Task.CompletedTask; });
        for (var i=0;i<5;i++) Assert.Null(await Service().LoginAsync(user.Email, "wrong"));
        Assert.True(user.IsLockedOut); issuer.DidNotReceive().GenerateWorkerToken(Arg.Any<User>());
    }
    [Fact]
    public async Task Inactive_identity_cannot_mint_tokens()
    {
        user.Deactivate(); Assert.Null(await Service().LoginAsync(user.Email, "synthetic-password"));
    }
    [Theory]
    [InlineData("provider")]
    [InlineData("supplier_admin")]
    [InlineData("cashier")]
    [InlineData("consumer")]
    [InlineData("staff")]
    public void Machine_identity_cannot_gain_a_cross_tenant_or_unrelated_role(string role) => Assert.False(WorkerAuthService.AllowedRole(role));
    [Fact]
    public void Worker_JWT_has_separate_audience_purpose_tenant_and_five_minute_expiry_without_staff_session_claims()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Jwt:Secret"]="SYNTHETIC-WORKER-KEY-00000000000000000000000000000000", ["Jwt:Issuer"]="worker-test", ["Jwt:Audience"]="worker-test" }).Build();
        var grant = new JwtService(config).GenerateWorkerToken(user); var jwt = new JwtSecurityTokenHandler().ReadJwtToken(grant.AccessToken);
        Assert.Equal("worker-test:worker", Assert.Single(jwt.Audiences)); Assert.Contains(jwt.Claims, c=>c.Type=="purpose" && c.Value=="worker-job");
        Assert.DoesNotContain(jwt.Claims,c=>c.Type is "sid" or "mfa_at" or "session_id" or "consumer_account_id");
        Assert.Equal(user.TenantId, grant.TenantId); Assert.InRange(grant.ExpiresAt-DateTime.UtcNow,TimeSpan.FromMinutes(4),TimeSpan.FromMinutes(5));
    }
    [Theory]
    [InlineData("127.0.0.1",true)]
    [InlineData("::1",true)]
    [InlineData("203.0.113.8",false)]
    public void Raw_peer_boundary_does_not_trust_forwarded_or_replaced_addresses(string raw,bool allowed)
    {
        var context=new DefaultHttpContext(); context.Items[WorkerPeerBoundary.OriginalPeer]=IPAddress.Parse(raw);
        context.Connection.RemoteIpAddress=IPAddress.Loopback; context.Request.Headers["X-Forwarded-For"]="127.0.0.1";
        Assert.Equal(allowed,WorkerPeerBoundary.IsTrusted(context,new ConfigurationBuilder().Build()));
    }
}
