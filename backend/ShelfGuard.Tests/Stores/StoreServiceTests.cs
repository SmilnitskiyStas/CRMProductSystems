using NSubstitute;
using ShelfGuard.Application.Features.Stores;
using ShelfGuard.Application.Features.Stores.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Stores;

public sealed class StoreServiceTests
{
    private readonly IStoreRepository _repo = Substitute.For<IStoreRepository>();
    private readonly StoreService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();

    public StoreServiceTests() => _sut = new StoreService(_repo);

    // ── Create Store ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsStore()
    {
        var req = new CreateStoreRequest("Магазин №1", "вул. Хрещатик 1", null, null, "shop");

        var (store, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(error);
        Assert.NotNull(store);
        Assert.Equal("Магазин №1", store.Name);
        Assert.Equal("shop", store.Type);
        await _repo.Received(1).AddAsync(
            Arg.Is<Store>(s => s.TenantId == _tenantId && s.Name == "Магазин №1"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CreateAsync_EmptyName_ReturnsError(string name)
    {
        var req = new CreateStoreRequest(name, null, null, null, "shop");
        var (store, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(store);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("hypermarket")]
    [InlineData("warehouse")]
    [InlineData("")]
    public async Task CreateAsync_InvalidType_ReturnsError(string type)
    {
        var req = new CreateStoreRequest("Store", null, null, null, type);
        var (store, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(store);
        Assert.Contains("type", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("shop")]
    [InlineData("central_warehouse")]
    [InlineData("production")]
    [InlineData("distribution")]
    public async Task CreateAsync_AllValidTypes_Succeed(string type)
    {
        var req = new CreateStoreRequest("Store", null, null, null, type);
        var (store, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(error);
        Assert.Equal(type, store!.Type);
    }

    // ── Update Store ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsError()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Store?)null);

        var req = new UpdateStoreRequest("Store", null, null, null, "shop", true);
        var (store, error) = await _sut.UpdateAsync(id, req);

        Assert.Null(store);
        Assert.Equal("Store not found.", error);
    }

    [Fact]
    public async Task UpdateAsync_Valid_UpdatesAndSaves()
    {
        var existing = new Store { TenantId = _tenantId, Name = "Old", Type = "shop" };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var req = new UpdateStoreRequest("New Name", "New Address", null, null, "production", true);
        var (store, error) = await _sut.UpdateAsync(existing.Id, req);

        Assert.Null(error);
        Assert.Equal("New Name", store!.Name);
        Assert.Equal("production", store.Type);
        _repo.Received(1).Update(existing);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Floor Plan ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateFloorPlanAsync_NotFound_ReturnsError()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Store?)null);

        var (store, error) = await _sut.UpdateFloorPlanAsync(id, new UpdateFloorPlanRequest("{}"));

        Assert.Null(store);
        Assert.Equal("Store not found.", error);
    }

    [Fact]
    public async Task UpdateFloorPlanAsync_Valid_SavesJson()
    {
        var existing = new Store { TenantId = _tenantId, Name = "Store", Type = "shop" };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var (store, error) = await _sut.UpdateFloorPlanAsync(existing.Id, new UpdateFloorPlanRequest("{\"zones\":[]}"));

        Assert.Null(error);
        Assert.Equal("{\"zones\":[]}", store!.FloorPlan);
    }

    // ── Create Zone ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateZoneAsync_StoreNotFound_ReturnsError()
    {
        var storeId = Guid.NewGuid();
        _repo.GetByIdAsync(storeId, Arg.Any<CancellationToken>()).Returns((Store?)null);

        var req = new CreateZoneRequest("Zone A", "shelf", null, 3, null, null);
        var (zone, error) = await _sut.CreateZoneAsync(storeId, req);

        Assert.Null(zone);
        Assert.Equal("Store not found.", error);
    }

    [Theory]
    [InlineData("room")]
    [InlineData("outdoor")]
    [InlineData("")]
    public async Task CreateZoneAsync_InvalidZoneType_ReturnsError(string type)
    {
        var store = new Store { TenantId = _tenantId, Name = "Store", Type = "shop" };
        _repo.GetByIdAsync(store.Id, Arg.Any<CancellationToken>()).Returns(store);

        var req = new CreateZoneRequest("Zone", type, null, 1, null, null);
        var (zone, error) = await _sut.CreateZoneAsync(store.Id, req);

        Assert.Null(zone);
        Assert.Contains("zone type", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("shelf")]
    [InlineData("fridge")]
    [InlineData("freezer")]
    [InlineData("display")]
    [InlineData("production")]
    [InlineData("warehouse")]
    public async Task CreateZoneAsync_AllValidTypes_Succeed(string type)
    {
        var store = new Store { TenantId = _tenantId, Name = "Store", Type = "shop" };
        _repo.GetByIdAsync(store.Id, Arg.Any<CancellationToken>()).Returns(store);

        var req = new CreateZoneRequest("Zone", type, null, 1, null, null);
        var (zone, error) = await _sut.CreateZoneAsync(store.Id, req);

        Assert.Null(error);
        Assert.Equal(type, zone!.Type);
        await _repo.Received(1).AddZoneAsync(
            Arg.Is<StoreZone>(z => z.StoreId == store.Id && z.Type == type),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateZoneAsync_ZeroShelvesCount_ReturnsError()
    {
        var store = new Store { TenantId = _tenantId, Name = "Store", Type = "shop" };
        _repo.GetByIdAsync(store.Id, Arg.Any<CancellationToken>()).Returns(store);

        var req = new CreateZoneRequest("Zone", "shelf", null, 0, null, null);
        var (zone, error) = await _sut.CreateZoneAsync(store.Id, req);

        Assert.Null(zone);
        Assert.NotNull(error);
    }

    // ── Delete Zone ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteZoneAsync_NotFound_ReturnsFalse()
    {
        var storeId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        _repo.GetZoneByIdAsync(zoneId, Arg.Any<CancellationToken>()).Returns((StoreZone?)null);

        var (success, error) = await _sut.DeleteZoneAsync(storeId, zoneId);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task DeleteZoneAsync_ZoneBelongsToDifferentStore_ReturnsFalse()
    {
        var storeId = Guid.NewGuid();
        var zone = new StoreZone { StoreId = Guid.NewGuid(), Name = "Zone", Type = "shelf" };
        _repo.GetZoneByIdAsync(zone.Id, Arg.Any<CancellationToken>()).Returns(zone);

        var (success, error) = await _sut.DeleteZoneAsync(storeId, zone.Id);

        Assert.False(success);
    }

    [Fact]
    public async Task DeleteZoneAsync_ValidZone_SetsInactive()
    {
        var storeId = Guid.NewGuid();
        var zone = new StoreZone { StoreId = storeId, Name = "Zone", Type = "shelf" };
        _repo.GetZoneByIdAsync(zone.Id, Arg.Any<CancellationToken>()).Returns(zone);

        var (success, error) = await _sut.DeleteZoneAsync(storeId, zone.Id);

        Assert.True(success);
        Assert.Null(error);
        Assert.False(zone.IsActive);
        _repo.Received(1).UpdateZone(zone);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Update Zone ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateZoneAsync_ZoneFromWrongStore_ReturnsError()
    {
        var storeId = Guid.NewGuid();
        var zone = new StoreZone { StoreId = Guid.NewGuid(), Name = "Zone", Type = "shelf" };
        _repo.GetZoneByIdAsync(zone.Id, Arg.Any<CancellationToken>()).Returns(zone);

        var req = new UpdateZoneRequest("New", "shelf", null, 1, null, null, true);
        var (updated, error) = await _sut.UpdateZoneAsync(storeId, zone.Id, req);

        Assert.Null(updated);
        Assert.Equal("Zone not found.", error);
    }
}
