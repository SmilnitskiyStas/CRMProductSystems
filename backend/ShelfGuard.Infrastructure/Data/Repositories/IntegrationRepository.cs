using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class IntegrationRepository : IIntegrationRepository
{
    private readonly AppDbContext _db;

    public IntegrationRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<IntegrationConfig>> GetAllByTenantAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        return await _db.IntegrationConfigs
            .Where(i => i.TenantId == tenantId)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IntegrationConfig?> GetByServiceAsync(
        Guid tenantId, string service, CancellationToken ct = default)
    {
        return await _db.IntegrationConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Service == service, ct);
    }

    public async Task UpsertAsync(
        Guid tenantId, string service, string config, bool isEnabled, CancellationToken ct = default)
    {
        var existing = await _db.IntegrationConfigs
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Service == service, ct);

        if (existing is not null)
        {
            existing.Config    = config;
            existing.IsEnabled = isEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.IntegrationConfigs.Add(new IntegrationConfig
            {
                TenantId  = tenantId,
                Service   = service,
                Config    = config,
                IsEnabled = isEnabled,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(
        Guid tenantId, string service, CancellationToken ct = default)
    {
        var existing = await _db.IntegrationConfigs
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Service == service, ct);

        if (existing is null) return false;

        _db.IntegrationConfigs.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
