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

    private User MakeUser(string role, Guid? tenantId = null) =>
        User.Create(tenantId ?? _tenantId, $"{Guid.NewGuid()}@example.com", "Test User", "hash", role);
}
