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

/// <summary>
/// TASK-351 (Block 1 pre-launch security audit): basic cross-tenant guard coverage — a caller
/// whose JWT `tenant_id` claim resolves to tenant A must not be able to read/mutate a user
/// belonging to tenant B by guessing/reusing that user's id. UserService already enforces this
/// via the "<c>target.TenantId != tenantId</c> → not found" check present in every method below
/// (independent of, and in addition to, RLS at the DB layer) — these tests just pin that
/// behavior so it can't silently regress. Full cross-tenant sweep across other controllers is
/// tracked separately for Block 2 (multi-tenancy/RLS audit).
/// </summary>
public sealed class UserServiceCrossTenantTests
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

    private readonly Guid _callerTenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();

    public UserServiceCrossTenantTests()
    {
        _sut = new UserService(_users, _activityLogs, _hasher, _legalEntities, _refreshTokens, _permissionGrants, _tenantRoles, _locations, _userLocations);
    }

    [Fact]
    public async Task GetByIdAsync_UserBelongsToDifferentTenant_ReturnsNotFound()
    {
        var foreignUser = MakeUser(_otherTenantId, "store_manager");
        _users.GetByIdAsync(foreignUser.Id, default).Returns(foreignUser);

        var (user, error) = await _sut.GetByIdAsync(_callerTenantId, foreignUser.Id, default);

        Assert.Null(user);
        Assert.Equal("User not found.", error);
    }

    [Fact]
    public async Task UpdateAsync_TargetBelongsToDifferentTenant_ReturnsNotFound_DoesNotUpdate()
    {
        var actingUser = MakeUser(_callerTenantId, "enterprise_admin");
        var foreignUser = MakeUser(_otherTenantId, "store_manager");
        _users.GetByIdAsync(foreignUser.Id, default).Returns(foreignUser);
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);

        var request = new UpdateUserRequest(
            FullName: "Hijacked Name", Phone: null, Role: foreignUser.Role, StoreId: null);

        var (user, error) = await _sut.UpdateAsync(_callerTenantId, actingUser.Id, foreignUser.Id, request, default);

        Assert.Null(user);
        Assert.Equal("User not found.", error);
        _users.DidNotReceive().Update(Arg.Any<User>());
    }

    [Fact]
    public async Task DeactivateAsync_TargetBelongsToDifferentTenant_ReturnsNotFound_DoesNotDeactivate()
    {
        var actingUser = MakeUser(_callerTenantId, "enterprise_admin");
        var foreignUser = MakeUser(_otherTenantId, "store_manager");
        _users.GetByIdAsync(foreignUser.Id, default).Returns(foreignUser);
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);

        var error = await _sut.DeactivateAsync(_callerTenantId, actingUser.Id, foreignUser.Id, default);

        Assert.Equal("User not found.", error);
        _users.DidNotReceive().Update(Arg.Any<User>());
        Assert.True(foreignUser.IsActive);
    }

    [Fact]
    public async Task GetActivityAsync_UserBelongsToDifferentTenant_ReturnsNotFound()
    {
        var foreignUser = MakeUser(_otherTenantId, "store_manager");
        _users.GetByIdAsync(foreignUser.Id, default).Returns(foreignUser);

        var (logs, error) = await _sut.GetActivityAsync(_callerTenantId, foreignUser.Id, 50, default);

        Assert.Empty(logs);
        Assert.Equal("User not found.", error);
    }

    [Fact]
    public async Task AssignTenantRoleAsync_TargetBelongsToDifferentTenant_ReturnsNotFound()
    {
        var foreignUser = MakeUser(_otherTenantId, "store_manager");
        _users.GetByIdAsync(foreignUser.Id, default).Returns(foreignUser);

        var (success, error) = await _sut.AssignTenantRoleAsync(_callerTenantId, foreignUser.Id, null, default);

        Assert.False(success);
        Assert.Equal("User not found.", error);
    }

    private static User MakeUser(Guid tenantId, string role) =>
        User.Create(tenantId, $"{Guid.NewGuid()}@example.com", "Test User", "hash", role);
}
