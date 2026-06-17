using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for the Supplier Marketplace feature.
///
/// Public listing methods (GetPublicSuppliersAsync, GetSupplierByIdAsync,
/// GetSupplierItemsAsync, SearchSuppliersAsync) set app.role = 'provider' before
/// executing the query so that the RLS provider_bypass policy allows cross-tenant
/// visibility of supplier_profiles/supplier_items rows where is_public = true.
///
/// Authenticated write methods (AddReviewAsync, GetOwnProfileAsync, etc.) rely on
/// the TenantConnectionInterceptor which sets app.tenant_id from the JWT, letting
/// the standard tenant_isolation RLS policy enforce access automatically.
/// </summary>
public sealed class MarketplaceRepository : IMarketplaceRepository
{
    private readonly AppDbContext _db;

    public MarketplaceRepository(AppDbContext db) => _db = db;

    // ── Public listing (provider-bypass RLS) ─────────────────────────────────

    public async Task<IReadOnlyList<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)>>
        GetPublicSuppliersAsync(
            string? region, string? category, string? plan,
            int page, int pageSize,
            CancellationToken ct = default)
    {
        await SetProviderRoleAsync(ct);

        var query = BuildPublicQuery(region, category, plan);

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return rows;
    }

    public async Task<int> CountPublicSuppliersAsync(
        string? region, string? category, string? plan,
        CancellationToken ct = default)
    {
        await SetProviderRoleAsync(ct);
        return await BuildPublicQuery(region, category, plan).CountAsync(ct);
    }

    public async Task<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)?>
        GetSupplierByIdAsync(Guid supplierId, CancellationToken ct = default)
    {
        await SetProviderRoleAsync(ct);

        var row = await _db.SupplierProfiles
            .AsNoTracking()
            .Where(p => p.SupplierId == supplierId)
            .Join(_db.Suppliers, p => p.SupplierId, s => s.Id,
                  (p, s) => new { Profile = p, Supplier = s })
            .GroupJoin(_db.SupplierMetrics, ps => ps.Profile.SupplierId, m => m.SupplierId,
                       (ps, metrics) => new { ps.Profile, ps.Supplier, Metrics = metrics })
            .SelectMany(x => x.Metrics.DefaultIfEmpty(),
                        (x, m) => new { x.Profile, x.Supplier, Metrics = m })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;
        return (row.Profile, row.Supplier, row.Metrics);
    }

    public async Task<IReadOnlyList<SupplierItem>> GetSupplierItemsAsync(
        Guid supplierId, CancellationToken ct = default)
    {
        await SetProviderRoleAsync(ct);

        return await _db.SupplierItems
            .AsNoTracking()
            .Include(i => i.Item)
            .Where(i => i.SupplierId == supplierId && i.IsAvailable)
            .OrderBy(i => i.CustomName ?? (i.Item != null ? i.Item.Name : string.Empty))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)>>
        SearchSuppliersAsync(string itemName, string? region, CancellationToken ct = default)
    {
        await SetProviderRoleAsync(ct);

        // Find supplier IDs that have matching items
        var matchingSupplierIds = await _db.SupplierItems
            .AsNoTracking()
            .Where(i => i.IsAvailable &&
                        (i.CustomName != null && EF.Functions.ILike(i.CustomName, $"%{itemName}%") ||
                         i.Item != null && EF.Functions.ILike(i.Item.Name, $"%{itemName}%")))
            .Select(i => i.SupplierId)
            .Distinct()
            .ToListAsync(ct);

        var query = _db.SupplierProfiles
            .AsNoTracking()
            .Where(p => p.IsPublic && matchingSupplierIds.Contains(p.SupplierId));

        if (!string.IsNullOrWhiteSpace(region))
            query = query.Where(p => p.Region != null && EF.Functions.ILike(p.Region, $"%{region}%"));

        var rows = await query
            .Join(_db.Suppliers, p => p.SupplierId, s => s.Id,
                  (p, s) => new { Profile = p, Supplier = s })
            .GroupJoin(_db.SupplierMetrics, ps => ps.Profile.SupplierId, m => m.SupplierId,
                       (ps, metrics) => new { ps.Profile, ps.Supplier, Metrics = metrics })
            .SelectMany(x => x.Metrics.DefaultIfEmpty(),
                        (x, m) => new { x.Profile, x.Supplier, Metrics = m })
            .ToListAsync(ct);

        return rows.Select(r => (r.Profile, r.Supplier, r.Metrics)).ToList();
    }

    // ── Authenticated writes (RLS enforced by interceptor) ───────────────────

    public Task<bool> ReviewExistsAsync(
        Guid supplierId, Guid tenantId, CancellationToken ct = default) =>
        _db.SupplierReviews.AnyAsync(
            r => r.SupplierId == supplierId && r.TenantId == tenantId, ct);

    public async Task AddReviewAsync(SupplierReview review, CancellationToken ct = default) =>
        await _db.SupplierReviews.AddAsync(review, ct);

    // ── Supplier self-management ──────────────────────────────────────────────

    public async Task<(SupplierProfile? Profile, Supplier? Supplier)?> GetOwnProfileAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        // Standard tenant RLS applies here — no provider bypass needed
        var row = await _db.SupplierProfiles
            .Include(p => p.Supplier)
            .FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

        if (row is null) return null;
        return (row, row.Supplier);
    }

    public Task<SupplierProfile?> GetProfileBySupplierId(
        Guid supplierId, CancellationToken ct = default) =>
        _db.SupplierProfiles.FirstOrDefaultAsync(p => p.SupplierId == supplierId, ct);

    public void UpdateProfile(SupplierProfile profile) =>
        _db.SupplierProfiles.Update(profile);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets app.role = 'provider' on the current connection so that the
    /// provider_bypass RLS policy activates and cross-tenant reads are allowed.
    /// </summary>
    private async Task SetProviderRoleAsync(CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SET app.role = 'provider';";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private IQueryable<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)>
        BuildPublicQuery(string? region, string? category, string? plan)
    {
        var profileQuery = _db.SupplierProfiles
            .AsNoTracking()
            .Where(p => p.IsPublic);

        if (!string.IsNullOrWhiteSpace(region))
            profileQuery = profileQuery.Where(p => p.Region != null &&
                EF.Functions.ILike(p.Region, $"%{region}%"));

        if (!string.IsNullOrWhiteSpace(plan))
            profileQuery = profileQuery.Where(p => p.Plan == plan);

        // category filter: JSONB contains the category string
        if (!string.IsNullOrWhiteSpace(category))
            profileQuery = profileQuery.Where(p =>
                p.Categories != null && EF.Functions.JsonContains(p.Categories, $"\"{category}\""));

        return profileQuery
            .Join(_db.Suppliers, p => p.SupplierId, s => s.Id,
                  (p, s) => new { Profile = p, Supplier = s })
            .GroupJoin(_db.SupplierMetrics, ps => ps.Profile.SupplierId, m => m.SupplierId,
                       (ps, metrics) => new { ps.Profile, ps.Supplier, Metrics = metrics })
            .SelectMany(x => x.Metrics.DefaultIfEmpty(),
                        (x, m) => new { x.Profile, x.Supplier, Metrics = m })
            .OrderBy(x => x.Supplier.Name)
            .Select(x => new ValueTuple<SupplierProfile, Supplier, SupplierMetrics?>(
                x.Profile, x.Supplier, x.Metrics));
    }
}
