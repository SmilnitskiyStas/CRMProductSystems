using NSubstitute;
using ShelfGuard.Application.Features.IoT;
using ShelfGuard.Application.Features.IoT.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.IoT;

public sealed class IotDeviceServiceTests
{
    private readonly IIotDeviceRepository _repo = Substitute.For<IIotDeviceRepository>();
    private readonly IotDeviceService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();

    public IotDeviceServiceTests() => _sut = new IotDeviceService(_repo);

    // ── Register ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_Valid_ReturnsDevice()
    {
        var req = new RegisterDeviceRequest(_storeId, null, "temp_sensor", "fridge-01", "Холодильник 1", null, null);

        var (device, error) = await _sut.RegisterAsync(_tenantId, req);

        Assert.Null(error);
        Assert.NotNull(device);
        Assert.Equal("fridge-01", device.DeviceId);
        Assert.False(device.IsOnline); // never seen yet
        await _repo.Received(1).AddAsync(
            Arg.Is<IotDevice>(d => d.TenantId == _tenantId && d.DeviceId == "fridge-01"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task RegisterAsync_EmptyDeviceId_ReturnsError(string deviceId)
    {
        var req = new RegisterDeviceRequest(_storeId, null, "temp_sensor", deviceId, null, null, null);
        var (device, error) = await _sut.RegisterAsync(_tenantId, req);

        Assert.Null(device);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("thermometer")]
    [InlineData("")]
    public async Task RegisterAsync_InvalidDeviceType_ReturnsError(string type)
    {
        var req = new RegisterDeviceRequest(_storeId, null, type, "dev-1", null, null, null);
        var (device, error) = await _sut.RegisterAsync(_tenantId, req);

        Assert.Null(device);
        Assert.Contains("type", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("weight_sensor")]
    [InlineData("camera")]
    [InlineData("temp_sensor")]
    [InlineData("barcode_reader")]
    public async Task RegisterAsync_AllValidTypes_Succeed(string type)
    {
        var req = new RegisterDeviceRequest(_storeId, null, type, $"dev-{type}", null, null, null);
        var (device, error) = await _sut.RegisterAsync(_tenantId, req);

        Assert.Null(error);
        Assert.Equal(type, device!.DeviceType);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateDeviceId_ReturnsError()
    {
        _repo.DeviceIdExistsAsync(_tenantId, "fridge-01", Arg.Any<CancellationToken>()).Returns(true);

        var req = new RegisterDeviceRequest(_storeId, null, "temp_sensor", "fridge-01", null, null, null);
        var (device, error) = await _sut.RegisterAsync(_tenantId, req);

        Assert.Null(device);
        Assert.Contains("already registered", error);
    }

    // ── Update / Deactivate ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsError()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((IotDevice?)null);

        var req = new UpdateDeviceRequest(null, "temp_sensor", null, null, null, true);
        var (device, error) = await _sut.UpdateAsync(id, req);

        Assert.Null(device);
        Assert.Equal("Device not found.", error);
    }

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse_NoHardDelete()
    {
        var existing = new IotDevice { TenantId = _tenantId, StoreId = _storeId, DeviceType = "temp_sensor", DeviceId = "fridge-01" };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var (success, error) = await _sut.DeactivateAsync(existing.Id);

        Assert.True(success);
        Assert.Null(error);
        Assert.False(existing.IsActive);
        _repo.Received(1).Update(existing);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Online window ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_RecentLastSeen_IsOnline()
    {
        var existing = new IotDevice
        {
            TenantId = _tenantId, StoreId = _storeId,
            DeviceType = "temp_sensor", DeviceId = "fridge-01",
            LastSeenAt = DateTime.UtcNow.AddMinutes(-5),
        };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var device = await _sut.GetByIdAsync(existing.Id);

        Assert.True(device!.IsOnline);
    }

    [Fact]
    public async Task GetByIdAsync_StaleLastSeen_IsOffline()
    {
        var existing = new IotDevice
        {
            TenantId = _tenantId, StoreId = _storeId,
            DeviceType = "temp_sensor", DeviceId = "fridge-01",
            LastSeenAt = DateTime.UtcNow.AddMinutes(-45),
        };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var device = await _sut.GetByIdAsync(existing.Id);

        Assert.False(device!.IsOnline);
    }

    // ── Latest temperatures ────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestTemperaturesAsync_ReturnsNewestPerDevice()
    {
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();
        _repo.GetAllAsync(_storeId, Arg.Any<CancellationToken>()).Returns(new List<IotDevice>());
        _repo.GetRecentTemperatureReadingsAsync(_storeId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<TemperatureReading>
            {
                new() { DeviceId = deviceA, StoreId = _storeId, Temperature = 4.0m, RecordedAt = DateTime.UtcNow.AddHours(-2) },
                new() { DeviceId = deviceA, StoreId = _storeId, Temperature = 9.5m, IsAlert = true, RecordedAt = DateTime.UtcNow.AddMinutes(-5) },
                new() { DeviceId = deviceB, StoreId = _storeId, Temperature = -18.0m, RecordedAt = DateTime.UtcNow.AddMinutes(-10) },
            });

        var latest = await _sut.GetLatestTemperaturesAsync(_storeId);

        Assert.Equal(2, latest.Count);
        var a = latest.Single(t => t.DeviceId == deviceA);
        Assert.Equal(9.5m, a.Temperature);
        Assert.True(a.IsAlert);
    }
}
