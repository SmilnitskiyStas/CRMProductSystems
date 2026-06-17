using System.Text.Json;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Marketplace;

public sealed class MarketplaceService : IMarketplaceService
{
    private readonly IMarketplaceRepository _repo;

    public MarketplaceService(IMarketplaceRepository repo) => _repo = repo;

    // ── Public listing ────────────────────────────────────────────────────────

    public async Task<PagedResult<SupplierListItemDto>> GetPublicSuppliersAsync(
        string? region, string? category, string? plan,
        int page, int pageSize,
        CancellationToken ct = default)
    {
        var rows = await _repo.GetPublicSuppliersAsync(region, category, plan, page, pageSize, ct);
        var total = await _repo.CountPublicSuppliersAsync(region, category, plan, ct);

        var items = rows.Select(r => ToListItemDto(r.Profile, r.Supplier, r.Metrics)).ToList();
        return new PagedResult<SupplierListItemDto>(items, total, page, pageSize);
    }

    public async Task<SupplierProfileDto?> GetSupplierProfileAsync(
        Guid supplierId, bool callerIsAuthenticated, CancellationToken ct = default)
    {
        var result = await _repo.GetSupplierByIdAsync(supplierId, ct);
        if (result is null) return null;

        var (profile, supplier, metrics) = result.Value;

        // Premium fields visible if plan=premium OR caller is authenticated
        bool showPremium = callerIsAuthenticated || profile.Plan == "premium";

        return ToFullProfileDto(profile, supplier, metrics, showPremium);
    }

    public async Task<IReadOnlyList<SupplierItemDto>> GetSupplierItemsAsync(
        Guid supplierId, CancellationToken ct = default)
    {
        var items = await _repo.GetSupplierItemsAsync(supplierId, ct);
        return items.Select(ToItemDto).ToList();
    }

    public async Task<IReadOnlyList<SupplierListItemDto>> SearchSuppliersAsync(
        SupplierSearchDto request, CancellationToken ct = default)
    {
        var rows = await _repo.SearchSuppliersAsync(request.ItemName, request.Region, ct);
        return rows.Select(r => ToListItemDto(r.Profile, r.Supplier, r.Metrics)).ToList();
    }

    // ── Authenticated ─────────────────────────────────────────────────────────

    public async Task<(SupplierReviewDto? Review, string? Error, bool IsDuplicate)> CreateReviewAsync(
        Guid supplierId, Guid tenantId,
        SupplierReviewCreateDto request,
        CancellationToken ct = default)
    {
        if (request.Rating < 1 || request.Rating > 5)
            return (null, "Rating must be between 1 and 5.", false);

        var duplicate = await _repo.ReviewExistsAsync(supplierId, tenantId, ct);
        if (duplicate)
            return (null, "You have already reviewed this supplier.", true);

        var review = new SupplierReview
        {
            SupplierId = supplierId,
            TenantId   = tenantId,
            Rating     = (short)request.Rating,
            Comment    = request.Comment?.Trim(),
        };

        await _repo.AddReviewAsync(review, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToReviewDto(review), null, false);
    }

    // ── Supplier self-management ──────────────────────────────────────────────

    public async Task<SupplierProfileDto?> GetOwnProfileAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var result = await _repo.GetOwnProfileAsync(tenantId, ct);
        if (result is null) return null;

        var (profile, supplier) = result.Value;
        if (profile is null || supplier is null) return null;

        return ToFullProfileDto(profile, supplier, null, showPremium: true);
    }

    public async Task<(SupplierProfileDto? Profile, string? Error)> UpdateOwnProfileAsync(
        Guid tenantId, SupplierProfileUpdateDto request, CancellationToken ct = default)
    {
        if (request.Plan is not null &&
            request.Plan != "free" && request.Plan != "premium")
            return (null, "Plan must be 'free' or 'premium'.");

        var result = await _repo.GetOwnProfileAsync(tenantId, ct);
        if (result is null)
            return (null, "Supplier profile not found for this tenant.");

        var (profile, supplier) = result.Value;
        if (profile is null || supplier is null)
            return (null, "Supplier profile not found for this tenant.");

        // Patch semantics — only update provided fields
        if (request.Region is not null)
            profile.Region = request.Region;
        if (request.Categories is not null)
            profile.Categories = JsonSerializer.Serialize(request.Categories);
        if (request.Website is not null)
            profile.Website = request.Website;
        if (request.DeliveryRegions is not null)
            profile.DeliveryRegions = JsonSerializer.Serialize(request.DeliveryRegions);
        if (request.WorkingHours is not null)
            profile.WorkingHours = request.WorkingHours;
        if (request.PaymentTerms is not null)
            profile.PaymentTerms = request.PaymentTerms;
        if (request.IsPublic.HasValue)
            profile.IsPublic = request.IsPublic.Value;
        if (request.Plan is not null)
            profile.Plan = request.Plan;

        profile.UpdatedAt = DateTimeOffset.UtcNow;

        _repo.UpdateProfile(profile);
        await _repo.SaveChangesAsync(ct);

        return (ToFullProfileDto(profile, supplier, null, showPremium: true), null);
    }

    // ── Private mapping ───────────────────────────────────────────────────────

    private static SupplierListItemDto ToListItemDto(
        SupplierProfile p, Supplier s, SupplierMetrics? m) =>
        new(
            s.Id,
            s.Name,
            p.Region,
            p.Plan,
            DeserializeStringArray(p.Categories),
            m?.Rating,
            m?.AvgDeliveryDays.HasValue == true ? (int?)Math.Round(m.AvgDeliveryDays!.Value) : null,
            p.IsPublic);

    private static SupplierProfileDto ToFullProfileDto(
        SupplierProfile p, Supplier s, SupplierMetrics? m, bool showPremium) =>
        new(
            s.Id,
            s.Name,
            p.Region,
            DeserializeStringArray(p.Categories),
            showPremium ? p.Website : null,
            showPremium ? DeserializeStringArray(p.DeliveryRegions) : null,
            showPremium ? p.WorkingHours : null,
            showPremium ? p.PaymentTerms : null,
            p.IsPublic,
            p.Plan,
            m is not null ? ToMetricsDto(m) : null);

    private static SupplierMetricsDto ToMetricsDto(SupplierMetrics m) =>
        new(m.Rating, m.AvgDeliveryDays, m.OrderAccuracy, m.QualityScore,
            m.CancellationRate, m.ResponseTimeHours, m.UpdatedAt);

    private static SupplierItemDto ToItemDto(SupplierItem i) =>
        new(i.Id, i.ItemId, i.CustomName, i.Item?.Name, i.Price, i.MinQty, i.Unit, i.IsAvailable);

    private static SupplierReviewDto ToReviewDto(SupplierReview r) =>
        new(r.Id, r.Rating, r.Comment, r.CreatedAt);

    private static string[]? DeserializeStringArray(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<string[]>(json); }
        catch { return null; }
    }
}
