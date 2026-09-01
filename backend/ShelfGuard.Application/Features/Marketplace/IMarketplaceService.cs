using ShelfGuard.Application.Features.Marketplace.Dtos;

namespace ShelfGuard.Application.Features.Marketplace;

public interface IMarketplaceService
{
    // ── Public listing ────────────────────────────────────────────────────────

    Task<PagedResult<SupplierListItemDto>> GetPublicSuppliersAsync(
        string? regionCode, string? category, string? plan,
        int page, int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a supplier profile.
    /// Premium fields (website, deliveryRegions, workingHours, paymentTerms) are hidden
    /// if the supplier's plan is "free" and <paramref name="callerIsAuthenticated"/> is false.
    /// </summary>
    Task<SupplierProfileDto?> GetSupplierProfileAsync(
        Guid supplierId,
        bool callerIsAuthenticated,
        CancellationToken ct = default);

    Task<IReadOnlyList<SupplierItemDto>?> GetSupplierItemsAsync(
        Guid supplierId, CancellationToken ct = default);

    Task<IReadOnlyList<SupplierListItemDto>> SearchSuppliersAsync(
        SupplierSearchDto request, CancellationToken ct = default);

    /// <summary>
    /// TASK-651: resolves a supplier's declared delivery coverage against ONE buyer's region for
    /// <c>GET /api/marketplace/suppliers/{id}/coverage</c>. The buyer's region is
    /// <paramref name="buyerRegionCodeOverride"/> when it is a known code, otherwise the caller
    /// tenant's primary location (first active <see cref="ShelfGuard.Domain.Entities.Location"/> by
    /// <c>CreatedAt</c> that has a <c>RegionCode</c>), otherwise unresolved
    /// (<c>BuyerRegionStatus = "unknown"</c>, <c>BuyerRegionCode = null</c>).
    /// Returns <c>null</c> when the supplier does not exist or is not published (404-equivalent).
    /// Coverage is never premium-gated.
    /// </summary>
    Task<SupplierCoverageForBuyerDto?> GetSupplierCoverageForBuyerAsync(
        Guid supplierId, string? buyerRegionCodeOverride, Guid callerTenantId,
        CancellationToken ct = default);

    /// <summary>
    /// TASK-671: buyer-facing time series of a supplier's nightly aggregate-metric snapshots for
    /// <c>GET /api/marketplace/suppliers/{id}/metrics-history</c>, oldest first (chart-ready).
    /// <paramref name="days"/> is clamped to <c>[7, 365]</c>. Returns <c>null</c> when the supplier
    /// does not exist or is not published (404-equivalent), an empty list when it exists but has no
    /// snapshots in the window.
    /// </summary>
    Task<IReadOnlyList<SupplierMetricsHistoryPointDto>?> GetSupplierMetricsHistoryAsync(
        Guid supplierId, int days, CancellationToken ct = default);

    // ── Authenticated ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns (review, null) on success.
    /// Returns (null, error) on validation or duplicate.
    /// v4.1 guards (ADR-016): self-review (reviewer tenant owns the supplier) and
    /// supplier-tenant reviewers are rejected. On success SupplierMetrics.Rating is
    /// synchronously recalculated as AVG of all reviews.
    /// </summary>
    Task<(SupplierReviewDto? Review, string? Error, bool IsDuplicate)> CreateReviewAsync(
        Guid supplierId, Guid tenantId,
        SupplierReviewCreateDto request,
        CancellationToken ct = default);

    /// <summary>Public paginated reviews of a supplier with denormalized reviewer display names.</summary>
    Task<PagedResult<PublicSupplierReviewDto>?> GetSupplierReviewsAsync(
        Guid supplierId, int page, int pageSize, CancellationToken ct = default);

    // ── Supplier self-management ──────────────────────────────────────────────

    Task<SupplierProfileDto?> GetOwnProfileAsync(Guid tenantId, CancellationToken ct = default);

    Task<(SupplierProfileDto? Profile, string? Error)> UpdateOwnProfileAsync(
        Guid tenantId, SupplierProfileUpdateDto request, CancellationToken ct = default);

    // ── Platform admin (ProviderOnly) ─────────────────────────────────────────

    Task<(SupplierItemDto? Item, string? Error)> AdminAddSupplierItemAsync(
        Guid supplierId, AdminAddSupplierItemDto request, CancellationToken ct = default);

    /// <summary>Patch-updates a supplier item. Only non-null request fields are applied.</summary>
    Task<(SupplierItemDto? Item, string? Error)> AdminUpdateSupplierItemAsync(
        Guid supplierId, Guid itemId, AdminUpdateSupplierItemDto request, CancellationToken ct = default);

    /// <summary>Returns null on success, error string on failure.</summary>
    Task<string?> AdminDeleteSupplierItemAsync(
        Guid supplierId, Guid itemId, CancellationToken ct = default);

    // ── Item category registry (TASK-294) ─────────────────────────────────────

    /// <summary>Fixed category/field registry (ADR-017 §4), backend source of truth for the item form.</summary>
    IReadOnlyList<SupplierItemCategoryDto> GetItemCategories();
}
