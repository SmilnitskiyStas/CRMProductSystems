using NSubstitute;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Users;

/// <summary>
/// TASK-517: UserService.GetAllAsync's optional storeIds filter (header store selector, same
/// convention as PriceSegmentsController's <c>storeIds</c> — omitted/empty means "all stores").
/// Covers the filtering semantics only; TASK-395's NeedsLocationAssignment coverage (unaffected
/// by this filter — it always reflects the FULL, unfiltered assignment) already lives in
/// UserServiceLocationsTests.
/// </summary>
public sealed class UserServiceStoreFilterTests
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

    private readonly Guid _tenantId = Guid.NewGuid();

    public UserServiceStoreFilterTests()
    {
        _sut = new UserService(_users, _activityLogs, _hasher, _legalEntities, _refreshTokens, _permissionGrants, _tenantRoles, _locations, _userLocations);
    }

    [Fact]
    public async Task GetAllAsync_StoreIdsNull_ReturnsFullUnfilteredList()
    {
        var storeManagerA = MakeUser("store_manager");
        var storeManagerB = MakeUser("store_manager");
        var admin = MakeUser("enterprise_admin");
        var users = new List<User> { storeManagerA, storeManagerB, admin };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);

        var result = await _sut.GetAllAsync(_tenantId, storeIds: null);

        Assert.Equal(3, result.Count);
        await _userLocations.DidNotReceive().GetUserIdsWithLocationInAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_StoreIdsEmptyArray_SameAsNull_ReturnsFullList()
    {
        var storeManagerA = MakeUser("store_manager");
        var admin = MakeUser("enterprise_admin");
        var users = new List<User> { storeManagerA, admin };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);

        var result = await _sut.GetAllAsync(_tenantId, storeIds: []);

        Assert.Equal(2, result.Count);
        await _userLocations.DidNotReceive().GetUserIdsWithLocationInAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_StoreIdsFilter_IncludesUserAssignedToMatchingStore_ExcludesOther()
    {
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var managerAtA = MakeUser("store_manager");
        var managerAtB = MakeUser("store_manager");
        var users = new List<User> { managerAtA, managerAtB };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);
        _userLocations.GetUserIdsWithLocationInAsync(
                _tenantId,
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(storeA)),
                Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { managerAtA.Id });

        var result = await _sut.GetAllAsync(_tenantId, storeIds: [storeA]);

        Assert.Single(result);
        Assert.Equal(managerAtA.Id, result[0].Id);
    }

    [Fact]
    public async Task GetAllAsync_StoreIdsFilter_EnterpriseAdminAlwaysIncluded()
    {
        var storeA = Guid.NewGuid();
        var admin = MakeUser("enterprise_admin");
        var storeManagerElsewhere = MakeUser("store_manager");
        var users = new List<User> { admin, storeManagerElsewhere };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);
        // Nothing matches storeA at all — admin must still show up.
        _userLocations.GetUserIdsWithLocationInAsync(
                _tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid>());

        var result = await _sut.GetAllAsync(_tenantId, storeIds: [storeA]);

        Assert.Single(result);
        Assert.Equal(admin.Id, result[0].Id);
    }

    [Fact]
    public async Task GetAllAsync_StoreIdsFilter_UserWithZeroLocationRows_ExcludedWhenFiltered_ButIncludedWhenUnfiltered_NeedsLocationAssignmentTrueInBothCases()
    {
        var storeA = Guid.NewGuid();
        var unassigned = MakeUser("store_manager");
        var users = new List<User> { unassigned };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);
        // Zero rows anywhere: both the "any location" batch check and the "location in storeIds"
        // batch check come back empty for this user.
        _userLocations.GetUserIdsWithAnyLocationAsync(
                _tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid>());
        _userLocations.GetUserIdsWithLocationInAsync(
                _tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid>());

        var filtered = await _sut.GetAllAsync(_tenantId, storeIds: [storeA]);
        var unfiltered = await _sut.GetAllAsync(_tenantId, storeIds: null);

        Assert.Empty(filtered);
        Assert.Single(unfiltered);
        Assert.True(unfiltered[0].NeedsLocationAssignment);
    }

    // ── TASK-519 (security fix): actingUserId caller-scoping ───────────────────
    // GET /api/users previously trusted the caller-supplied storeIds at face value, letting any
    // store-bound role (store_manager and the rest of LocationScopedRoles) select "all stores" in
    // the header selector and see every employee in the tenant, or request an arbitrary store
    // they have no user_locations assignment to. These cases cover the new clamp.

    [Fact]
    public async Task GetAllAsync_ActingUserIdNull_UnchangedTask517Behavior()
    {
        var storeA = Guid.NewGuid();
        var managerAtA = MakeUser("store_manager");
        var admin = MakeUser("enterprise_admin");
        var users = new List<User> { managerAtA, admin };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);
        _userLocations.GetUserIdsWithLocationInAsync(
                _tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { managerAtA.Id });

        var result = await _sut.GetAllAsync(_tenantId, storeIds: [storeA], actingUserId: null);

        Assert.Equal(2, result.Count);
        await _userLocations.DidNotReceive().GetLocationIdsForUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_ScopedActingCaller_NoStoreIdsRequested_ClampsToOwnStore()
    {
        var storeA = Guid.NewGuid();
        var actingManager = MakeUser("store_manager");
        var colleagueAtA = MakeUser("store_manager");
        var colleagueAtB = MakeUser("store_manager");
        var admin = MakeUser("enterprise_admin");
        var users = new List<User> { actingManager, colleagueAtA, colleagueAtB, admin };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);

        _userLocations.GetLocationIdsForUserAsync(_tenantId, actingManager.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { storeA });
        _userLocations.GetUserIdsWithLocationInAsync(
                _tenantId,
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(storeA)),
                Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { actingManager.Id, colleagueAtA.Id });

        // "All stores" (no storeIds) from a store_manager must NOT mean the whole tenant.
        var result = await _sut.GetAllAsync(_tenantId, storeIds: null, actingUserId: actingManager.Id);

        Assert.Equal(3, result.Count); // actingManager + colleagueAtA + admin (always visible)
        Assert.Contains(result, u => u.Id == actingManager.Id);
        Assert.Contains(result, u => u.Id == colleagueAtA.Id);
        Assert.Contains(result, u => u.Id == admin.Id);
        Assert.DoesNotContain(result, u => u.Id == colleagueAtB.Id);
    }

    [Fact]
    public async Task GetAllAsync_ScopedActingCaller_RequestsStoreOutsideOwnScope_ReturnsNoLocationScopedUsers()
    {
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var actingManager = MakeUser("store_manager");
        var managerAtB = MakeUser("store_manager");
        var admin = MakeUser("enterprise_admin");
        var users = new List<User> { actingManager, managerAtB, admin };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);

        _userLocations.GetLocationIdsForUserAsync(_tenantId, actingManager.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { storeA });

        // Explicitly asking for a store they don't own — intersecting [storeB] with their own
        // [storeA] leaves nothing, so the request must fail closed rather than fall back to
        // "all stores" or leak storeB's actual users.
        var result = await _sut.GetAllAsync(_tenantId, storeIds: [storeB], actingUserId: actingManager.Id);

        Assert.Single(result);
        Assert.Equal(admin.Id, result[0].Id);
        await _userLocations.DidNotReceive().GetUserIdsWithLocationInAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_MultiStoreScopedActingCaller_NoStoreIdsRequested_SeesBothOwnStores()
    {
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var storeC = Guid.NewGuid();
        var actingManager = MakeUser("network_manager");
        var colleagueAtA = MakeUser("store_manager");
        var colleagueAtB = MakeUser("store_manager");
        var colleagueAtC = MakeUser("store_manager");
        var users = new List<User> { actingManager, colleagueAtA, colleagueAtB, colleagueAtC };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);

        _userLocations.GetLocationIdsForUserAsync(_tenantId, actingManager.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { storeA, storeB });
        _userLocations.GetUserIdsWithLocationInAsync(
                _tenantId,
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(storeA) && ids.Contains(storeB)),
                Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { actingManager.Id, colleagueAtA.Id, colleagueAtB.Id });

        var result = await _sut.GetAllAsync(_tenantId, storeIds: null, actingUserId: actingManager.Id);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, u => u.Id == actingManager.Id);
        Assert.Contains(result, u => u.Id == colleagueAtA.Id);
        Assert.Contains(result, u => u.Id == colleagueAtB.Id);
        Assert.DoesNotContain(result, u => u.Id == colleagueAtC.Id);
    }

    [Fact]
    public async Task GetAllAsync_ScopedActingCaller_ZeroOwnLocations_FailsClosed_StillSeesUnscopedRoles()
    {
        var actingManager = MakeUser("store_manager");
        var colleague = MakeUser("store_manager");
        var admin = MakeUser("enterprise_admin");
        var users = new List<User> { actingManager, colleague, admin };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);

        // TASK-395 backfill-gap cohort — a store_manager with zero user_locations rows at all.
        _userLocations.GetLocationIdsForUserAsync(_tenantId, actingManager.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Guid>());

        var result = await _sut.GetAllAsync(_tenantId, storeIds: null, actingUserId: actingManager.Id);

        Assert.Single(result);
        Assert.Equal(admin.Id, result[0].Id);
        await _userLocations.DidNotReceive().GetUserIdsWithLocationInAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_UnscopedActingCaller_NoStoreIdsRequested_SeesEveryone()
    {
        var actingAdmin = MakeUser("enterprise_admin");
        var managerA = MakeUser("store_manager");
        var managerB = MakeUser("store_manager");
        var users = new List<User> { actingAdmin, managerA, managerB };
        _users.GetAllByTenantAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(users);

        // enterprise_admin is not in LocationScopedRoles — today's unrestricted behavior.
        var result = await _sut.GetAllAsync(_tenantId, storeIds: null, actingUserId: actingAdmin.Id);

        Assert.Equal(3, result.Count);
        await _userLocations.DidNotReceive().GetLocationIdsForUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _userLocations.DidNotReceive().GetUserIdsWithLocationInAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    private User MakeUser(string role, Guid? tenantId = null) =>
        User.Create(tenantId ?? _tenantId, $"{Guid.NewGuid()}@example.com", "Test User", "hash", role);
}
