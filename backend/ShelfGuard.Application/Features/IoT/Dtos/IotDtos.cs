namespace ShelfGuard.Application.Features.IoT.Dtos;

public sealed record IotDeviceDto(
    Guid Id,
    Guid StoreId,
    string? StoreName,
    Guid? ZoneId,
    string? ZoneName,
    string DeviceType,
    string DeviceId,
    string? Name,
    string? MqttTopic,
    string? Config,
    bool IsActive,
    bool IsOnline,
    DateTime? LastSeenAt,
    int? BatteryLevel,
    string? FirmwareVersion,
    DateTime CreatedAt
);

public sealed record RegisterDeviceRequest(
    Guid StoreId,
    Guid? ZoneId,
    string DeviceType,
    string DeviceId,
    string? Name,
    string? MqttTopic,
    string? Config
);

public sealed record UpdateDeviceRequest(
    Guid? ZoneId,
    string DeviceType,
    string? Name,
    string? MqttTopic,
    string? Config,
    bool IsActive
);

public sealed record TemperatureReadingDto(
    Guid Id,
    Guid DeviceId,
    decimal Temperature,
    decimal? Humidity,
    bool IsAlert,
    DateTime RecordedAt
);

// Latest reading per temp_sensor device of a store
public sealed record LatestTemperatureDto(
    Guid DeviceId,
    string? DeviceName,
    Guid? ZoneId,
    decimal Temperature,
    decimal? Humidity,
    bool IsAlert,
    DateTime RecordedAt
);
