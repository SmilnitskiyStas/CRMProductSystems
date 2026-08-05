using NSubstitute;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Application.Features.Users.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Users;

/// <summary>TASK-329 — password change: policy + session revocation.</summary>
public sealed class UserServicePasswordTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ILegalEntityService _legalEntities = Substitute.For<ILegalEntityService>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUserPermissionGrantRepository _permissionGrants = Substitute.For<IUserPermissionGrantRepository>();
    private readonly ITenantRoleRepository _tenantRoles = Substitute.For<ITenantRoleRepository>();
    private readonly ILocationService _locations = Substitute.For<ILocationService>();
    private readonly IUserLocationRepository _userLocations = Substitute.For<IUserLocationRepository>();
    private readonly UserService _sut;

    public UserServicePasswordTests()
    {
        _sut = new UserService(_users, _activityLogs, _hasher, _legalEntities, _refreshTokens, _permissionGrants, _tenantRoles, _locations, _userLocations);
    }

    [Fact]
    public async Task ChangePassword_revokes_all_refresh_tokens_on_success()
    {
        var user = MakeUser();
        _users.GetByIdAsync(user.Id, default).Returns(user);
        _hasher.Verify("old-password-1", "hash").Returns(true);
        _hasher.Hash("new-Password-42").Returns("new_hash");

        var error = await _sut.ChangePasswordAsync(user.Id,
            new ChangePasswordRequest("old-password-1", "new-Password-42"), default);

        Assert.Null(error);
        Assert.Equal("new_hash", user.PasswordHash);
        // Stolen sessions must not survive a password change
        await _refreshTokens.Received(1).RevokeAllForUserAsync(user.Id, default);
        await _refreshTokens.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task ChangePassword_clears_temp_password_expiry_on_success()
    {
        // TASK-465: this is the one place a user "takes control" of a temporary password
        // issued by the forgot-password flow — a successful change-password must clear the
        // marker so the account stops being treated as running on a temporary/expiring password.
        var user = MakeUser();
        user.SetTempPasswordExpiry(DateTime.UtcNow.AddHours(3));
        _users.GetByIdAsync(user.Id, default).Returns(user);
        _hasher.Verify("old-password-1", "hash").Returns(true);
        _hasher.Hash("new-Password-42").Returns("new_hash");

        var error = await _sut.ChangePasswordAsync(user.Id,
            new ChangePasswordRequest("old-password-1", "new-Password-42"), default);

        Assert.Null(error);
        Assert.False(user.HasActiveTempPassword);
        Assert.Null(user.TempPasswordExpiresAt);
    }

    [Fact]
    public async Task ChangePassword_rejects_weak_new_password_without_touching_sessions()
    {
        var user = MakeUser();
        _users.GetByIdAsync(user.Id, default).Returns(user);

        var error = await _sut.ChangePasswordAsync(user.Id,
            new ChangePasswordRequest("old-password-1", "short1"), default);

        Assert.Equal("Password must be at least 12 characters.", error);
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), default);
    }

    [Fact]
    public async Task ChangePassword_rejects_wrong_current_password()
    {
        var user = MakeUser();
        _users.GetByIdAsync(user.Id, default).Returns(user);
        _hasher.Verify("wrong-current-1", "hash").Returns(false);

        var error = await _sut.ChangePasswordAsync(user.Id,
            new ChangePasswordRequest("wrong-current-1", "new-Password-42"), default);

        Assert.Equal("Current password is incorrect.", error);
        await _refreshTokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), default);
    }

    [Fact]
    public async Task Invite_rejects_password_that_violates_policy()
    {
        var tenantId = Guid.NewGuid();
        // Password validation (TASK-347: InviteAsync now also takes actingUserId) runs before
        // the acting-user lookup/rank check, so this short-circuits without ever touching
        // _users.GetByIdAsync — an arbitrary actingUserId is fine, no extra mock needed.
        var actingUserId = Guid.NewGuid();
        var request = new InviteUserRequest(
            Email: "new.user@example.com",
            FullName: "New User",
            Role: "storekeeper",
            Password: "qwerty123456"); // common password

        var (user, error) = await _sut.InviteAsync(tenantId, actingUserId, request, "Inviter", default);

        Assert.Null(user);
        Assert.Equal("This password is too common. Choose a more unique password.", error);
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), default);
    }

    private static User MakeUser() =>
        User.Create(Guid.NewGuid(), "test@example.com", "Test User", "hash", "store_manager");
}
