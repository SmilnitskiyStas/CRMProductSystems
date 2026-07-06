using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Supplier contract requisites (TASK-316). Single-tenant RLS applies via
/// TenantConnectionInterceptor (tenant_isolation on TenantId).
/// </summary>
public sealed class SupplierContractSettingsRepository : ISupplierContractSettingsRepository
{
    private readonly AppDbContext _db;

    public SupplierContractSettingsRepository(AppDbContext db) => _db = db;

    public Task<SupplierContractSettings?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.SupplierContractSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public async Task AddAsync(SupplierContractSettings settings, CancellationToken ct = default) =>
        await _db.SupplierContractSettings.AddAsync(settings, ct);

    public void Update(SupplierContractSettings settings) =>
        _db.SupplierContractSettings.Update(settings);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
