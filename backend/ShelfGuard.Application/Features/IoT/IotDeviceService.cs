using ShelfGuard.Application.Features.IoT.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.IoT;

public sealed class IotDeviceService : IIotDeviceService
{
    // Device with no message for this long is shown as offline (v3-spec §4 alert rule)
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(30);

    private readonly IIotDeviceRepository _repo;

    public IotDeviceService(IIotDeviceRepository repo) => _repo = repo;

    public async Task<List<IotDeviceDto>> GetAllAsync(Guid? storeId, CancellationToken ct = default)
    {
        var devices = await _repo.GetAllAsync(storeId, ct);
        return devices.Select(ToDto).ToList();
    }

    public async Task<IotDeviceDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var device = await _repo.GetByIdAsync(id, ct);
        return device is null ? null : ToDto(device);
    }

    public async Task<(IotDeviceDto? Device, string? Error)> RegisterAsync(
        Guid tenantId, RegisterDeviceRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            return (null, "DeviceId is required.");

        if (!IsValidDeviceType(request.DeviceType))
            return (null, $"Invalid device type '{request.DeviceType}'. Valid: weight_sensor, camera, temp_sensor, barcode_reader.");

        if (await _repo.DeviceIdExistsAsync(tenantId, request.DeviceId.Trim(), ct))
            return (null, $"Device '{request.DeviceId.Trim()}' is already registered.");

        var device = new IotDevice
        {
            TenantId = tenantId,
            StoreId = request.StoreId,
            ZoneId = request.ZoneId,
            DeviceType = request.DeviceType,
            DeviceId = request.DeviceId.Trim(),
            Name = request.Name?.Trim(),
            MqttTopic = request.MqttTopic?.Trim(),
            Config = request.Config,
        };

        await _repo.AddAsync(device, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(device), null);
    }

    public async Task<(IotDeviceDto? Device, string? Error)> UpdateAsync(
        Guid id, UpdateDeviceRequest request, CancellationToken ct = default)
    {
        var device = await _repo.GetByIdAsync(id, ct);
        if (device is null)
            return (null, "Device not found.");

        if (!IsValidDeviceType(request.DeviceType))
            return (null, $"Invalid device type '{request.DeviceType}'. Valid: weight_sensor, camera, temp_sensor, barcode_reader.");

        device.ZoneId = request.ZoneId;
        device.DeviceType = request.DeviceType;
        device.Name = request.Name?.Trim();
        device.MqttTopic = request.MqttTopic?.Trim();
        device.Config = request.Config;
        device.IsActive = request.IsActive;

        _repo.Update(device);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(device), null);
    }

    public async Task<(bool Success, string? Error)> DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var device = await _repo.GetByIdAsync(id, ct);
        if (device is null)
            return (false, "Device not found.");

        device.IsActive = false;
        _repo.Update(device);
        await _repo.SaveChangesAsync(ct);

        return (true, null);
    }

    public async Task<(List<TemperatureReadingDto>? Readings, string? Error)> GetTemperatureReadingsAsync(
        Guid deviceId, int hours, int limit, CancellationToken ct = default)
    {
        var device = await _repo.GetByIdAsync(deviceId, ct);
        if (device is null)
            return (null, "Device not found.");

        var from = DateTime.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 30));
        var readings = await _repo.GetTemperatureReadingsAsync(deviceId, from, Math.Clamp(limit, 1, 2000), ct);

        return (readings.Select(r => new TemperatureReadingDto(
            r.Id, r.DeviceId, r.Temperature, r.Humidity, r.IsAlert, r.RecordedAt)).ToList(), null);
    }

    public async Task<List<LatestTemperatureDto>> GetLatestTemperaturesAsync(
        Guid storeId, CancellationToken ct = default)
    {
        var devices = await _repo.GetAllAsync(storeId, ct);
        var deviceNames = devices.ToDictionary(d => d.Id, d => d.Name ?? d.DeviceId);

        var from = DateTime.UtcNow.AddHours(-24);
        var readings = await _repo.GetRecentTemperatureReadingsAsync(storeId, from, ct);

        // Latest reading per device, computed over the last-24h window
        return readings
            .GroupBy(r => r.DeviceId)
            .Select(g => g.OrderByDescending(r => r.RecordedAt).First())
            .Select(r => new LatestTemperatureDto(
                r.DeviceId,
                deviceNames.GetValueOrDefault(r.DeviceId),
                r.ZoneId,
                r.Temperature,
                r.Humidity,
                r.IsAlert,
                r.RecordedAt))
            .OrderBy(t => t.DeviceName)
            .ToList();
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private static IotDeviceDto ToDto(IotDevice d) => new(
        d.Id,
        d.StoreId,
        d.Store?.Name,
        d.ZoneId,
        d.Zone?.Name,
        d.DeviceType,
        d.DeviceId,
        d.Name,
        d.MqttTopic,
        d.Config,
        d.IsActive,
        d.LastSeenAt is not null && DateTime.UtcNow - d.LastSeenAt < OnlineWindow,
        d.LastSeenAt,
        d.BatteryLevel,
        d.FirmwareVersion,
        d.CreatedAt
    );

    private static bool IsValidDeviceType(string type) =>
        type is "weight_sensor" or "camera" or "temp_sensor" or "barcode_reader";
}
