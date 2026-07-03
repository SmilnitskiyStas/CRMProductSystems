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

    // BUG-014: the internal system tenant used as a home for provider-created
    // (pre-v4.1) suppliers (BUG-012) is never a real client — never surface it
    // in the provider tenant list.
    public async Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct) =>
        await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Slug != MarketplaceRepository.PlatformTenantSlug)
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

    public async Task AddAsync(Tenant tenant, CancellationToken ct)
    {
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        _db.Tenants.AnyAsync(t => t.Slug == slug.ToLowerInvariant(), ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    // v4.1 supplier self-service onboarding hook (ADR-016, TASK-289)

    public Task AddPendingAsync(Tenant tenant, CancellationToken ct)
    {
        _db.Tenants.Add(tenant);
        return Task.CompletedTask;
    }

    public async Task AddSupplierAsync(Supplier supplier, CancellationToken ct) =>
        await _db.Suppliers.AddAsync(supplier, ct);

    public async Task AddSupplierProfileAsync(SupplierProfile profile, CancellationToken ct) =>
        await _db.SupplierProfiles.AddAsync(profile, ct);
}
