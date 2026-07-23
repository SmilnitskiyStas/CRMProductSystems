using NSubstitute;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Locations;

/// <summary>
/// TASK-401 (ADR-022 Stage 3 companion): LocationService.GetAllAsync narrows the location
/// list for store-scoped roles (network_manager and below) to their user_locations
/// assignments. Three branches: (1) admin-tier roles always see everything, (2) scoped role
/// with ≥1 assignment sees only assigned locations, (3) scoped role with 0 assignments sees
/// everything (deliberate transitional fail-open — StoreSelector must not go empty before
/// the Stage 2 backfill completes; data protection itself is the RESTRICTIVE RLS layer).
/// </summary>
public sealed class LocationServiceGetAllScopeTests
{
    private readonly ILocationRepository _repo = Substitute.For<ILocationRepository>();
    private readonly IUserLocationRepository _userLocations = Substitute.For<IUserLocationRepository>();
    private readonly LocationService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Location _storeA;
    private readonly Location _storeB;
    private readonly Location _storeC;

    public LocationServiceGetAllScopeTests()
    {
        _sut = new LocationService(_repo, _userLocations);

        _storeA = new Location { TenantId = _tenantId, Name = "Store A" };
        _storeB = new Location { TenantId = _tenantId, Name = "Store B" };
        _storeC = new Location { TenantId = _tenantId, Name = "Store C" };

        _repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Location> { _storeA, _storeB, _storeC });
    }

    // ── branch 1: admin-tier roles see everything ──────────────────────────

    [Theory]
    [InlineData(AppRoles.Provider)]
    [InlineData(AppRoles.ProviderAdmin)]
    [InlineData(AppRoles.EnterpriseAdmin)]
    public async Task GetAllAsync_AdminTierRole_ReturnsAllLocations_WithoutAssignmentLookup(string role)
    {
        var result = await _sut.GetAllAsync(_tenantId, _userId, role);

        Assert.Equal(3, result.Count);
        await _userLocations.DidNotReceiveWithAnyArgs()
            .GetLocationIdsForUserAsync(default, default, default);
    }

    // ── branch 2: scoped role with assignments sees only assigned ──────────

    [Theory]
    [InlineData(AppRoles.NetworkManager)]
    [InlineData(AppRoles.StoreManager)]
    [InlineData(AppRoles.Merchandiser)]
    [InlineData(AppRoles.Storekeeper)]
    [InlineData(AppRoles.Cashier)]
    [InlineData(AppRoles.Staff)]
    public async Task GetAllAsync_ScopedRoleWithAssignments_ReturnsOnlyAssignedLocations(string role)
    {
        _userLocations.GetLocationIdsForUserAsync(_tenantId, _userId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { _storeA.Id, _storeC.Id });

        var result = await _sut.GetAllAsync(_tenantId, _userId, role);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, l => l.Id == _storeA.Id);
        Assert.Contains(result, l => l.Id == _storeC.Id);
        Assert.DoesNotContain(result, l => l.Id == _storeB.Id);
    }

    // ── branch 3: scoped role with zero assignments — fail-open ────────────

    [Fact]
    public async Task GetAllAsync_ScopedRoleWithoutAssignments_FailsOpen_ReturnsAllLocations()
    {
        _userLocations.GetLocationIdsForUserAsync(_tenantId, _userId, Arg.Any<CancellationToken>())
            .Returns(new List<Guid>());

        var result = await _sut.GetAllAsync(_tenantId, _userId, AppRoles.StoreManager);

        Assert.Equal(3, result.Count);
    }

    // ── defensive: missing claims never break the list ─────────────────────

    [Fact]
    public async Task GetAllAsync_ScopedRoleWithMissingTenantOrUserClaim_ReturnsAllLocations()
    {
        var withoutTenant = await _sut.GetAllAsync(null, _userId, AppRoles.Cashier);
        var withoutUser = await _sut.GetAllAsync(_tenantId, null, AppRoles.Cashier);

        Assert.Equal(3, withoutTenant.Count);
        Assert.Equal(3, withoutUser.Count);
        await _userLocations.DidNotReceiveWithAnyArgs()
            .GetLocationIdsForUserAsync(default, default, default);
    }
}
