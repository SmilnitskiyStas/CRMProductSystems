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
/// TASK-391b: AuthService.BuildEffectiveTabsAsync — merged into the JWT "tabs" claim and
/// AuthUserDto.Tabs at every mint site (login, refresh) and GetCurrentUserAsync, parallel to
/// the existing effective-capabilities merge (ADR-020/TASK-346, AuthServiceCapabilitiesTests).
/// AllowedTabs and Capabilities are deliberately separate columns/claims/consumers (see
/// ShelfGuard.Domain.Constants.TenantRoleTabs) — this file exercises the Tabs axis only.
/// </summary>
public sealed class AuthServiceTabsTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtService _jwt = Substitute.For<IJwtService>();
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly ITotpService _totp = Substitute.For<ITotpService>();
    private readonly IUserPermissionGrantRepository _permissionGrants = Substitute.For<IUserPermissionGrantRepository>();
    private readonly ITenantRoleRepository _tenantRoles = Substitute.For<ITenantRoleRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly AuthService _sut;

    public AuthServiceTabsTests()
    {
        _sut = new AuthService(_users, _tokens, _hasher, _jwt, _activityLogs, _totp, _permissionGrants, _tenantRoles,
            _notifications, NullLogger<AuthService>.Instance);
        _permissionGrants.GetActiveGrantsForUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserPermissionGrant>());
        _jwt.GenerateRefreshToken().Returns(("raw_token", "hashed_token"));
        _jwt.HashToken("raw_token").Returns("hashed_token");
        _jwt.GenerateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(),
            Arg.Any<string?>(), Arg.Any<Dictionary<string, bool>?>(), Arg.Any<List<string>?>(), Arg.Any<List<string>?>())
            .Returns("access_token");
    }

    [Fact]
    public async Task LoginAsync_ActiveTenantRole_TabsReachBothTheJwtCallAndTheDto()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "hr@example.com", "HR User", "hash", AppRoles.Staff);
        var role = TenantRole.Create(tenantId, "HR", [TenantRoleCapabilities.UsersManage], null,
            [TenantRoleTabs.Workforce, TenantRoleTabs.Support]);
        user.SetTenantRole(role.Id);

        _users.GetByEmailAsync("hr@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);
        _tenantRoles.GetByIdAsync(tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        List<string>? capturedTabs = null;
        _jwt.GenerateAccessToken(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(),
                Arg.Any<string?>(), Arg.Any<Dictionary<string, bool>?>(), Arg.Any<List<string>?>(),
                Arg.Do<List<string>?>(t => capturedTabs = t))
            .Returns("access_token");

        var outcome = await _sut.LoginAsync("hr@example.com", "password123");

        Assert.NotNull(outcome.Response);
        Assert.Equal(
            new[] { TenantRoleTabs.Workforce, TenantRoleTabs.Support },
            capturedTabs);
        Assert.Equal(
            new[] { TenantRoleTabs.Workforce, TenantRoleTabs.Support },
            outcome.Response!.User.Tabs);
    }

    [Fact]
    public async Task LoginAsync_NoTenantRoleAssigned_TabsEmpty_NoRepositoryLookup()
    {
        var user = User.Create(Guid.NewGuid(), "manager@example.com", "Manager", "hash", AppRoles.StoreManager);
        _users.GetByEmailAsync("manager@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);

        var outcome = await _sut.LoginAsync("manager@example.com", "password123");

        Assert.NotNull(outcome.Response);
        Assert.Empty(outcome.Response!.User.Tabs!);
        await _tenantRoles.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginAsync_ArchivedTenantRole_TabsEmpty()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "archived@example.com", "User", "hash", AppRoles.Staff);
        var role = TenantRole.Create(tenantId, "Archived", [TenantRoleCapabilities.OrdersManage], null,
            [TenantRoleTabs.Procurement]);
        role.Deactivate();
        user.SetTenantRole(role.Id);

        _users.GetByEmailAsync("archived@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);
        _tenantRoles.GetByIdAsync(tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        var outcome = await _sut.LoginAsync("archived@example.com", "password123");

        Assert.NotNull(outcome.Response);
        Assert.Empty(outcome.Response!.User.Tabs!);
    }

    [Fact]
    public async Task LoginAsync_TenantRoleIdPointsAtMissingRow_TabsEmpty()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "dangling@example.com", "User", "hash", AppRoles.Staff);
        user.SetTenantRole(Guid.NewGuid()); // no matching row stubbed -> repository returns null
        _users.GetByEmailAsync("dangling@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);

        var outcome = await _sut.LoginAsync("dangling@example.com", "password123");

        Assert.NotNull(outcome.Response);
        Assert.Empty(outcome.Response!.User.Tabs!);
    }

    [Fact]
    public async Task RefreshAsync_ActiveTenantRole_TabsInDto()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "purchasing@example.com", "Purchasing", "hash", AppRoles.Staff);
        var role = TenantRole.Create(tenantId, "Закупка", [TenantRoleCapabilities.SuppliersView], null,
            [TenantRoleTabs.Procurement, TenantRoleTabs.Marketplace]);
        user.SetTenantRole(role.Id);

        var token = RefreshToken.Create(user.Id, "hashed_token", DateTime.UtcNow.AddDays(7));
        _tokens.GetByHashAsync("hashed_token", default).Returns(token);
        _users.GetByIdAsync(user.Id, default).Returns(user);
        _tenantRoles.GetByIdAsync(tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);
        _jwt.GenerateRefreshToken().Returns(("new_raw", "new_hash"));

        var (response, error) = await _sut.RefreshAsync("raw_token");

        Assert.Null(error);
        Assert.Equal(new[] { TenantRoleTabs.Procurement, TenantRoleTabs.Marketplace }, response!.User.Tabs);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ReflectsEffectiveTabs()
    {
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "accountant@example.com", "Accountant", "hash", AppRoles.Staff);
        var role = TenantRole.Create(tenantId, "Бухгалтер",
            [TenantRoleCapabilities.AnalyticsView, TenantUserPermissions.LegalEntitiesManage], null,
            [TenantRoleTabs.Analytics]);
        user.SetTenantRole(role.Id);

        _users.GetByIdAsync(user.Id, default).Returns(user);
        _tenantRoles.GetByIdAsync(tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        var dto = await _sut.GetCurrentUserAsync(user.Id);

        Assert.NotNull(dto);
        Assert.Equal(new[] { TenantRoleTabs.Analytics }, dto!.Tabs);
    }

    [Fact]
    public async Task LoginAsync_TabsAndCapabilitiesResolveIndependently_DifferentSubsetsOfTheSameRole()
    {
        // Guards against a future refactor accidentally merging the two resolution paths —
        // Capabilities and AllowedTabs are unrelated lists on the same TenantRole row.
        var tenantId = Guid.NewGuid();
        var user = User.Create(tenantId, "both@example.com", "Both", "hash", AppRoles.Staff);
        var role = TenantRole.Create(tenantId, "Mixed",
            [TenantRoleCapabilities.UsersManage, TenantRoleCapabilities.OrdersManage], null,
            [TenantRoleTabs.Dashboard]);
        user.SetTenantRole(role.Id);

        _users.GetByEmailAsync("both@example.com", default).Returns(user);
        _hasher.Verify("password123", "hash").Returns(true);
        _tenantRoles.GetByIdAsync(tenantId, role.Id, Arg.Any<CancellationToken>()).Returns(role);

        var outcome = await _sut.LoginAsync("both@example.com", "password123");

        Assert.NotNull(outcome.Response);
        Assert.Equal(new[] { TenantRoleTabs.Dashboard }, outcome.Response!.User.Tabs);
        Assert.Equal(
            new[] { TenantRoleCapabilities.UsersManage, TenantRoleCapabilities.OrdersManage },
            outcome.Response!.User.Capabilities);
    }
}
