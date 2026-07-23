using NSubstitute;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Locations;

/// <summary>
/// TASK-392b: LocationService.BelongsToTenantAsync — mirrors ILegalEntityService's method of
/// the same name exactly (see LegalEntityService.BelongsToTenantAsync). Closes the previously
/// unchecked gap where UserService.InviteAsync/UpdateAsync accepted any GUID as StoreId with
/// no tenant-ownership check at all.
/// </summary>
public sealed class LocationServiceTests
{
    private readonly ILocationRepository _repo = Substitute.For<ILocationRepository>();
    private readonly IUserLocationRepository _userLocations = Substitute.For<IUserLocationRepository>();
    private readonly LocationService _sut;

    public LocationServiceTests()
    {
        _sut = new LocationService(_repo, _userLocations);
    }

    [Fact]
    public async Task BelongsToTenantAsync_SameTenant_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        var location = new Location { TenantId = tenantId };
        _repo.GetByIdAsync(location.Id, Arg.Any<CancellationToken>()).Returns(location);

        var result = await _sut.BelongsToTenantAsync(tenantId, location.Id);

        Assert.True(result);
    }

    [Fact]
    public async Task BelongsToTenantAsync_DifferentTenant_ReturnsFalse()
    {
        var location = new Location { TenantId = Guid.NewGuid() };
        _repo.GetByIdAsync(location.Id, Arg.Any<CancellationToken>()).Returns(location);

        var result = await _sut.BelongsToTenantAsync(Guid.NewGuid(), location.Id);

        Assert.False(result);
    }

    [Fact]
    public async Task BelongsToTenantAsync_LocationNotFound_ReturnsFalse()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Location?)null);

        var result = await _sut.BelongsToTenantAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result);
    }
}
