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
    /// Images of the given supplier items, grouped by SupplierItemId (via provider context —
    /// same cross-tenant-read need as <see cref="GetSupplierItemsAsync"/>: the caller is a client
    /// tenant reading a supplier's catalog data). Used by MarketplaceOrderReceiptService's
    /// reference-photo fallback for not-yet-scanned receipt lines (TASK-599). Suppliers with no
    /// images, or ids not found, are simply absent from the result — never throws.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<SupplierItemImage>>> GetSupplierItemImagesByIdsAsync(
        IReadOnlyList<Guid> supplierItemIds, CancellationToken ct = default);

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

    /// <summary>
    /// Returns the id of the system "Platform Marketplace" tenant, creating it on
    /// first use (BUG-012). Provider-created suppliers reference this tenant so the
    /// suppliers→tenants FK holds. The tenant is inactive, has no users, and its
    /// profiles keep IsOwnerManaged = false — the supplier cabinet never resolves it.
    /// </summary>
    Task<Guid> GetOrCreatePlatformTenantIdAsync(CancellationToken ct = default);

    Task AddSupplierAsync(Supplier supplier, CancellationToken ct = default);

    Task AddSupplierProfileAsync(SupplierProfile profile, CancellationToken ct = default);

    Task AddSupplierItemAsync(SupplierItem item, CancellationToken ct = default);

    /// <summary>Returns a supplier item only if it belongs to the given supplier.</summary>
    Task<SupplierItem?> GetSupplierItemByIdAsync(
        Guid supplierId, Guid itemId, CancellationToken ct = default);

    void RemoveSupplierItem(SupplierItem item);

    /// <summary>Returns a supplier by its Id (no tenant filter; provider-bypass read).</summary>
    Task<Supplier?> GetSupplierByRawIdAsync(Guid supplierId, CancellationToken ct = default);

    /// <summary>
    /// Resolves a public supplierId to its owning tenant id (TASK-313, supplier↔client
    /// chat) — the client side of the chat only knows the supplierId from the
    /// marketplace listing/detail page, not the supplier's tenant id.
    /// Provider-bypass read (no tenant filter).
    /// </summary>
    Task<Guid?> GetSupplierTenantIdAsync(Guid supplierId, CancellationToken ct = default);

    /// <summary>
    /// Replaces all barcodes of <paramref name="item"/> with <paramref name="newBarcodes"/> via
    /// explicit RemoveRange/AddRange against the DbContext (not navigation-collection mutation).
    /// Avoids the DbUpdateConcurrencyException that occurs when EF's change tracker treats
    /// newly-added children (client-generated Guid keys) as pre-existing rows to UPDATE — see
    /// BUG-018. Existing rows for the item are always removed first, even when
    /// <paramref name="newBarcodes"/> is empty.
    /// </summary>
    void ReplaceItemBarcodes(SupplierItem item, IReadOnlyList<SupplierItemBarcode> newBarcodes);

    /// <summary>
    /// Replaces all images of <paramref name="item"/> with <paramref name="newImages"/> via
    /// explicit RemoveRange/AddRange against the DbContext. See <see cref="ReplaceItemBarcodes"/>
    /// for the rationale (BUG-018).
    /// </summary>
    void ReplaceItemImages(SupplierItem item, IReadOnlyList<SupplierItemImage> newImages);

    // ── Supplier cabinet (v4.1, ADR-016) ─────────────────────────────────────

    /// <summary>
    /// Deterministic "my supplier" lookup for self-service supplier tenants:
    /// the single owner-managed profile of the given tenant (partial unique index).
    /// Provider-created suppliers (platform tenant, IsOwnerManaged = false) are never returned.
    /// Tenant RLS applies.
    /// </summary>
    Task<(SupplierProfile Profile, Supplier Supplier)?> GetOwnerManagedProfileAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Lazy backfill (TASK-289): persists the given not-yet-saved Supplier + owner-managed
    /// SupplierProfile pair and returns it. Race-safe — if a concurrent request already
    /// created the tenant's owner-managed profile (partial unique index on
    /// (TenantId, IsOwnerManaged)), the insert is abandoned and the winner's row is
    /// returned instead (same pattern as GetOrCreatePlatformTenantIdAsync, BUG-012).
    /// </summary>
    Task<(SupplierProfile Profile, Supplier Supplier)?> GetOrCreateOwnerManagedProfileAsync(
        Supplier supplier, SupplierProfile profile, CancellationToken ct = default);

    /// <summary>All items of a supplier including unavailable ones (cabinet view; tenant RLS applies).</summary>
    Task<IReadOnlyList<SupplierItem>> GetSupplierItemsForOwnerAsync(
        Guid supplierId, CancellationToken ct = default);

    // ── Reviews / metrics (v4.1, ADR-016) ────────────────────────────────────

    /// <summary>Reviewer tenant's business_type (tenants table — no RLS).</summary>
    Task<string?> GetTenantBusinessTypeAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Tenant's business_type + name (tenants table — no RLS). Used by the supplier
    /// cabinet lazy backfill (TASK-289) to decide whether/how to self-heal a missing
    /// owner-managed Supplier/Profile pair for an existing supplier tenant.
    /// </summary>
    Task<(string BusinessType, string Name)?> GetTenantOnboardingInfoAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>All review ratings of a supplier, cross-tenant (provider-bypass read).</summary>
    Task<IReadOnlyList<short>> GetReviewRatingsAsync(Guid supplierId, CancellationToken ct = default);

    /// <summary>
    /// Paged reviews of a supplier joined with the reviewer tenant display name
    /// (provider-bypass read), newest first.
    /// </summary>
    Task<IReadOnlyList<(SupplierReview Review, string ReviewerName)>> GetReviewsBySupplierAsync(
        Guid supplierId, int page, int pageSize, CancellationToken ct = default);

    Task<int> CountReviewsBySupplierAsync(Guid supplierId, CancellationToken ct = default);

    /// <summary>
    /// Untracked metrics row for a supplier (provider-bypass read), or null when absent.
    /// Read-only: mutating the rating goes through <see cref="UpsertMetricsRatingAsync"/>.
    /// </summary>
    Task<SupplierMetrics?> GetMetricsBySupplierIdAsync(Guid supplierId, CancellationToken ct = default);

    // TASK-645: AddMetricsAsync deleted — UpsertMetricsRatingAsync below owns the INSERT branch
    // now, and leaving unused public surface on the interface this task exists to harden is the
    // wrong default even when the method itself is harmless (staging-only, no bypass).

    // ── Composite cross-tenant read+write (TASK-643/KI-036, ADR-035) ─────────

    /// <summary>
    /// Sets <c>SupplierMetrics.Rating</c> for <paramref name="supplierId"/>, inserting the row
    /// (owned by <paramref name="supplierTenantId"/>) when it does not exist yet. Both branches
    /// are cross-tenant: the caller is the REVIEWER tenant, the row belongs to the SUPPLIER
    /// tenant, and supplier_metrics has a plain single-tenant RLS policy — so the read, the
    /// UPDATE/INSERT and the flush must all happen inside one provider-role transaction. That is
    /// why this is a composite repository method and not a read + a caller-side SaveChangesAsync.
    ///
    /// CALLER CONTRACT (not enforced by the type system — TASK-641 F10, a review criterion):
    /// this calls SaveChangesAsync on the shared request-scoped AppDbContext under the provider
    /// role, so it flushes ANY pending tracked changes, not just its own. Every caller must have
    /// flushed its own writes BEFORE calling in. True today: MarketplaceService.CreateReviewAsync
    /// saves the review itself before recalculating the rating.
    /// </summary>
    Task UpsertMetricsRatingAsync(
        Guid supplierId, Guid supplierTenantId, decimal rating, CancellationToken ct = default);

    /// <summary>
    /// Records a supplier's reply on one of its own reviews and returns the updated (tracked)
    /// entity with <c>Tenant</c> included, or <c>null</c> when the review does not exist or
    /// belongs to a different supplier — the caller must map both cases to the same "not found"
    /// error and never reveal which. Cross-tenant: the review row belongs to the REVIEWER tenant
    /// while the caller is the supplier tenant, so read + UPDATE + flush must share one
    /// provider-role transaction.
    ///
    /// CALLER CONTRACT: identical to <see cref="UpsertMetricsRatingAsync"/> above — the
    /// SaveChangesAsync inside runs under the provider role and flushes any pending tracked
    /// change. True today: SupplierCabinetService.ReplyToReviewAsync stages nothing else.
    /// </summary>
    Task<SupplierReview?> SetReviewReplyAsync(
        Guid supplierId, Guid reviewId, string replyText, DateTimeOffset repliedAt,
        CancellationToken ct = default);
}
