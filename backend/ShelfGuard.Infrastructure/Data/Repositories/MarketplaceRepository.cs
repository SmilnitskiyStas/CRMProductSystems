using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for the Supplier Marketplace feature.
///
/// Cross-tenant marketplace reads (public listing, supplier detail/items/images, search,
/// supplier→tenant resolution, reviews and metrics) genuinely need to see rows owned by other
/// tenants: a client tenant browses a supplier tenant's catalog, a supplier tenant reads a
/// reviewer tenant's review. Each such method runs its query inside
/// <see cref="IProviderRlsOverride"/>.ExecuteAsync, which sets Postgres
/// <c>SET LOCAL app.role = 'provider'</c> for that one transaction so the <c>provider_bypass</c>
/// RLS policy applies, and reverts automatically on commit/rollback.
///
/// TASK-643/KI-036 (ADR-035): this replaces a private SetProviderRoleAsync helper that issued a
/// SESSION-level <c>SET app.role = 'provider'</c> on a manually opened DbConnection and never
/// reset it — every subsequent statement of the same HTTP request (including unrelated
/// <c>items</c> lookups in MarketplaceOrderService) then ran with a full cross-tenant read+write
/// bypass. Nothing in this file may open a raw DbConnection or issue a session-level SET again;
/// "no <c>GetDbConnection()</c> in this file" is a standing review criterion.
///
/// Two composite methods (<see cref="UpsertMetricsRatingAsync"/>,
/// <see cref="SetReviewReplyAsync"/>) exist because their write is legitimately cross-tenant and
/// must happen in the SAME override block as the read that precedes it — see their own docs.
///
/// Everything else (AddReviewAsync, GetOwnProfileAsync, the cabinet/admin item methods, ...)
/// relies on TenantConnectionInterceptor setting app.tenant_id from the JWT, letting the
/// standard tenant_isolation RLS policy enforce access automatically. Pure change-tracker
/// staging methods (Add*/Replace*/Remove*/Update*) emit no SQL of their own and are deliberately
/// NOT wrapped — their SQL is emitted by the caller's own SaveChangesAsync, under the caller's
/// own RLS context.
/// </summary>
public sealed class MarketplaceRepository : IMarketplaceRepository
{
    private readonly AppDbContext _db;
    private readonly IProviderRlsOverride _providerRlsOverride;

    public MarketplaceRepository(AppDbContext db, IProviderRlsOverride providerRlsOverride)
    {
        _db = db;
        _providerRlsOverride = providerRlsOverride;
    }

    // ── Public listing (provider-bypass RLS) ─────────────────────────────────

    public Task<IReadOnlyList<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)>>
        GetPublicSuppliersAsync(
            string? regionCode, string? category, string? plan,
            int page, int pageSize,
            CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync<IReadOnlyList<(SupplierProfile, Supplier, SupplierMetrics?)>>(
            async () => await BuildPublicQuery(regionCode, category, plan)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct),
            ct);

    public Task<int> CountPublicSuppliersAsync(
        string? regionCode, string? category, string? plan,
        CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync(
            async () => await BuildPublicQuery(regionCode, category, plan).CountAsync(ct),
            ct);

    public Task<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)?>
        GetSupplierByIdAsync(Guid supplierId, CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync<(SupplierProfile, Supplier, SupplierMetrics?)?>(async () =>
        {
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
        }, ct);

    public Task<IReadOnlyList<SupplierItem>> GetSupplierItemsAsync(
        Guid supplierId, CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync<IReadOnlyList<SupplierItem>>(
            async () => await _db.SupplierItems
                .AsNoTracking()
                .Include(i => i.Item)
                .Include(i => i.Barcodes)
                .Include(i => i.Images)
                .Include(i => i.PlatformCategory)
                .Where(i => i.SupplierId == supplierId && i.IsAvailable)
                .OrderBy(i => i.CustomName ?? (i.Item != null ? i.Item.Name : string.Empty))
                .ToListAsync(ct),
            ct);

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<SupplierItemImage>>> GetSupplierItemImagesByIdsAsync(
        IReadOnlyList<Guid> supplierItemIds, CancellationToken ct = default)
    {
        // Early return stays OUTSIDE the override block so the common empty case still costs
        // zero transactions (and zero role switches).
        if (supplierItemIds.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<SupplierItemImage>>>(
                new Dictionary<Guid, IReadOnlyList<SupplierItemImage>>());

        return _providerRlsOverride.ExecuteAsync<IReadOnlyDictionary<Guid, IReadOnlyList<SupplierItemImage>>>(
            async () =>
            {
                var rows = await _db.SupplierItemImages
                    .AsNoTracking()
                    .Where(img => supplierItemIds.Contains(img.SupplierItemId))
                    .OrderBy(img => img.SortOrder)
                    .ToListAsync(ct);

                return rows
                    .GroupBy(img => img.SupplierItemId)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<SupplierItemImage>)g.ToList());
            }, ct);
    }

    public Task<IReadOnlyList<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)>>
        SearchSuppliersAsync(string itemName, string? regionCode, CancellationToken ct = default) =>
        // Both queries are dependent (the second filters on the first's ids) and MUST share one
        // override block — splitting them would run the second under the caller's own role and
        // silently return nothing.
        _providerRlsOverride.ExecuteAsync<IReadOnlyList<(SupplierProfile, Supplier, SupplierMetrics?)>>(
            async () =>
            {
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

                if (!string.IsNullOrWhiteSpace(regionCode))
                    query = ApplyRegionCoverageFilter(query, regionCode);

                var rows = await query
                    .Join(_db.Suppliers, p => p.SupplierId, s => s.Id,
                          (p, s) => new { Profile = p, Supplier = s })
                    .GroupJoin(_db.SupplierMetrics, ps => ps.Profile.SupplierId, m => m.SupplierId,
                               (ps, metrics) => new { ps.Profile, ps.Supplier, Metrics = metrics })
                    .SelectMany(x => x.Metrics.DefaultIfEmpty(),
                                (x, m) => new { x.Profile, x.Supplier, Metrics = m })
                    .ToListAsync(ct);

                return rows.Select(r => (r.Profile, r.Supplier, r.Metrics)).ToList();
            }, ct);

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

    // ── Platform admin operations ─────────────────────────────────────────────

    /// <summary>Slug of the system tenant that owns provider-created marketplace suppliers (BUG-012).</summary>
    public const string PlatformTenantSlug = "platform-marketplace";
    public const string PlatformTenantName = "Platform Marketplace";

    public async Task<Guid> GetOrCreatePlatformTenantIdAsync(CancellationToken ct = default)
    {
        // tenants table has no tenant RLS — plain lookup by unique slug.
        var existing = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Slug == PlatformTenantSlug)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is not null) return existing.Value;

        var tenant = Tenant.Create(PlatformTenantName, PlatformTenantSlug);
        tenant.UpdateBusinessType("supplier");
        tenant.Deactivate(); // system tenant: no users, no login, cabinet unreachable

        await _db.Tenants.AddAsync(tenant, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
            return tenant.Id;
        }
        catch (DbUpdateException)
        {
            // Lost a concurrent race on the unique slug index — detach our copy
            // (so later SaveChanges of the supplier doesn't retry it) and reuse
            // the winner's row.
            _db.Entry(tenant).State = EntityState.Detached;
            return await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Slug == PlatformTenantSlug)
                .Select(t => t.Id)
                .FirstAsync(ct);
        }
    }

    public async Task AddSupplierAsync(Supplier supplier, CancellationToken ct = default) =>
        await _db.Suppliers.AddAsync(supplier, ct);

    public async Task AddSupplierProfileAsync(SupplierProfile profile, CancellationToken ct = default) =>
        await _db.SupplierProfiles.AddAsync(profile, ct);

    public async Task AddSupplierItemAsync(SupplierItem item, CancellationToken ct = default) =>
        await _db.SupplierItems.AddAsync(item, ct);

    public Task<SupplierItem?> GetSupplierItemByIdAsync(
        Guid supplierId, Guid itemId, CancellationToken ct = default) =>
        _db.SupplierItems
            .Include(i => i.Barcodes)
            .Include(i => i.Images)
            .Include(i => i.PlatformCategory)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.SupplierId == supplierId, ct);

    public void RemoveSupplierItem(SupplierItem item) =>
        _db.SupplierItems.Remove(item);

    // BUG-018: Barcodes/Images have client-generated Guid keys with HasDefaultValueSql
    // ("gen_random_uuid()"). When new children are attached via navigation-collection
    // mutation (item.Barcodes.Add(...)) on an already-tracked/Unchanged parent, EF's
    // change tracker does not reliably infer EntityState.Added for them — it can treat
    // them as pre-existing rows to UPDATE, producing a DbUpdateConcurrencyException
    // ("expected to affect 1 row(s), but actually affected 0") when the item previously
    // had zero rows. Explicit RemoveRange/AddRange against the DbSet sidesteps the
    // ambiguity entirely: removed rows are always marked Deleted, added rows are always
    // marked Added, regardless of the parent's tracking state.
    public void ReplaceItemBarcodes(SupplierItem item, IReadOnlyList<SupplierItemBarcode> newBarcodes)
    {
        if (item.Barcodes.Count > 0)
            _db.SupplierItemBarcodes.RemoveRange(item.Barcodes);

        if (newBarcodes.Count > 0)
            _db.SupplierItemBarcodes.AddRange(newBarcodes);
    }

    public void ReplaceItemImages(SupplierItem item, IReadOnlyList<SupplierItemImage> newImages)
    {
        if (item.Images.Count > 0)
            _db.SupplierItemImages.RemoveRange(item.Images);

        if (newImages.Count > 0)
            _db.SupplierItemImages.AddRange(newImages);
    }

    /// <summary>
    /// Provider bypass: a reviewing tenant must be able to resolve the supplier's TenantId
    /// (self-review guard) even though the row belongs to another tenant. AsNoTracking is
    /// load-bearing — this returns a FOREIGN-tenant Supplier, and leaving it in the shared
    /// change tracker would let an unrelated later SaveChangesAsync flush it under whatever RLS
    /// context happens to be active then. Callers read Id/TenantId only.
    /// </summary>
    public Task<Supplier?> GetSupplierByRawIdAsync(Guid supplierId, CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync(
            async () => await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct),
            ct);

    public Task<Guid?> GetSupplierTenantIdAsync(Guid supplierId, CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync(async () =>
        {
            var supplier = await _db.Suppliers
                .AsNoTracking()
                .Where(s => s.Id == supplierId)
                .Select(s => new { s.TenantId })
                .FirstOrDefaultAsync(ct);
            return supplier?.TenantId;
        }, ct);

    // ── Supplier cabinet (v4.1, ADR-016) ─────────────────────────────────────

    public async Task<(SupplierProfile Profile, Supplier Supplier)?> GetOwnerManagedProfileAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        // Tenant RLS applies — a supplier tenant only ever sees its own rows.
        var row = await _db.SupplierProfiles
            .Include(p => p.Supplier)
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.IsOwnerManaged, ct);

        if (row?.Supplier is null) return null;
        return (row, row.Supplier);
    }

    public async Task<(SupplierProfile Profile, Supplier Supplier)?> GetOrCreateOwnerManagedProfileAsync(
        Supplier supplier, SupplierProfile profile, CancellationToken ct = default)
    {
        await _db.Suppliers.AddAsync(supplier, ct);
        await _db.SupplierProfiles.AddAsync(profile, ct);

        try
        {
            await _db.SaveChangesAsync(ct);
            return (profile, supplier);
        }
        catch (DbUpdateException)
        {
            // Lost a concurrent race on the partial unique index (TenantId, IsOwnerManaged) —
            // another request already created the pair. Detach our copies and re-fetch the winner.
            _db.Entry(supplier).State = EntityState.Detached;
            _db.Entry(profile).State = EntityState.Detached;
            return await GetOwnerManagedProfileAsync(supplier.TenantId, ct);
        }
    }

    public async Task<IReadOnlyList<SupplierItem>> GetSupplierItemsForOwnerAsync(
        Guid supplierId, CancellationToken ct = default) =>
        await _db.SupplierItems
            .AsNoTracking()
            .Include(i => i.Item)
            .Include(i => i.Barcodes)
            .Include(i => i.Images)
            .Include(i => i.PlatformCategory)
            .Where(i => i.SupplierId == supplierId)
            .OrderBy(i => i.CustomName ?? (i.Item != null ? i.Item.Name : string.Empty))
            .ToListAsync(ct);

    // ── Reviews / metrics (v4.1, ADR-016) ────────────────────────────────────

    public Task<string?> GetTenantBusinessTypeAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (string?)t.BusinessType)
            .FirstOrDefaultAsync(ct);

    public async Task<(string BusinessType, string Name)?> GetTenantOnboardingInfoAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var row = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.BusinessType, t.Name })
            .FirstOrDefaultAsync(ct);

        return row is null ? null : (row.BusinessType, row.Name);
    }

    public Task<IReadOnlyList<short>> GetReviewRatingsAsync(
        Guid supplierId, CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync<IReadOnlyList<short>>(
            async () => await _db.SupplierReviews
                .AsNoTracking()
                .Where(r => r.SupplierId == supplierId)
                .Select(r => r.Rating)
                .ToListAsync(ct),
            ct);

    public Task<IReadOnlyList<(SupplierReview Review, string ReviewerName)>> GetReviewsBySupplierAsync(
        Guid supplierId, int page, int pageSize, CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync<IReadOnlyList<(SupplierReview, string)>>(async () =>
        {
            var rows = await _db.SupplierReviews
                .AsNoTracking()
                .Where(r => r.SupplierId == supplierId)
                .Join(_db.Tenants, r => r.TenantId, t => t.Id,
                      (r, t) => new { Review = r, ReviewerName = t.Name })
                .OrderByDescending(x => x.Review.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return rows.Select(x => (x.Review, x.ReviewerName)).ToList();
        }, ct);

    public Task<int> CountReviewsBySupplierAsync(Guid supplierId, CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync(
            async () => await _db.SupplierReviews.CountAsync(r => r.SupplierId == supplierId, ct),
            ct);

    /// <summary>
    /// AsNoTracking (TASK-643): the metrics row belongs to the SUPPLIER tenant, not necessarily
    /// the caller's, so it must never sit tracked in the shared change tracker waiting for an
    /// unrelated SaveChangesAsync. The one code path that mutates it goes through
    /// <see cref="UpsertMetricsRatingAsync"/> instead; the remaining callers read scalars only.
    /// </summary>
    public Task<SupplierMetrics?> GetMetricsBySupplierIdAsync(
        Guid supplierId, CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync(
            async () => await _db.SupplierMetrics.AsNoTracking()
                .FirstOrDefaultAsync(m => m.SupplierId == supplierId, ct),
            ct);

    /// <summary>
    /// TASK-671: see <see cref="IMarketplaceRepository.GetMetricsHistoryAsync"/>. Cross-tenant read
    /// (buyer tenant → supplier tenant's rows), so it runs inside one provider-override block,
    /// <c>AsNoTracking</c> — same pattern as <see cref="GetSupplierByIdAsync"/>. Pure LINQ /
    /// EF translation, no <c>GetDbConnection()</c> / raw SQL / session-level SET — KI-036 (ADR-035)
    /// standing rule at the top of this file still holds. The <c>SnapshotDate &gt;= cutoff</c>
    /// window uses <c>idx_supplier_metrics_snapshots_supplier_date</c>; ascending order is a
    /// forward scan of that same index.
    /// </summary>
    public Task<IReadOnlyList<SupplierMetricsSnapshot>> GetMetricsHistoryAsync(
        Guid supplierId, int days, CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync<IReadOnlyList<SupplierMetricsSnapshot>>(async () =>
        {
            var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-days);
            return await _db.SupplierMetricsSnapshots
                .AsNoTracking()
                .Where(s => s.SupplierId == supplierId && s.SnapshotDate >= cutoff)
                .OrderBy(s => s.SnapshotDate)
                .ToListAsync(ct);
        }, ct);

    // ── Composite cross-tenant read+write (TASK-643, ADR-035) ────────────────

    /// <summary>
    /// See <see cref="IMarketplaceRepository.UpsertMetricsRatingAsync"/> for the contract.
    /// Read and write share ONE provider-override block because the target row's TenantId is the
    /// SUPPLIER tenant while the ambient session is the reviewer tenant — supplier_metrics has a
    /// plain single-tenant tenant_isolation policy, so an ambient UPDATE would affect 0 rows and
    /// an ambient INSERT would raise 42501.
    /// </summary>
    public Task UpsertMetricsRatingAsync(
        Guid supplierId, Guid supplierTenantId, decimal rating, CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync(async () =>
        {
            var metrics = await _db.SupplierMetrics
                .FirstOrDefaultAsync(m => m.SupplierId == supplierId, ct);

            if (metrics is null)
            {
                metrics = new SupplierMetrics
                {
                    SupplierId = supplierId,
                    TenantId   = supplierTenantId,
                    Rating     = rating,
                };
                await _db.SupplierMetrics.AddAsync(metrics, ct);
            }
            else
            {
                metrics.Rating    = rating;
                metrics.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await _db.SaveChangesAsync(ct);

            // TASK-645 R3: this row belongs to the SUPPLIER tenant, not the caller's. Once it is
            // saved, nothing outside this block has any business holding it — detaching removes
            // the whole "foreign entity lingering in the shared change tracker waiting for
            // somebody else's SaveChangesAsync" hazard class rather than relying on it staying
            // Unchanged. Same reasoning as the AsNoTracking() on the reads above.
            _db.Entry(metrics).State = EntityState.Detached;
            return true;
        }, ct);

    /// <summary>
    /// See <see cref="IMarketplaceRepository.SetReviewReplyAsync"/> for the contract.
    /// Read and write share ONE provider-override block because a review row's TenantId is the
    /// REVIEWER tenant while the ambient session is the supplier tenant (whose role is not even
    /// in TenantConnectionInterceptor.ValidRoles), so an ambient UPDATE would affect 0 rows and
    /// throw DbUpdateConcurrencyException.
    /// </summary>
    public Task<SupplierReview?> SetReviewReplyAsync(
        Guid supplierId, Guid reviewId, string replyText, DateTimeOffset repliedAt,
        CancellationToken ct = default) =>
        _providerRlsOverride.ExecuteAsync<SupplierReview?>(async () =>
        {
            var review = await _db.SupplierReviews
                .Include(r => r.Tenant)
                .FirstOrDefaultAsync(r => r.Id == reviewId && r.SupplierId == supplierId, ct);

            // Not this supplier's review (or no such review) — never reveal which.
            if (review is null) return null;

            review.ReplyText = replyText;
            review.RepliedAt = repliedAt;

            await _db.SaveChangesAsync(ct);

            // TASK-645 R3: the review row belongs to the REVIEWER tenant and its .Include'd Tenant
            // is that tenant's own row — neither may outlive this block in the shared change
            // tracker. Detaching does not clear the loaded values, so the caller can still read
            // ReplyText/RepliedAt/Tenant.Name off the returned instance.
            if (review.Tenant is not null)
                _db.Entry(review.Tenant).State = EntityState.Detached;
            _db.Entry(review).State = EntityState.Detached;

            return review;
        }, ct);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// TASK-651: region filter over a supplier's structured delivery coverage. A profile matches
    /// when <c>DeliveryCoverage.served</c> contains an entry for <paramref name="regionCode"/> AND
    /// the code is not present in <c>DeliveryCoverage.notServed</c>. Legacy profiles whose
    /// <c>DeliveryCoverage</c> is still NULL (not yet backfilled — T14) fall back to a free-text
    /// ILIKE against the deprecated <c>Region</c> column, matched against either the raw code or
    /// the region's Ukrainian name, so they don't disappear from search during the transition.
    ///
    /// <para>
    /// Both jsonb predicates are <see cref="EF.Functions"/> translations to Postgres' server-side
    /// <c>@&gt;</c> containment operator (identical mechanism to the <c>Categories</c> filter in
    /// <see cref="BuildPublicQuery"/>) — nothing here is client-evaluated, and no raw SQL /
    /// <c>GetDbConnection()</c> is involved, so the KI-036 (ADR-035) standing rule at the top of
    /// this file still holds. <paramref name="regionCode"/> is a registry-validated code by the
    /// time it reaches here (MarketplaceService normalizes via <c>UkraineRegions.TryMatchFreeText</c>),
    /// so the interpolated JSON fragments cannot be malformed.
    /// </para>
    /// </summary>
    private static IQueryable<SupplierProfile> ApplyRegionCoverageFilter(
        IQueryable<SupplierProfile> query, string regionCode)
    {
        var servedJson    = $"{{\"served\":[{{\"regionCode\":\"{regionCode}\"}}]}}";
        var notServedJson = $"{{\"notServed\":[\"{regionCode}\"]}}";
        var regionName    = UkraineRegions.Find(regionCode)?.NameUa;

        return query.Where(p =>
            (p.DeliveryCoverage != null
                && EF.Functions.JsonContains(p.DeliveryCoverage, servedJson)
                && !EF.Functions.JsonContains(p.DeliveryCoverage, notServedJson))
            || (p.DeliveryCoverage == null
                && p.Region != null
                && (EF.Functions.ILike(p.Region, $"%{regionCode}%")
                    || (regionName != null && EF.Functions.ILike(p.Region, $"%{regionName}%")))));
    }

    private IQueryable<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)>
        BuildPublicQuery(string? regionCode, string? category, string? plan)
    {
        var profileQuery = _db.SupplierProfiles
            .AsNoTracking()
            .Where(p => p.IsPublic);

        if (!string.IsNullOrWhiteSpace(regionCode))
            profileQuery = ApplyRegionCoverageFilter(profileQuery, regionCode);

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
