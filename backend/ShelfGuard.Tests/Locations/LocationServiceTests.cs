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
    private readonly IGeocodingClient _geocoder = Substitute.For<IGeocodingClient>();
    private readonly LocationService _sut;

    public LocationServiceTests()
    {
        _sut = new LocationService(_repo, _userLocations, _geocoder);
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

    // ── TASK-708: GeocodeAddressAsync — thin pass-through over IGeocodingClient ──

    [Fact]
    public async Task GeocodeAddressAsync_Resolved_MapsResult()
    {
        _geocoder.GeocodeAsync("Київ, Хрещатик 1", Arg.Any<CancellationToken>())
            .Returns(new GeocodeResult(50.45m, 30.52m, "Хрещатик, Київ, Україна"));

        var result = await _sut.GeocodeAddressAsync("  Київ, Хрещатик 1  ");

        Assert.NotNull(result);
        Assert.Equal(50.45m, result!.Latitude);
        Assert.Equal(30.52m, result.Longitude);
        Assert.Equal("Хрещатик, Київ, Україна", result.DisplayName);
    }

    [Fact]
    public async Task GeocodeAddressAsync_NotResolved_ReturnsNull()
    {
        _geocoder.GeocodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((GeocodeResult?)null);

        var result = await _sut.GeocodeAddressAsync("nowhere at all");

        Assert.Null(result);
    }

    [Fact]
    public async Task GeocodeAddressAsync_BlankQuery_ReturnsNull_WithoutCallingClient()
    {
        var result = await _sut.GeocodeAddressAsync("   ");

        Assert.Null(result);
        await _geocoder.DidNotReceive().GeocodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
