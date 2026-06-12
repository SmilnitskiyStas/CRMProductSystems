using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class IotDeviceRepository : IIotDeviceRepository
{
    private readonly AppDbContext _db;

    public IotDeviceRepository(AppDbContext db) => _db = db;

    public async Task<List<IotDevice>> GetAllAsync(Guid? storeId, CancellationToken ct = default) =>
        await _db.IotDevices
            .Include(d => d.Store)
            .Include(d => d.Zone)
            .Where(d => storeId == null || d.StoreId == storeId)
            .OrderBy(d => d.DeviceType).ThenBy(d => d.Name)
            .ToListAsync(ct);

    public async Task<IotDevice?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.IotDevices
            .Include(d => d.Store)
            .Include(d => d.Zone)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<bool> DeviceIdExistsAsync(Guid tenantId, string deviceId, CancellationToken ct = default) =>
        await _db.IotDevices.AnyAsync(d => d.TenantId == tenantId && d.DeviceId == deviceId, ct);

    public async Task<List<TemperatureReading>> GetTemperatureReadingsAsync(
        Guid deviceId, DateTime from, int limit, CancellationToken ct = default) =>
        await _db.TemperatureReadings
            .Where(r => r.DeviceId == deviceId && r.RecordedAt >= from)
            .OrderByDescending(r => r.RecordedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<List<TemperatureReading>> GetRecentTemperatureReadingsAsync(
        Guid storeId, DateTime from, CancellationToken ct = default) =>
        await _db.TemperatureReadings
            .Where(r => r.StoreId == storeId && r.RecordedAt >= from)
            .ToListAsync(ct);

    public async Task AddAsync(IotDevice device, CancellationToken ct = default) =>
        await _db.IotDevices.AddAsync(device, ct);

    public void Update(IotDevice device) => _db.IotDevices.Update(device);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
