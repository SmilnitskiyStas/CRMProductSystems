using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Cross-tenant repository for the SaaS Admin Panel.
/// Queries run without tenant-scoped RLS (provider-level access).
/// Only used by TenantAdminService (role = provider).
/// </summary>
public sealed class TenantAdminRepository : ITenantAdminRepository
{
    private readonly AppDbContext _db;

    public TenantAdminRepository(AppDbContext db) => _db = db;

    // ── Tenants ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Tenant>> GetAllTenantsAsync(CancellationToken ct) =>
        await _db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<Tenant?> GetTenantByIdAsync(Guid id, CancellationToken ct) =>
        await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        await _db.Tenants.AnyAsync(t => t.Slug == slug, ct);

    public async Task AddTenantAsync(Tenant tenant, CancellationToken ct) =>
        await _db.Tenants.AddAsync(tenant, ct);

    public async Task AddUserAsync(User user, CancellationToken ct) =>
        await _db.Users.AddAsync(user, ct);

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);

    // ── Usage stats ───────────────────────────────────────────────────────────

    public Task<int> CountUsersAsync(Guid tenantId, CancellationToken ct) =>
        _db.Users.CountAsync(u => u.TenantId == tenantId, ct);

    public Task<int> CountStoresAsync(Guid tenantId, CancellationToken ct) =>
        _db.Stores.CountAsync(s => s.TenantId == tenantId, ct);

    public Task<int> CountProductsAsync(Guid tenantId, CancellationToken ct) =>
        _db.CatalogProducts.CountAsync(p => p.TenantId == tenantId, ct);

    public Task<int> CountSalesLast30DaysAsync(Guid tenantId, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        return _db.PosTransactions.CountAsync(
            p => p.TenantId == tenantId && p.CreatedAt > cutoff, ct);
    }
}
