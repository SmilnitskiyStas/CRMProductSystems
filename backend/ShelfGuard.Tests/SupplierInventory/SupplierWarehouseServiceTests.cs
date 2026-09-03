using NSubstitute;
using ShelfGuard.Application.Features.Locations;
using ShelfGuard.Application.Features.Locations.Dtos;
using ShelfGuard.Application.Features.SupplierInventory;
using ShelfGuard.Application.Features.SupplierInventory.Dtos;
using Xunit;

namespace ShelfGuard.Tests.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 1 (plan 1-partitioned-book.md, D1). SupplierWarehouseService
/// is a thin wrapper over ILocationService: it must force Location.Type = "warehouse" on writes,
/// list only warehouse-type rows, and reject any id that isn't an own-tenant warehouse.
/// </summary>
public sealed class SupplierWarehouseServiceTests
{
    private readonly ILocationService _locations = Substitute.For<ILocationService>();
    private readonly SupplierWarehouseService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();

    public SupplierWarehouseServiceTests() => _sut = new SupplierWarehouseService(_locations);

    private static LocationDto Loc(Guid id, string name, string type, bool isActive = true, string? region = null) =>
        new(id, name, "вул. Складська 1", null, null, type, null, isActive, DateTime.UtcNow, new(), null, region);

    // ── ListAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsOnlyWarehouseTypeLocations()
    {
        _locations.GetAllAsync(_tenantId, null, null, Arg.Any<CancellationToken>())
            .Returns(new List<LocationDto>
            {
                Loc(Guid.NewGuid(), "Основний склад", "warehouse"),
                Loc(Guid.NewGuid(), "Магазин на розі", "retail_store"),
                Loc(Guid.NewGuid(), "Склад №2", "warehouse", isActive: false, region: "UA-32"),
            });

        var result = await _sut.ListAsync(_tenantId);

        Assert.Equal(2, result.Count);
        Assert.All(result, w => Assert.Contains(w.Name, new[] { "Основний склад", "Склад №2" }));
        var inactive = Assert.Single(result, w => w.Name == "Склад №2");
        Assert.False(inactive.IsActive);
        Assert.Equal("UA-32", inactive.RegionCode);
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ForcesWarehouseLocationType()
    {
        CreateLocationRequest? captured = null;
        _locations.CreateAsync(_tenantId, Arg.Do<CreateLocationRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(ci => (Loc(Guid.NewGuid(), ci.Arg<CreateLocationRequest>().Name, "warehouse",
                region: ci.Arg<CreateLocationRequest>().RegionCode), (string?)null));

        var (warehouse, error) = await _sut.CreateAsync(
            _tenantId, new CreateSupplierWarehouseRequest("Центральний склад", "вул. Логістична 5", "UA-30"));

        Assert.Null(error);
        Assert.NotNull(warehouse);
        Assert.Equal("Центральний склад", warehouse!.Name);
        Assert.NotNull(captured);
        Assert.Equal("warehouse", captured!.LocationType);
        Assert.Equal("UA-30", captured.RegionCode);
    }

    [Fact]
    public async Task CreateAsync_PassesThroughLocationServiceError()
    {
        _locations.CreateAsync(_tenantId, Arg.Any<CreateLocationRequest>(), Arg.Any<CancellationToken>())
            .Returns((null, "Invalid region code 'XX'."));

        var (warehouse, error) = await _sut.CreateAsync(
            _tenantId, new CreateSupplierWarehouseRequest("Склад", null, "XX"));

        Assert.Null(warehouse);
        Assert.Equal("Invalid region code 'XX'.", error);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ForeignTenantId_ReturnsNotFound_AndNeverDelegates()
    {
        var id = Guid.NewGuid();
        _locations.BelongsToTenantAsync(_tenantId, id, Arg.Any<CancellationToken>()).Returns(false);

        var (warehouse, error) = await _sut.UpdateAsync(
            _tenantId, id, new UpdateSupplierWarehouseRequest("X", null, null, true));

        Assert.Null(warehouse);
        Assert.Equal(SupplierWarehouseService.WarehouseNotFoundError, error);
        await _locations.DidNotReceive().UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateLocationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_IdIsNotAWarehouse_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _locations.BelongsToTenantAsync(_tenantId, id, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Loc(id, "Магазин", "retail_store"));

        var (warehouse, error) = await _sut.UpdateAsync(
            _tenantId, id, new UpdateSupplierWarehouseRequest("X", null, null, true));

        Assert.Null(warehouse);
        Assert.Equal(SupplierWarehouseService.WarehouseNotFoundError, error);
        await _locations.DidNotReceive().UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateLocationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_OwnWarehouse_DelegatesWithWarehouseType()
    {
        var id = Guid.NewGuid();
        _locations.BelongsToTenantAsync(_tenantId, id, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(Loc(id, "Старий", "warehouse"));

        UpdateLocationRequest? captured = null;
        _locations.UpdateAsync(id, Arg.Do<UpdateLocationRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(ci => (Loc(id, ci.Arg<UpdateLocationRequest>().Name, "warehouse",
                ci.Arg<UpdateLocationRequest>().IsActive), (string?)null));

        var (warehouse, error) = await _sut.UpdateAsync(
            _tenantId, id, new UpdateSupplierWarehouseRequest("Новий", "вул. Нова 2", "UA-46", IsActive: true));

        Assert.Null(error);
        Assert.Equal("Новий", warehouse!.Name);
        Assert.NotNull(captured);
        Assert.Equal("warehouse", captured!.LocationType);
        Assert.Equal("Новий", captured.Name);
        Assert.Equal("UA-46", captured.RegionCode);
    }

    // ── DeactivateAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_ForeignTenantId_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _locations.BelongsToTenantAsync(_tenantId, id, Arg.Any<CancellationToken>()).Returns(false);

        var (success, error) = await _sut.DeactivateAsync(_tenantId, id);

        Assert.False(success);
        Assert.Equal(SupplierWarehouseService.WarehouseNotFoundError, error);
    }

    [Fact]
    public async Task DeactivateAsync_OwnWarehouse_SetsIsActiveFalseKeepingCurrentFields()
    {
        var id = Guid.NewGuid();
        _locations.BelongsToTenantAsync(_tenantId, id, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(Loc(id, "Склад для закриття", "warehouse", region: "UA-63"));

        UpdateLocationRequest? captured = null;
        _locations.UpdateAsync(id, Arg.Do<UpdateLocationRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns((Loc(id, "Склад для закриття", "warehouse", isActive: false), (string?)null));

        var (success, error) = await _sut.DeactivateAsync(_tenantId, id);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(captured);
        Assert.False(captured!.IsActive);
        Assert.Equal("Склад для закриття", captured.Name);
        Assert.Equal("UA-63", captured.RegionCode);
        Assert.Equal("warehouse", captured.LocationType);
    }
}
