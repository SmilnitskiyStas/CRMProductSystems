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
/// TASK-392b (Feature 2 Stage 1, store-scoped user↔location assignment — schema/plumbing only,
/// enforcement RLS is Stage 3): covers (1) StoreId tenant-ownership validation in
/// InviteAsync/UpdateAsync (closes the previously-unchecked "any GUID accepted" gap, same
/// pattern as LegalEntityId), (2) the single user_locations row kept in sync for
/// store_manager-and-below via SyncSingleLocationAsync, and (3) the dedicated
/// SetLocationsAsync/GetLocationsAsync full-replace path used by network_manager (and callable
/// for any rank — the controller is the AtLeastEnterpriseAdmin gate, not this service method).
/// </summary>
public sealed class UserServiceLocationsTests
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

    public UserServiceLocationsTests()
    {
        _sut = new UserService(_users, _activityLogs, _hasher, _legalEntities, _refreshTokens, _permissionGrants, _tenantRoles, _locations, _userLocations);
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");
    }

    // ── InviteAsync / UpdateAsync: StoreId tenant-ownership validation ─────────

    [Fact]
    public async Task InviteAsync_StoreIdNotInTenant_ReturnsError_DoesNotCreateUser()
    {
        var actingUser = MakeUser("enterprise_admin");
        var badStoreId = Guid.NewGuid();
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);
        _locations.BelongsToTenantAsync(_tenantId, badStoreId, Arg.Any<CancellationToken>()).Returns(false);

        var request = new InviteUserRequest(
            Email: "bad.store@example.com", FullName: "Bad Store",
            Role: "store_manager", Password: StrongPassword, StoreId: badStoreId);

        var (user, error) = await _sut.InviteAsync(_tenantId, actingUser.Id, request, "Inviter", default);

        Assert.Null(user);
        Assert.Equal("Вказана локація не належить цьому тенанту.", error);
        await _users.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _userLocations.DidNotReceive().ReplaceForUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_StoreIdNotInTenant_ReturnsError_DoesNotUpdate()
    {
        var actingUser = MakeUser("enterprise_admin");
        var target = MakeUser("store_manager");
        var badStoreId = Guid.NewGuid();
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);
        _locations.BelongsToTenantAsync(_tenantId, badStoreId, Arg.Any<CancellationToken>()).Returns(false);

        var request = new UpdateUserRequest(
            FullName: target.FullName, Phone: null, Role: target.Role, StoreId: badStoreId);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, target.Id, request, default);

        Assert.Null(user);
        Assert.Equal("Вказана локація не належить цьому тенанту.", error);
        _users.DidNotReceive().Update(Arg.Any<User>());
        await _userLocations.DidNotReceive().ReplaceForUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // ── SyncSingleLocationAsync (via InviteAsync/UpdateAsync) ──────────────────

    [Fact]
    public async Task InviteAsync_StoreManagerWithValidStore_WritesSingleUserLocationRow()
    {
        var actingUser = MakeUser("enterprise_admin");
        var storeId = Guid.NewGuid();
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);
        _locations.BelongsToTenantAsync(_tenantId, storeId, Arg.Any<CancellationToken>()).Returns(true);

        var request = new InviteUserRequest(
            Email: "new.manager@example.com", FullName: "New Manager",
            Role: "store_manager", Password: StrongPassword, StoreId: storeId);

        var (user, error) = await _sut.InviteAsync(_tenantId, actingUser.Id, request, "Inviter", default);

        Assert.Null(error);
        Assert.NotNull(user);
        await _userLocations.Received(1).ReplaceForUserAsync(
            _tenantId, user!.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(storeId)),
            actingUser.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteAsync_StoreManagerWithoutStore_ReplacesWithEmptySet()
    {
        var actingUser = MakeUser("enterprise_admin");
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);

        var request = new InviteUserRequest(
            Email: "no.store@example.com", FullName: "No Store",
            Role: "store_manager", Password: StrongPassword); // StoreId omitted -> null

        var (user, error) = await _sut.InviteAsync(_tenantId, actingUser.Id, request, "Inviter", default);

        Assert.Null(error);
        Assert.NotNull(user);
        await _locations.DidNotReceive().BelongsToTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _userLocations.Received(1).ReplaceForUserAsync(
            _tenantId, user!.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
            actingUser.Id, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("network_manager")]
    [InlineData("enterprise_admin")]
    public async Task InviteAsync_RoleOutsideSingleLocationSet_DoesNotWriteUserLocations(string role)
    {
        var actingUser = MakeUser("enterprise_admin");
        var storeId = Guid.NewGuid();
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);
        _locations.BelongsToTenantAsync(_tenantId, storeId, Arg.Any<CancellationToken>()).Returns(true);

        var request = new InviteUserRequest(
            Email: $"{role}@example.com", FullName: "Someone",
            Role: role, Password: StrongPassword, StoreId: storeId);

        var (user, error) = await _sut.InviteAsync(_tenantId, actingUser.Id, request, "Inviter", default);

        Assert.Null(error);
        Assert.NotNull(user);
        await _userLocations.DidNotReceive().ReplaceForUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_StoreManagerStoreChanged_ReplacesUserLocationRow()
    {
        var actingUser = MakeUser("enterprise_admin");
        var target = MakeUser("store_manager");
        var newStoreId = Guid.NewGuid();
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);
        _locations.BelongsToTenantAsync(_tenantId, newStoreId, Arg.Any<CancellationToken>()).Returns(true);

        var request = new UpdateUserRequest(
            FullName: target.FullName, Phone: null, Role: target.Role, StoreId: newStoreId);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, target.Id, request, default);

        Assert.Null(error);
        Assert.NotNull(user);
        await _userLocations.Received(1).ReplaceForUserAsync(
            _tenantId, target.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(newStoreId)),
            actingUser.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_StoreClearedForStoreManager_ReplacesWithEmptySet()
    {
        var actingUser = MakeUser("enterprise_admin");
        var target = MakeUser("store_manager");
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);

        var request = new UpdateUserRequest(
            FullName: target.FullName, Phone: null, Role: target.Role, StoreId: null);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, target.Id, request, default);

        Assert.Null(error);
        Assert.NotNull(user);
        await _userLocations.Received(1).ReplaceForUserAsync(
            _tenantId, target.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
            actingUser.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NetworkManagerRoleUnchanged_DoesNotWriteUserLocations()
    {
        var actingUser = MakeUser("enterprise_admin");
        var target = MakeUser("network_manager");
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);

        var request = new UpdateUserRequest(
            FullName: "Updated", Phone: null, Role: target.Role, StoreId: null);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, target.Id, request, default);

        Assert.Null(error);
        Assert.NotNull(user);
        await _userLocations.DidNotReceive().ReplaceForUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RoleChangedFromNetworkManagerToStoreManager_CollapsesToSingleRow()
    {
        var actingUser = MakeUser("enterprise_admin");
        var target = MakeUser("network_manager");
        var storeId = Guid.NewGuid();
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);
        _locations.BelongsToTenantAsync(_tenantId, storeId, Arg.Any<CancellationToken>()).Returns(true);

        var request = new UpdateUserRequest(
            FullName: target.FullName, Phone: null, Role: "store_manager", StoreId: storeId);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, target.Id, request, default);

        Assert.Null(error);
        Assert.NotNull(user);
        await _userLocations.Received(1).ReplaceForUserAsync(
            _tenantId, target.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(storeId)),
            actingUser.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_StoreManagerAlreadyHasMultipleLocations_DoesNotCollapseToSingleRow()
    {
        // TASK-397: once an admin has used the dedicated multi-select endpoint (SetLocationsAsync,
        // now available for every LocationScopedRoles member, not just network_manager) to give a
        // store_manager-tier user 2+ locations, an unrelated plain-profile save (name/phone/role/
        // legal entity) through this endpoint must not silently collapse that back down to one row
        // via the legacy single-location auto-sync — SyncSingleLocationAsync's new guard checks the
        // existing row count first and steps aside once it's already 2+.
        var actingUser = MakeUser("enterprise_admin");
        var target = MakeUser("store_manager");
        var storeId = Guid.NewGuid();
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);
        _locations.BelongsToTenantAsync(_tenantId, storeId, Arg.Any<CancellationToken>()).Returns(true);
        _userLocations.GetLocationIdsForUserAsync(_tenantId, target.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });

        var request = new UpdateUserRequest(
            FullName: "Updated Name", Phone: null, Role: target.Role, StoreId: storeId);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, target.Id, request, default);

        Assert.Null(error);
        Assert.NotNull(user);
        await _userLocations.DidNotReceive().ReplaceForUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_StoreManagerWithExactlyOneExistingLocation_StillSyncsNormally()
    {
        // Guard boundary check: exactly ONE pre-existing row is still the legacy shape (not yet
        // "graduated" to multi-location via SetLocationsAsync), so the plain Update endpoint must
        // keep auto-syncing it as before — only 2+ rows should suppress the sync.
        var actingUser = MakeUser("enterprise_admin");
        var target = MakeUser("store_manager");
        var newStoreId = Guid.NewGuid();
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);
        _locations.BelongsToTenantAsync(_tenantId, newStoreId, Arg.Any<CancellationToken>()).Returns(true);
        _userLocations.GetLocationIdsForUserAsync(_tenantId, target.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { Guid.NewGuid() });

        var request = new UpdateUserRequest(
            FullName: target.FullName, Phone: null, Role: target.Role, StoreId: newStoreId);

        var (user, error) = await _sut.UpdateAsync(_tenantId, actingUser.Id, target.Id, request, default);

        Assert.Null(error);
        Assert.NotNull(user);
        await _userLocations.Received(1).ReplaceForUserAsync(
            _tenantId, target.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(newStoreId)),
            actingUser.Id, Arg.Any<CancellationToken>());
    }

    // ── SetLocationsAsync (network_manager full-replace path) ──────────────────

    [Fact]
    public async Task SetLocationsAsync_TargetNotFound_ReturnsError()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var (success, error) = await _sut.SetLocationsAsync(_tenantId, Guid.NewGuid(), [Guid.NewGuid()], Guid.NewGuid());

        Assert.False(success);
        Assert.Equal("User not found.", error);
    }

    [Fact]
    public async Task SetLocationsAsync_TargetBelongsToDifferentTenant_ReturnsNotFound()
    {
        var target = MakeUser("network_manager", tenantId: Guid.NewGuid());
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        var (success, error) = await _sut.SetLocationsAsync(_tenantId, target.Id, [Guid.NewGuid()], Guid.NewGuid());

        Assert.False(success);
        Assert.Equal("User not found.", error);
    }

    [Fact]
    public async Task SetLocationsAsync_OneLocationNotInTenant_ReturnsError_DoesNotReplace()
    {
        var target = MakeUser("network_manager");
        var goodId = Guid.NewGuid();
        var badId = Guid.NewGuid();
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _locations.BelongsToTenantAsync(_tenantId, goodId, Arg.Any<CancellationToken>()).Returns(true);
        _locations.BelongsToTenantAsync(_tenantId, badId, Arg.Any<CancellationToken>()).Returns(false);

        var (success, error) = await _sut.SetLocationsAsync(_tenantId, target.Id, [goodId, badId], Guid.NewGuid());

        Assert.False(success);
        Assert.Equal("Вказана локація не належить цьому тенанту.", error);
        await _userLocations.DidNotReceive().ReplaceForUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetLocationsAsync_ValidList_ReplacesAndSaves()
    {
        var target = MakeUser("network_manager");
        var actingUserId = Guid.NewGuid();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _locations.BelongsToTenantAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var (success, error) = await _sut.SetLocationsAsync(_tenantId, target.Id, [id1, id2], actingUserId);

        Assert.True(success);
        Assert.Null(error);
        await _userLocations.Received(1).ReplaceForUserAsync(
            _tenantId, target.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(id1) && ids.Contains(id2)),
            actingUserId, Arg.Any<CancellationToken>());
        await _userLocations.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetLocationsAsync_DuplicateIds_DedupedBeforeReplace()
    {
        var target = MakeUser("network_manager");
        var id1 = Guid.NewGuid();
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _locations.BelongsToTenantAsync(_tenantId, id1, Arg.Any<CancellationToken>()).Returns(true);

        var (success, _) = await _sut.SetLocationsAsync(_tenantId, target.Id, [id1, id1, id1], Guid.NewGuid());

        Assert.True(success);
        await _userLocations.Received(1).ReplaceForUserAsync(
            _tenantId, target.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetLocationsAsync_EmptyList_ClearsAssignments()
    {
        var target = MakeUser("network_manager");
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        var (success, error) = await _sut.SetLocationsAsync(_tenantId, target.Id, [], Guid.NewGuid());

        Assert.True(success);
        Assert.Null(error);
        await _userLocations.Received(1).ReplaceForUserAsync(
            _tenantId, target.Id,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // ── GetLocationsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetLocationsAsync_TargetNotFound_ReturnsError()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var (ids, error) = await _sut.GetLocationsAsync(_tenantId, Guid.NewGuid());

        Assert.Null(ids);
        Assert.Equal("User not found.", error);
    }

    [Fact]
    public async Task GetLocationsAsync_TargetBelongsToDifferentTenant_ReturnsNotFound()
    {
        var target = MakeUser("store_manager", tenantId: Guid.NewGuid());
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        var (ids, error) = await _sut.GetLocationsAsync(_tenantId, target.Id);

        Assert.Null(ids);
        Assert.Equal("User not found.", error);
    }

    [Fact]
    public async Task GetLocationsAsync_ValidTarget_ReturnsRepositoryList()
    {
        var target = MakeUser("store_manager");
        var expected = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _userLocations.GetLocationIdsForUserAsync(_tenantId, target.Id, Arg.Any<CancellationToken>())
            .Returns(expected);

        var (ids, error) = await _sut.GetLocationsAsync(_tenantId, target.Id);

        Assert.Null(error);
        Assert.Equal(expected, ids);
    }

    // ── NeedsLocationAssignment (TASK-395) ─────────────────────────────────────
    // GetAllAsync must batch its user_locations existence check into a single query (never one
    // per user); the single-user paths (GetById/Invite) each get one fresh query/read of the
    // just-committed state instead.

    [Fact]
    public async Task GetByIdAsync_RestrictedRoleWithLocationRow_NeedsLocationAssignmentFalse()
    {
        var target = MakeUser("store_manager");
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _userLocations.HasAnyLocationAsync(_tenantId, target.Id, Arg.Any<CancellationToken>()).Returns(true);

        var (user, error) = await _sut.GetByIdAsync(_tenantId, target.Id);

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.False(user!.NeedsLocationAssignment);
    }

    [Fact]
    public async Task GetByIdAsync_RestrictedRoleWithoutLocationRow_NeedsLocationAssignmentTrue()
    {
        var target = MakeUser("cashier");
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _userLocations.HasAnyLocationAsync(_tenantId, target.Id, Arg.Any<CancellationToken>()).Returns(false);

        var (user, error) = await _sut.GetByIdAsync(_tenantId, target.Id);

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.True(user!.NeedsLocationAssignment);
    }

    [Fact]
    public async Task GetByIdAsync_EnterpriseAdminWithoutAnyLocationRows_NeedsLocationAssignmentFalse()
    {
        var target = MakeUser("enterprise_admin");
        _users.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        // Deliberately NOT configuring HasAnyLocationAsync: enterprise_admin's role alone
        // settles the answer (unconditional bypass), so it must short-circuit before ever
        // querying — asserted below via DidNotReceive.

        var (user, error) = await _sut.GetByIdAsync(_tenantId, target.Id);

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.False(user!.NeedsLocationAssignment);
        await _userLocations.DidNotReceive().HasAnyLocationAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InviteAsync_StoreManagerWithoutStore_NeedsLocationAssignmentTrue()
    {
        var actingUser = MakeUser("enterprise_admin");
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);
        _userLocations.HasAnyLocationAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var request = new InviteUserRequest(
            Email: "no.store.gap@example.com", FullName: "No Store",
            Role: "store_manager", Password: StrongPassword); // StoreId omitted -> null

        var (user, error) = await _sut.InviteAsync(_tenantId, actingUser.Id, request, "Inviter", default);

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.True(user!.NeedsLocationAssignment);
    }

    [Fact]
    public async Task InviteAsync_StoreManagerWithValidStore_NeedsLocationAssignmentFalse()
    {
        var actingUser = MakeUser("enterprise_admin");
        var storeId = Guid.NewGuid();
        _users.GetByIdAsync(actingUser.Id, Arg.Any<CancellationToken>()).Returns(actingUser);
        _locations.BelongsToTenantAsync(_tenantId, storeId, Arg.Any<CancellationToken>()).Returns(true);
        _userLocations.HasAnyLocationAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var request = new InviteUserRequest(
            Email: "has.store.gap@example.com", FullName: "Has Store",
            Role: "store_manager", Password: StrongPassword, StoreId: storeId);

        var (user, error) = await _sut.InviteAsync(_tenantId, actingUser.Id, request, "Inviter", default);

        Assert.Null(error);
        Assert.NotNull(user);
        Assert.False(user!.NeedsLocationAssignment);
    }

    [Fact]
    public async Task GetAllAsync_MixOfRolesAndCoverage_SetsNeedsLocationAssignmentPerUser_OneBatchQuery()
    {
        var withLocation = MakeUser("store_manager");
        var withoutLocation = MakeUser("cashier");
        var admin = MakeUser("enterprise_admin");
        var users = new List<User> { withLocation, withoutLocation, admin };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);
        _userLocations.GetUserIdsWithAnyLocationAsync(
                _tenantId,
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Count == 2 && ids.Contains(withLocation.Id) && ids.Contains(withoutLocation.Id)),
                Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { withLocation.Id });

        var result = await _sut.GetAllAsync(_tenantId);

        Assert.False(result.Single(u => u.Id == withLocation.Id).NeedsLocationAssignment);
        Assert.True(result.Single(u => u.Id == withoutLocation.Id).NeedsLocationAssignment);
        Assert.False(result.Single(u => u.Id == admin.Id).NeedsLocationAssignment);
        await _userLocations.Received(1).GetUserIdsWithAnyLocationAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_NoUsersInLocationScopedRoles_SkipsUserLocationsQueryEntirely()
    {
        var admin = MakeUser("enterprise_admin");
        var supplier = MakeUser("supplier_admin");
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>())
            .Returns(new List<User> { admin, supplier });

        var result = await _sut.GetAllAsync(_tenantId);

        Assert.All(result, u => Assert.False(u.NeedsLocationAssignment));
        await _userLocations.DidNotReceive().GetUserIdsWithAnyLocationAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    private User MakeUser(string role, Guid? tenantId = null) =>
        User.Create(tenantId ?? _tenantId, $"{Guid.NewGuid()}@example.com", "Test User", "hash", role);
}
