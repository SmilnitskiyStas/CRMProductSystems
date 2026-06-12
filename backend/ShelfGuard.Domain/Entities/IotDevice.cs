namespace ShelfGuard.Domain.Entities;

public sealed class IotDevice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public Guid? ZoneId { get; set; }
    // weight_sensor / camera / temp_sensor / barcode_reader
    public string DeviceType { get; set; } = string.Empty;
    // Physical device identifier (printed on the device, used in MQTT payloads)
    public string DeviceId { get; init; } = string.Empty;
    public string? Name { get; set; }
    public string? MqttTopic { get; set; }
    public string? Config { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastSeenAt { get; set; }
    public int? BatteryLevel { get; set; }
    public string? FirmwareVersion { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Navigation properties
    public Store? Store { get; init; }
    public StoreZone? Zone { get; init; }
}
