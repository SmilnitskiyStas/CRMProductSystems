using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for tenant-scoped supplier staff roles (TASK-306). Standard tenant
/// RLS applies via TenantConnectionInterceptor — no explicit TenantId filter is
/// strictly required for the authenticated cabinet caller, but it is applied
/// explicitly anyway for defense in depth and to keep unit tests (InMemory
/// provider, no RLS) meaningful.
/// </summary>
public sealed class SupplierRolesRepository : ISupplierRolesRepository
{
    private readonly AppDbContext _db;

    public SupplierRolesRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SupplierRole>> GetAllAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.SupplierRoles
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.IsSystem ? 0 : 1).ThenBy(r => r.DisplayName)
            .ToListAsync(ct);

    public Task<SupplierRole?> GetByIdAsync(Guid tenantId, Guid roleId, CancellationToken ct = default) =>
        _db.SupplierRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.TenantId == tenantId, ct);

    public Task<bool> DisplayNameExistsAsync(Guid tenantId, string displayName, CancellationToken ct = default) =>
        _db.SupplierRoles.AnyAsync(r => r.TenantId == tenantId && r.DisplayName == displayName, ct);

    public Task<bool> IsAssignedToAnyUserAsync(Guid tenantId, Guid roleId, CancellationToken ct = default) =>
        _db.Users.AnyAsync(u => u.TenantId == tenantId && u.SupplierRoleId == roleId, ct);

    public async Task AddAsync(SupplierRole role, CancellationToken ct = default) =>
        await _db.SupplierRoles.AddAsync(role, ct);

    public void Remove(SupplierRole role) => _db.SupplierRoles.Remove(role);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
