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
}
