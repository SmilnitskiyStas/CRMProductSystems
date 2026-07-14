using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for tenant-scoped custom role capability templates (ADR-020, TASK-345).
/// See <see cref="ITenantRoleRepository"/> for the transaction-boundary convention
/// (mutating methods do not call SaveChanges).
/// </summary>
public sealed class TenantRoleRepository : ITenantRoleRepository
{
    private readonly AppDbContext _db;

    public TenantRoleRepository(AppDbContext db) => _db = db;

    public Task<TenantRole?> GetByIdAsync(Guid tenantId, Guid roleId, CancellationToken ct = default) =>
        _db.TenantRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<TenantRole>> GetAllForTenantAsync(
        Guid tenantId, bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _db.TenantRoles.AsNoTracking().Where(r => r.TenantId == tenantId);

        if (!includeInactive)
            query = query.Where(r => r.IsActive);

        return await query.OrderBy(r => r.Name).ToListAsync(ct);
    }

    public Task<TenantRole?> GetByNameAsync(Guid tenantId, string name, CancellationToken ct = default) =>
        _db.TenantRoles.FirstOrDefaultAsync(
            r => r.TenantId == tenantId && r.Name == name && r.IsActive, ct);

    public async Task AddAsync(TenantRole role, CancellationToken ct = default) =>
        await _db.TenantRoles.AddAsync(role, ct);

    public Task UpdateAsync(TenantRole role, CancellationToken ct = default)
    {
        _db.TenantRoles.Update(role);
        return Task.CompletedTask;
    }

    public async Task<bool> DeactivateAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        var role = await _db.TenantRoles
            .FirstOrDefaultAsync(r => r.Id == roleId && r.TenantId == tenantId, ct);

        if (role is null || !role.IsActive)
            return false;

        role.Deactivate();
        return true;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
