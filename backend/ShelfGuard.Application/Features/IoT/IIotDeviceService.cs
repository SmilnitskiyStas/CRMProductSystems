using ShelfGuard.Application.Features.IoT.Dtos;

namespace ShelfGuard.Application.Features.IoT;

public interface IIotDeviceService
{
    Task<List<IotDeviceDto>> GetAllAsync(Guid? storeId, CancellationToken ct = default);
    Task<IotDeviceDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(IotDeviceDto? Device, string? Error)> RegisterAsync(
        Guid tenantId, RegisterDeviceRequest request, CancellationToken ct = default);
    Task<(IotDeviceDto? Device, string? Error)> UpdateAsync(
        Guid id, UpdateDeviceRequest request, CancellationToken ct = default);
    Task<(bool Success, string? Error)> DeactivateAsync(Guid id, CancellationToken ct = default);

    Task<(List<TemperatureReadingDto>? Readings, string? Error)> GetTemperatureReadingsAsync(
        Guid deviceId, int hours, int limit, CancellationToken ct = default);
    Task<List<LatestTemperatureDto>> GetLatestTemperaturesAsync(
        Guid storeId, CancellationToken ct = default);
}
