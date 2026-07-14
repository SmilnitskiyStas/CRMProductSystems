using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Auth;

/// <summary>
/// ADR-020 (TASK-346): AuthService.BuildEffectiveCapabilitiesAsync — merged into the JWT
/// "capabilities" claim and AuthUserDto.Capabilities at every mint site (login, 2FA verify,
/// refresh) and GetCurrentUserAsync, parallel to the existing effective-permissions merge.
/// </summary>
public sealed class AuthServiceCapabilitiesTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly ITotpService _totp = Substitute.For<ITotpService>();
    private readonly IUserPermissionGrantRepository _permissionGrants = Substitute.For<IUserPermissionGrantRepository>();
    private readonly ITenantRoleRepository _tenantRoles = Substitute.For<ITenantRoleRepository>();
    private readonly AuthService _sut;

    public AuthServiceCapabilitiesTests()
    {
        _sut = new AuthService(_users, _tokens, _hasher, _jwt, _activityLogs, _totp, _permissionGrants, _tenantRoles,
            NullLogger<AuthService>.Instance);
        _permissionGrants.GetActiveGrantsForUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserPermissionGrant>());
        _jwt.GenerateRefreshToken().Returns(("raw_token", "hashed_token"));
        _jwt.HashToken("raw_token").Returns("hashed_token");
        _jwt.GenerateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(),
            Arg.Any<string?>(), Arg.Any<Dictionary<string, bool>?>(), Arg.Any<List<string>?>()).Returns("access_token");
    }

    [Fact]
    public async Task LoginAsync_ActiveTenantRole_CapabilitiesReachBothTheJwtCallAndTheDto()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "hr@example.com", "HR User", "hash", AppRoles.Staff);
        var role = TenantRole.Create(tenantId, "HR",
            [TenantRoleCapabilities.UsersManage, TenantRoleCapabilities.SchedulesManage], null);
        user.SetTenantRole(role.Id);

        _users.GetByEmailAsync("hr@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);
        _tenantRoles.GetByIdAsync(tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        List<string>? capturedCapabilities = null;
        _jwt.GenerateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<Dictionary<string, bool>?>(),
                Arg.Do<List<string>?>(c => capturedCapabilities = c))
            .Returns("access_token");

        var outcome = await _sut.LoginAsync("hr@example.com", "password123");

        Assert.NotNull(outcome.Response);
        Assert.Equal(
            new[] { TenantRoleCapabilities.UsersManage, TenantRoleCapabilities.SchedulesManage },
            capturedCapabilities);
        Assert.Equal(
            new[] { TenantRoleCapabilities.UsersManage, TenantRoleCapabilities.SchedulesManage },
            outcome.Response!.User.Capabilities);
    }

    [Fact]
    public async Task LoginAsync_NoTenantRoleAssigned_CapabilitiesEmpty_NoRepositoryLookup()
    {
        var user = User.Create(Guid.NewGuid(), "manager@example.com", "Manager", "hash", AppRoles.StoreManager);
        _users.GetByEmailAsync("manager@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);

        var outcome = await _sut.LoginAsync("manager@example.com", "password123");

        Assert.NotNull(outcome.Response);
        Assert.Empty(outcome.Response!.User.Capabilities!);
        await _tenantRoles.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginAsync_ArchivedTenantRole_CapabilitiesEmpty()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "archived@example.com", "User", "hash", AppRoles.Staff);
        var role = TenantRole.Create(tenantId, "Archived", [TenantRoleCapabilities.OrdersManage], null);
        role.Deactivate();
        user.SetTenantRole(role.Id);

        _users.GetByEmailAsync("archived@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);
        _tenantRoles.GetByIdAsync(tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        var outcome = await _sut.LoginAsync("archived@example.com", "password123");

        Assert.NotNull(outcome.Response);
        Assert.Empty(outcome.Response!.User.Capabilities!);
    }

    [Fact]
    public async Task LoginAsync_TenantRoleIdPointsAtMissingRow_CapabilitiesEmpty()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "dangling@example.com", "User", "hash", AppRoles.Staff);
        user.SetTenantRole(Guid.NewGuid()); // no matching row stubbed -> repository returns null
        _users.GetByEmailAsync("dangling@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);

        var outcome = await _sut.LoginAsync("dangling@example.com", "password123");

        Assert.NotNull(outcome.Response);
        Assert.Empty(outcome.Response!.User.Capabilities!);
    }

    [Fact]
    public async Task RefreshAsync_ActiveTenantRole_CapabilitiesInDto()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "purchasing@example.com", "Purchasing", "hash", AppRoles.Staff);
        var role = TenantRole.Create(tenantId, "Закупка", [TenantRoleCapabilities.SuppliersView], null);
        user.SetTenantRole(role.Id);

        var token = RefreshToken.Create(user.Id, "hashed_token", DateTime.UtcNow.AddDays(7));
        _tokens.GetByHashAsync("hashed_token", default).Returns(token);
        _users.GetByIdAsync(user.Id, default).Returns(user);
        _tenantRoles.GetByIdAsync(tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);
        _jwt.GenerateRefreshToken().Returns(("new_raw", "new_hash"));

        var (response, error) = await _sut.RefreshAsync("raw_token");

        Assert.Null(error);
        Assert.Equal(new[] { TenantRoleCapabilities.SuppliersView }, response!.User.Capabilities);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ReflectsEffectiveCapabilities()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "accountant@example.com", "Accountant", "hash", AppRoles.Staff);
        var role = TenantRole.Create(tenantId, "Бухгалтер",
            [TenantRoleCapabilities.AnalyticsView, TenantUserPermissions.LegalEntitiesManage], null);
        user.SetTenantRole(role.Id);

        _users.GetByIdAsync(user.Id, default).Returns(user);
        _tenantRoles.GetByIdAsync(tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        var dto = await _sut.GetCurrentUserAsync(user.Id);

        Assert.NotNull(dto);
        Assert.Equal(
            new[] { TenantRoleCapabilities.AnalyticsView, TenantUserPermissions.LegalEntitiesManage },
            dto!.Capabilities);
    }
}
