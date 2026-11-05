using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class SessionControllerTests
{
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private AuthController Controller(params Claim[] claims)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Length == 0 ? null : "Bearer")) };
        http.Request.Headers.Cookie = "refreshToken=synthetic-cookie";
        return new AuthController(_auth, Substitute.For<IUserService>(), Substitute.For<ITenantRepository>())
            { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Fact]
    public async Task Bearer_logout_revokes_the_callers_session_even_if_browser_cookie_names_another_session()
    {
        var id = Guid.NewGuid(); var sid = Guid.NewGuid();
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, "provider"), new Claim("sid", sid.ToString()));
        Assert.IsType<NoContentResult>(await controller.Logout(default));
        await _auth.Received(1).RevokeSessionAsync(id, sid, default);
        await _auth.DidNotReceiveWithAnyArgs().RevokeAsync(default!, default);
    }

    [Fact]
    public async Task Expired_bearer_can_log_out_with_only_the_refresh_cookie_proof()
    {
        Assert.IsType<NoContentResult>(await Controller().Logout(default));
        await _auth.Received(1).RevokeAsync("synthetic-cookie", default);
    }

    [Fact]
    public async Task Personal_identity_cannot_revoke_workspace_sessions()
    {
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "consumer"));
        Assert.IsType<ForbidResult>(await controller.Logout(default));
        Assert.IsType<ForbidResult>(await controller.LogoutAll(default));
        Assert.Empty(_auth.ReceivedCalls());
    }
}
