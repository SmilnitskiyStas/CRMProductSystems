using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using ShelfGuard.Api.Controllers;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Features.Auth.Dtos;
using ShelfGuard.Application.Features.ConsumerAuth;
using ShelfGuard.Application.Features.ConsumerAuth.Dtos;
using ShelfGuard.Application.Features.MobileAuth.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class MobileAuthControllerTests
{
    private readonly IAuthService _staffAuth = Substitute.For<IAuthService>();
    private readonly IConsumerAuthService _consumerAuth = Substitute.For<IConsumerAuthService>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IConsumerAccountRepository _consumers = Substitute.For<IConsumerAccountRepository>();
    private readonly MobileAuthController _controller;

    public MobileAuthControllerTests()
    {
        _controller = new MobileAuthController(_staffAuth, _consumerAuth, _users, _consumers)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task Registered_phone_linked_to_user_returns_workspace_access()
    {
        var account = new ConsumerAccount
        {
            Phone = "+380501234567", FullName = "Mobile User", PasswordHash = "hash",
        };
        var staff = User.Create(Guid.NewGuid(), "worker@example.com", "Mobile User", "staff-hash", "staff");
        staff.UpdateProfile("Mobile User", account.Phone);
        var staffDto = StaffDto(staff);

        _consumers.GetByPhoneAsync(account.Phone, default).Returns(account);
        _consumerAuth.LoginAsync(Arg.Any<ConsumerLoginRequest>(), default)
            .Returns((new ConsumerAuthResponse("consumer-token", account.Id, account.FullName, account.Phone), null));
        _users.GetByPhoneAsync(account.Phone, default).Returns(staff);
        _staffAuth.IssueLinkedMobileSessionAsync(staff.Id, Arg.Any<string?>(), default)
            .Returns(new LoginOutcome(new LoginResponse("staff-token", "refresh", staffDto), null, null));

        var result = await _controller.Login(
            new MobileLoginRequest("050 123 45 67", "personal-password"), default);

        var response = Assert.IsType<MobileLoginResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(response.Access.CanAccessWorkspace);
        Assert.Equal("staff-token", response.WorkspaceAccessToken);
        Assert.Equal("consumer-token", response.PersonalAccessToken);
        await _staffAuth.Received(1).IssueLinkedMobileSessionAsync(staff.Id, Arg.Any<string?>(), default);
    }

    [Fact]
    public async Task Login_linked_staff_requiring_two_factor_still_exposes_personal_token()
    {
        var account = new ConsumerAccount
        {
            Phone = "+380501234567", FullName = "Mobile User", PasswordHash = "hash",
        };
        var staff = User.Create(Guid.NewGuid(), "worker@example.com", "Mobile User", "staff-hash", "staff");
        staff.UpdateProfile("Mobile User", account.Phone);

        _consumers.GetByPhoneAsync(account.Phone, default).Returns(account);
        _consumerAuth.LoginAsync(Arg.Any<ConsumerLoginRequest>(), default)
            .Returns((new ConsumerAuthResponse("consumer-token", account.Id, account.FullName, account.Phone), null));
        _users.GetByPhoneAsync(account.Phone, default).Returns(staff);
        _staffAuth.IssueLinkedMobileSessionAsync(staff.Id, Arg.Any<string?>(), default)
            .Returns(new LoginOutcome(null, "challenge-token", null));

        var result = await _controller.Login(
            new MobileLoginRequest("050 123 45 67", "personal-password"), default);

        var value = Assert.IsType<OkObjectResult>(result).Value!;
        Assert.Equal(true, value.GetType().GetProperty("requiresTwoFactor")!.GetValue(value));
        Assert.Equal("challenge-token", value.GetType().GetProperty("challengeToken")!.GetValue(value));
        Assert.Equal("consumer-token", value.GetType().GetProperty("personalAccessToken")!.GetValue(value));
    }

    [Fact]
    public async Task Registration_immediately_returns_workspace_access_when_phone_is_linked()
    {
        var account = new ConsumerAccount
        {
            Phone = "+380501234567", FullName = "New Mobile User", PasswordHash = "hash",
        };
        var staff = User.Create(Guid.NewGuid(), "worker@example.com", account.FullName, "hash", "staff");
        staff.UpdateProfile(account.FullName, account.Phone);
        var request = new ConsumerRegisterRequest(account.Phone, "StrongPassword1", account.FullName);

        _consumerAuth.RegisterAsync(request, default)
            .Returns((new ConsumerAuthResponse("consumer-token", account.Id, account.FullName, account.Phone), null));
        _consumers.GetByIdAsync(account.Id, default).Returns(account);
        _users.GetByPhoneAsync(account.Phone, default).Returns(staff);
        _staffAuth.IssueLinkedMobileSessionAsync(staff.Id, Arg.Any<string?>(), default)
            .Returns(new LoginOutcome(
                new LoginResponse("staff-token", "refresh", StaffDto(staff)), null, null));

        var result = await _controller.Register(request, default);

        var response = Assert.IsType<MobileLoginResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(response.Access.CanAccessWorkspace);
        Assert.Equal("staff-token", response.WorkspaceAccessToken);
        Assert.Equal("consumer-token", response.PersonalAccessToken);
    }

    [Fact]
    public async Task Register_linked_staff_requiring_two_factor_still_exposes_personal_token()
    {
        var account = new ConsumerAccount
        {
            Phone = "+380501234567", FullName = "New Mobile User", PasswordHash = "hash",
        };
        var staff = User.Create(Guid.NewGuid(), "worker@example.com", account.FullName, "hash", "staff");
        staff.UpdateProfile(account.FullName, account.Phone);
        var request = new ConsumerRegisterRequest(account.Phone, "StrongPassword1", account.FullName);

        _consumerAuth.RegisterAsync(request, default)
            .Returns((new ConsumerAuthResponse("consumer-token", account.Id, account.FullName, account.Phone), null));
        _consumers.GetByIdAsync(account.Id, default).Returns(account);
        _users.GetByPhoneAsync(account.Phone, default).Returns(staff);
        _staffAuth.IssueLinkedMobileSessionAsync(staff.Id, Arg.Any<string?>(), default)
            .Returns(new LoginOutcome(null, "challenge-token", null));

        var result = await _controller.Register(request, default);

        var value = Assert.IsType<OkObjectResult>(result).Value!;
        Assert.Equal(true, value.GetType().GetProperty("requiresTwoFactor")!.GetValue(value));
        Assert.Equal("challenge-token", value.GetType().GetProperty("challengeToken")!.GetValue(value));
        Assert.Equal("consumer-token", value.GetType().GetProperty("personalAccessToken")!.GetValue(value));
    }

    [Fact]
    public async Task Consumer_email_without_link_stays_personal_only()
    {
        var account = new ConsumerAccount
        {
            Phone = "+380501234567", Email = "person@example.com", FullName = "Person", PasswordHash = "hash",
        };
        _consumers.GetByEmailAsync("person@example.com", default).Returns(account);
        _consumerAuth.LoginAsync(Arg.Any<ConsumerLoginRequest>(), default)
            .Returns((new ConsumerAuthResponse("consumer-token", account.Id, account.FullName, account.Phone), null));
        _users.GetByPhoneAsync(account.Phone, default).ReturnsNull();
        _users.GetByEmailAsync(account.Email, default).ReturnsNull();

        var result = await _controller.Login(
            new MobileLoginRequest("person@example.com", "password"), default);

        var response = Assert.IsType<MobileLoginResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(response.Access.CanAccessWorkspace);
        Assert.Equal("consumer-token", response.PersonalAccessToken);
        Assert.Null(response.WorkspaceAccessToken);
    }

    [Fact]
    public async Task Staff_without_personal_account_can_use_phone_as_fallback()
    {
        var staff = User.Create(Guid.NewGuid(), "worker@example.com", "Worker", "hash", "staff");
        staff.UpdateProfile("Worker", "+380501234567");
        _consumers.GetByPhoneAsync(staff.Phone!, default).ReturnsNull();
        _users.GetByPhoneAsync(staff.Phone!, default).Returns(staff);
        _staffAuth.LoginAsync(staff.Email, "password", Arg.Any<string?>(), default)
            .Returns(new LoginOutcome(new LoginResponse("staff-token", "refresh", StaffDto(staff)), null, null));

        var result = await _controller.Login(
            new MobileLoginRequest("+380501234567", "password"), default);

        var response = Assert.IsType<MobileLoginResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(response.Access.CanAccessWorkspace);
        Assert.Null(response.PersonalAccessToken);
        Assert.Equal("staff-token", response.WorkspaceAccessToken);
        await _staffAuth.Received(1).LoginAsync(staff.Email, "password", Arg.Any<string?>(), default);
    }

    [Fact]
    public async Task Staff_only_fallback_two_factor_challenge_has_no_personal_token_field()
    {
        var staff = User.Create(Guid.NewGuid(), "worker@example.com", "Worker", "hash", "staff");
        staff.UpdateProfile("Worker", "+380501234567");
        _consumers.GetByPhoneAsync(staff.Phone!, default).ReturnsNull();
        _users.GetByPhoneAsync(staff.Phone!, default).Returns(staff);
        _staffAuth.LoginAsync(staff.Email, "password", Arg.Any<string?>(), default)
            .Returns(new LoginOutcome(null, "challenge-token", null));

        var result = await _controller.Login(
            new MobileLoginRequest("+380501234567", "password"), default);

        var value = Assert.IsType<OkObjectResult>(result).Value!;
        Assert.Equal(true, value.GetType().GetProperty("requiresTwoFactor")!.GetValue(value));
        Assert.Equal("challenge-token", value.GetType().GetProperty("challengeToken")!.GetValue(value));
        Assert.Null(value.GetType().GetProperty("personalAccessToken"));
    }

    private static AuthUserDto StaffDto(User user) => new(
        user.Id, user.Email, user.FullName, user.Role, user.TenantId, null, user.StoreId,
        new Dictionary<string, bool>());
}
