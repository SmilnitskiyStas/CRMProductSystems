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
/// TASK-347 (security hotfix): closes the ADR-020 "users.manage" TenantRole-capability
/// escalation chain — a low-rank capability holder that cleared the coarse
/// EnterpriseAdminOrUsersManage/StoreManagerOrUsersManage policy gate could previously
/// Invite/Update/Deactivate a user of ANY rank, including higher than their own, with no
/// server-side RoleRank check at all. Covers the three exploit paths directly plus the
/// supplier_admin flat-hierarchy exemption the fix required (see
/// UserService.IsExemptFromOutrankGate).
/// </summary>
public sealed class UserServiceEscalationTests
{
    private const string StrongPassword = "Xk7#mQp29Lvz"; // 12 chars, letter+digit, not common

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

    private readonly Guid _tenantId = Guid.NewGuid();

    public UserServiceEscalationTests()
    {
        _sut = new UserService(_users, _activityLogs, _hasher, _legalEntities, _refreshTokens, _permissionGrants, _tenantRoles, _locations, _userLocations);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");
    }

    // ── InviteAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task InviteAsync_RoleHigherThanActingUsersRank_ReturnsError_DoesNotCreateUser()
    {
        var actingUser = MakeUser("store_manager");
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);

        var request = new InviteUserRequest(
            Email: "new.admin@example.com",
            FullName: "New Admin",
            Role: "enterprise_admin", // higher rank than store_manager
            Password: StrongPassword);

        var (user, error) = await _sut.InviteAsync(_tenantId, actingUser.Id, request, "Inviter", default);

        Assert.Null(user);
        Assert.Equal("You do not have permission to invite a role higher than your own ('store_manager').", error);
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), default);
    }

    [Fact]
    public async Task InviteAsync_RoleAtOrBelowActingUsersRank_Succeeds()
    {
        var actingUser = MakeUser("enterprise_admin");
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);
        _users.GetByEmailAsync("new.manager@example.com", default).Returns((User?)null);

        var request = new InviteUserRequest(
            Email: "new.manager@example.com",
            FullName: "New Manager",
            Role: "store_manager",
            Password: StrongPassword);

        var (user, error) = await _sut.InviteAsync(_tenantId, actingUser.Id, request, "Inviter", default);

        Assert.Null(error);
        Assert.NotNull(user);
        await _users.Received(1).AddAsync(Arg.Any<User>(), default);
    }

    // ── UpdateAsync: self-update role-change block ────────────────────────────

    [Fact]
    public async Task UpdateAsync_SelfUpdate_CannotChangeOwnRole_EvenDemotion()
    {
        var actingUser = MakeUser("store_manager");
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);

        var request = new UpdateUserRequest(
            FullName: actingUser.FullName,
            Phone: null,
            Role: "cashier", // demotion attempt on self
            StoreId: null);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, actingUser.Id, request, default);

        Assert.Null(user);
        Assert.Equal("You do not have permission to change your own role.", error);
        _users.DidNotReceive().Update(Arg.Any<User>());
    }

    [Fact]
    public async Task UpdateAsync_SelfUpdate_NonRoleFields_StillApply()
    {
        var actingUser = MakeUser("store_manager");
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);

        var request = new UpdateUserRequest(
            FullName: "Updated Name",
            Phone: "+380501112233",
            Role: actingUser.Role, // unchanged
            StoreId: null);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, actingUser.Id, request, default);

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.Equal("Updated Name", user!.FullName);
    }

    // ── UpdateAsync: outrank gate on another user ─────────────────────────────

    [Fact]
    public async Task UpdateAsync_CannotAssignRoleHigherThanActingUsersOwnRank()
    {
        var actingUser = MakeUser("store_manager");
        var target = MakeUser("cashier");
        _users.GetByIdAsync(target.Id, default).Returns(target);
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);

        var request = new UpdateUserRequest(
            FullName: target.FullName,
            Phone: null,
            Role: "enterprise_admin", // higher than actingUser's own store_manager rank
            StoreId: null);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, target.Id, request, default);

        Assert.Null(user);
        Assert.Equal("You do not have permission to assign a role higher than your own ('store_manager').", error);
    }

    [Fact]
    public async Task UpdateAsync_ActorMustOutrankTargetsCurrentRole_EqualRank_ReturnsError()
    {
        var actingUser = MakeUser("store_manager");
        var target = MakeUser("store_manager"); // same rank as actor
        _users.GetByIdAsync(target.Id, default).Returns(target);
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);

        var request = new UpdateUserRequest(
            FullName: "New Name",
            Phone: null,
            Role: target.Role,
            StoreId: null);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, target.Id, request, default);

        Assert.Null(user);
        Assert.Equal("You do not have permission to edit a 'store_manager' user.", error);
    }

    [Fact]
    public async Task UpdateAsync_SupplierAdminPeer_NonRoleFields_Allowed()
    {
        // Regression guard for the TASK-347 fix: supplier_admin is a flat, single-role
        // domain (ADR-016) — RoleRank has no entry for it, so two peers must not be blocked
        // by the generic outrank gate (see UserService.IsExemptFromOutrankGate).
        var owner = MakeUser("supplier_admin");
        var teammate = MakeUser("supplier_admin");
        _users.GetByIdAsync(teammate.Id, default).Returns(teammate);
        _users.GetByIdAsync(owner.Id, default).Returns(owner);

        var request = new UpdateUserRequest(
            FullName: "Updated Teammate",
            Phone: null,
            Role: teammate.Role,
            StoreId: null);

        var (user, error) = await _sut.UpdateAsync(_tenantId, owner.Id, teammate.Id, request, default);

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.Equal("Updated Teammate", user!.FullName);
    }

    // ── DeactivateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_Self_ReturnsError()
    {
        var actingUser = MakeUser("enterprise_admin");

        var error = await _sut.DeactivateAsync(_tenantId, actingUser.Id, actingUser.Id, default);

        Assert.Equal("You do not have permission to deactivate your own account.", error);
    }

    [Fact]
    public async Task DeactivateAsync_EqualRankTarget_ReturnsError_DoesNotDeactivate()
    {
        var actingUser = MakeUser("store_manager");
        var target = MakeUser("store_manager");
        _users.GetByIdAsync(target.Id, default).Returns(target);
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);

        var error = await _sut.DeactivateAsync(_tenantId, actingUser.Id, target.Id, default);

        Assert.Equal("You do not have permission to deactivate a 'store_manager' user.", error);
        _users.DidNotReceive().Update(Arg.Any<User>());
        Assert.True(target.IsActive);
    }

    [Fact]
    public async Task DeactivateAsync_HigherRankTarget_ReturnsError_DoesNotDeactivate()
    {
        var actingUser = MakeUser("storekeeper"); // rank 1
        var target = MakeUser("enterprise_admin"); // rank 4
        _users.GetByIdAsync(target.Id, default).Returns(target);
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);

        var error = await _sut.DeactivateAsync(_tenantId, actingUser.Id, target.Id, default);

        Assert.Equal("You do not have permission to deactivate a 'enterprise_admin' user.", error);
        _users.DidNotReceive().Update(Arg.Any<User>());
    }

    [Fact]
    public async Task DeactivateAsync_LowerRankTarget_Succeeds()
    {
        var actingUser = MakeUser("store_manager"); // rank 2
        var target = MakeUser("cashier"); // rank 1
        _users.GetByIdAsync(target.Id, default).Returns(target);
        _users.GetByIdAsync(actingUser.Id, default).Returns(actingUser);

        var error = await _sut.DeactivateAsync(_tenantId, actingUser.Id, target.Id, default);

        Assert.Null(error);
        Assert.False(target.IsActive);
        _users.Received(1).Update(target);
    }

    [Fact]
    public async Task DeactivateAsync_SupplierAdminPeer_Allowed()
    {
        // Regression guard (TASK-347 fix): without IsExemptFromOutrankGate, this always
        // failed — SupplierCabinetController.DeactivateStaff is a live production endpoint
        // where caller and target are always both role=supplier_admin (rank 0 default on
        // both sides), so the plain "actingRank <= targetRank" check denied 100% of calls.
        var owner = MakeUser("supplier_admin");
        var teammate = MakeUser("supplier_admin");
        _users.GetByIdAsync(teammate.Id, default).Returns(teammate);
        _users.GetByIdAsync(owner.Id, default).Returns(owner);

        var error = await _sut.DeactivateAsync(_tenantId, owner.Id, teammate.Id, default);

        Assert.Null(error);
        Assert.False(teammate.IsActive);
        _users.Received(1).Update(teammate);
    }

    private User MakeUser(string role) =>
        User.Create(_tenantId, $"{Guid.NewGuid()}@example.com", "Test User", "hash", role);
}
