using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IIotDeviceRepository
{
    Task<List<IotDevice>> GetAllAsync(Guid? storeId, CancellationToken ct = default);
    Task<IotDevice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeviceIdExistsAsync(Guid tenantId, string deviceId, CancellationToken ct = default);

    Task<List<TemperatureReading>> GetTemperatureReadingsAsync(
        Guid deviceId, DateTime from, int limit, CancellationToken ct = default);
    Task<List<TemperatureReading>> GetRecentTemperatureReadingsAsync(
        Guid storeId, DateTime from, CancellationToken ct = default);

    Task AddAsync(IotDevice device, CancellationToken ct = default);
    void Update(IotDevice device);
    Task SaveChangesAsync(CancellationToken ct = default);
}
