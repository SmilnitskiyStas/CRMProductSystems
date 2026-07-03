using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IMarketplaceRepository
{
    // ── Public listing (RLS bypass via provider context) ────────────────────

    /// <summary>
    /// Returns all supplier profiles where IsPublic=true.
    /// Executes with provider-level DB context (bypasses tenant RLS) so that
    /// unauthenticated marketplace listing can cross tenant boundaries.
    /// </summary>
    Task<IReadOnlyList<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)>>
        GetPublicSuppliersAsync(string? region, string? category, string? plan,
            int page, int pageSize, CancellationToken ct = default);

    Task<int> CountPublicSuppliersAsync(string? region, string? category, string? plan,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a single supplier profile with joined metrics.
    /// Uses provider context — caller is responsible for filtering premium fields.
    /// </summary>
    Task<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)?> GetSupplierByIdAsync(
        Guid supplierId, CancellationToken ct = default);

    /// <summary>Returns all supplier items for a given supplier (via provider context).</summary>
    Task<IReadOnlyList<SupplierItem>> GetSupplierItemsAsync(
        Guid supplierId, CancellationToken ct = default);

    /// <summary>
    /// Searches public suppliers whose item catalog contains items matching
    /// <paramref name="itemName"/> and optionally filtered by <paramref name="region"/>.
    /// Uses provider context.
    /// </summary>
    Task<IReadOnlyList<(SupplierProfile Profile, Supplier Supplier, SupplierMetrics? Metrics)>>
        SearchSuppliersAsync(string itemName, string? region, CancellationToken ct = default);

    // ── Authenticated operations (RLS enforced by TenantConnectionInterceptor) ──

    /// <summary>Checks whether the calling tenant already reviewed a supplier.</summary>
    Task<bool> ReviewExistsAsync(Guid supplierId, Guid tenantId, CancellationToken ct = default);

    Task AddReviewAsync(SupplierReview review, CancellationToken ct = default);

    // ── Supplier self-management (RLS enforced) ──────────────────────────────

    /// <summary>Returns own supplier profile for the calling tenant's supplier.</summary>
    Task<(SupplierProfile? Profile, Supplier? Supplier)?> GetOwnProfileAsync(
        Guid tenantId, CancellationToken ct = default);

    Task<SupplierProfile?> GetProfileBySupplierId(Guid supplierId, CancellationToken ct = default);

    void UpdateProfile(SupplierProfile profile);

    Task SaveChangesAsync(CancellationToken ct = default);

    // ── Platform admin operations (ProviderOnly) ─────────────────────────────

    Task AddSupplierAsync(Supplier supplier, CancellationToken ct = default);

    Task AddSupplierProfileAsync(SupplierProfile profile, CancellationToken ct = default);

    Task AddSupplierItemAsync(SupplierItem item, CancellationToken ct = default);

    /// <summary>Returns a supplier item only if it belongs to the given supplier.</summary>
    Task<SupplierItem?> GetSupplierItemByIdAsync(
        Guid supplierId, Guid itemId, CancellationToken ct = default);

    void RemoveSupplierItem(SupplierItem item);

    /// <summary>Returns a supplier by its Id (no tenant filter; provider-bypass read).</summary>
    Task<Supplier?> GetSupplierByRawIdAsync(Guid supplierId, CancellationToken ct = default);

    // ── Supplier cabinet (v4.1, ADR-016) ─────────────────────────────────────

    /// <summary>
    /// Deterministic "my supplier" lookup for self-service supplier tenants:
    /// the single owner-managed profile of the given tenant (partial unique index).
    /// Provider-created suppliers (TenantId = Guid.Empty) are never returned.
    /// Tenant RLS applies.
    /// </summary>
    Task<(SupplierProfile Profile, Supplier Supplier)?> GetOwnerManagedProfileAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>All items of a supplier including unavailable ones (cabinet view; tenant RLS applies).</summary>
    Task<IReadOnlyList<SupplierItem>> GetSupplierItemsForOwnerAsync(
        Guid supplierId, CancellationToken ct = default);

    // ── Reviews / metrics (v4.1, ADR-016) ────────────────────────────────────

    /// <summary>Reviewer tenant's business_type (tenants table — no RLS).</summary>
    Task<string?> GetTenantBusinessTypeAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>All review ratings of a supplier, cross-tenant (provider-bypass read).</summary>
    Task<IReadOnlyList<short>> GetReviewRatingsAsync(Guid supplierId, CancellationToken ct = default);

    /// <summary>
    /// Paged reviews of a supplier joined with the reviewer tenant display name
    /// (provider-bypass read), newest first.
    /// </summary>
    Task<IReadOnlyList<(SupplierReview Review, string ReviewerName)>> GetReviewsBySupplierAsync(
        Guid supplierId, int page, int pageSize, CancellationToken ct = default);

    Task<int> CountReviewsBySupplierAsync(Guid supplierId, CancellationToken ct = default);

    /// <summary>Tracked metrics row for a supplier (provider-bypass read), or null when absent.</summary>
    Task<SupplierMetrics?> GetMetricsBySupplierIdAsync(Guid supplierId, CancellationToken ct = default);

    Task AddMetricsAsync(SupplierMetrics metrics, CancellationToken ct = default);
}
