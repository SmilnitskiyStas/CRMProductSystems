using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Provider-level repository — queries run under provider_bypass RLS policy
/// (app.role = 'provider' is set by TenantConnectionInterceptor for the provider JWT).
/// </summary>
public sealed class TenantRepository : ITenantRepository
{
    private readonly AppDbContext _db;

    public TenantRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct) =>
        await _db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <summary>
    /// Active user count per tenant in a single GROUP BY query.
    /// Excludes provider-role accounts (tenant_id IS NULL) from counts.
    /// </summary>
    public async Task<Dictionary<Guid, int>> GetUserCountsAsync(CancellationToken ct) =>
        await _db.Users
            .Where(u => u.IsActive && u.TenantId.HasValue && u.Role != "provider")
            .GroupBy(u => u.TenantId!.Value)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

    /// <summary>Active store count per tenant in a single GROUP BY query.</summary>
    public async Task<Dictionary<Guid, int>> GetStoreCountsAsync(CancellationToken ct) =>
        await _db.Locations
            .Where(s => s.IsActive)
            .GroupBy(s => s.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

    /// <summary>Expired batch count per tenant in a single GROUP BY query.</summary>
    public async Task<Dictionary<Guid, int>> GetExpiredBatchCountsAsync(CancellationToken ct) =>
        await _db.ProductStocks
            .Where(ps => ps.Status == "expired" && ps.Quantity > 0)
            .GroupBy(ps => ps.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

    /// <summary>Total active staff users across all tenants.</summary>
    public async Task<int> GetTotalUsersAsync(CancellationToken ct) =>
        await _db.Users
            .CountAsync(u => u.IsActive && u.TenantId.HasValue && u.Role != "provider", ct);

    /// <summary>Total expired batches with quantity > 0 across all tenants.</summary>
    public async Task<int> GetTotalExpiredBatchesAsync(CancellationToken ct) =>
        await _db.ProductStocks
            .CountAsync(ps => ps.Status == "expired" && ps.Quantity > 0, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
