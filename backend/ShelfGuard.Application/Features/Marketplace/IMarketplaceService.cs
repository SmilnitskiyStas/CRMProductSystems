using ShelfGuard.Application.Features.Marketplace.Dtos;

namespace ShelfGuard.Application.Features.Marketplace;

public interface IMarketplaceService
{
    // ── Public listing ────────────────────────────────────────────────────────

    Task<PagedResult<SupplierListItemDto>> GetPublicSuppliersAsync(
        string? region, string? category, string? plan,
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

    Task<IReadOnlyList<SupplierItemDto>> GetSupplierItemsAsync(
        Guid supplierId, CancellationToken ct = default);

    Task<IReadOnlyList<SupplierListItemDto>> SearchSuppliersAsync(
        SupplierSearchDto request, CancellationToken ct = default);

    // ── Authenticated ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns (review, null) on success.
    /// Returns (null, error) on validation or duplicate.
    /// </summary>
    Task<(SupplierReviewDto? Review, string? Error, bool IsDuplicate)> CreateReviewAsync(
        Guid supplierId, Guid tenantId,
        SupplierReviewCreateDto request,
        CancellationToken ct = default);

    // ── Supplier self-management ──────────────────────────────────────────────

    Task<SupplierProfileDto?> GetOwnProfileAsync(Guid tenantId, CancellationToken ct = default);

    Task<(SupplierProfileDto? Profile, string? Error)> UpdateOwnProfileAsync(
        Guid tenantId, SupplierProfileUpdateDto request, CancellationToken ct = default);

    // ── Platform admin (ProviderOnly) ─────────────────────────────────────────

    Task<(SupplierProfileDto Profile, string? Error)> AdminCreateSupplierAsync(
        AdminCreateSupplierDto request, CancellationToken ct = default);

    Task<(SupplierItemDto? Item, string? Error)> AdminAddSupplierItemAsync(
        Guid supplierId, AdminAddSupplierItemDto request, CancellationToken ct = default);

    /// <summary>Returns null on success, error string on failure.</summary>
    Task<string?> AdminDeleteSupplierItemAsync(
        Guid supplierId, Guid itemId, CancellationToken ct = default);
}
