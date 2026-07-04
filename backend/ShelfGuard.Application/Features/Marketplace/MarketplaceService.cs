using System.Text.Json;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Constants;
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

        // BUG-010: unpublished profiles must not be exposed via the public detail
        // endpoint — behave exactly like "not found" (listing/search already filter IsPublic).
        if (!profile.IsPublic) return null;

        // Premium fields visible if plan=premium OR caller is authenticated
        bool showPremium = callerIsAuthenticated || profile.Plan == "premium";

        return ToFullProfileDto(profile, supplier, metrics, showPremium);
    }

    public async Task<IReadOnlyList<SupplierItemDto>?> GetSupplierItemsAsync(
        Guid supplierId, CancellationToken ct = default)
    {
        // BUG-010: items of an unpublished supplier must not be exposed either.
        if (!await IsPublishedAsync(supplierId, ct)) return null;

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

        // v4.1 review hardening (ADR-016, TASK-285)
        var supplier = await _repo.GetSupplierByRawIdAsync(supplierId, ct);
        if (supplier is null)
            return (null, "Supplier not found.", false);

        if (supplier.TenantId == tenantId)
            return (null, "You cannot review your own supplier.", false);

        var reviewerBusinessType = await _repo.GetTenantBusinessTypeAsync(tenantId, ct);
        if (string.Equals(reviewerBusinessType, "supplier", StringComparison.OrdinalIgnoreCase))
            return (null, "Supplier tenants cannot leave reviews.", false);

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

        // Synchronous rating recalc: SupplierMetrics.Rating = AVG(all reviews)
        await RecalculateRatingAsync(supplier, ct);

        return (ToReviewDto(review), null, false);
    }

    public async Task<PagedResult<PublicSupplierReviewDto>?> GetSupplierReviewsAsync(
        Guid supplierId, int page, int pageSize, CancellationToken ct = default)
    {
        // BUG-010: reviews of an unpublished supplier must not be exposed either.
        if (!await IsPublishedAsync(supplierId, ct)) return null;

        var rows  = await _repo.GetReviewsBySupplierAsync(supplierId, page, pageSize, ct);
        var total = await _repo.CountReviewsBySupplierAsync(supplierId, ct);

        var items = rows
            .Select(r => new PublicSupplierReviewDto(
                r.Review.Id, r.Review.Rating, r.Review.Comment, r.Review.CreatedAt, r.ReviewerName))
            .ToList();

        return new PagedResult<PublicSupplierReviewDto>(items, total, page, pageSize);
    }

    private async Task<bool> IsPublishedAsync(Guid supplierId, CancellationToken ct)
    {
        var result = await _repo.GetSupplierByIdAsync(supplierId, ct);
        return result is not null && result.Value.Profile.IsPublic;
    }

    private async Task RecalculateRatingAsync(Supplier supplier, CancellationToken ct)
    {
        var ratings = await _repo.GetReviewRatingsAsync(supplier.Id, ct);
        if (ratings.Count == 0) return;

        var avg = Math.Round((decimal)ratings.Average(r => (int)r), 2);

        var metrics = await _repo.GetMetricsBySupplierIdAsync(supplier.Id, ct);
        if (metrics is null)
        {
            metrics = new SupplierMetrics
            {
                SupplierId = supplier.Id,
                TenantId   = supplier.TenantId,
                Rating     = avg,
            };
            await _repo.AddMetricsAsync(metrics, ct);
        }
        else
        {
            metrics.Rating    = avg;
            metrics.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _repo.SaveChangesAsync(ct);
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

    // ── Platform admin (ProviderOnly) ────────────────────────────────────────

    public async Task<(SupplierProfileDto Profile, string? Error)> AdminCreateSupplierAsync(
        AdminCreateSupplierDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
            return (null!, "CompanyName is required.");

        if (request.Plan is not "free" and not "premium")
            return (null!, "Plan must be 'free' or 'premium'.");

        // BUG-012: suppliers.TenantId has a FK to tenants — Guid.Empty violated it
        // (tenant 00000000-... does not exist). Platform-managed suppliers are
        // attached to a real system "Platform Marketplace" tenant, created lazily.
        var platformTenantId = await _repo.GetOrCreatePlatformTenantIdAsync(ct);

        var supplier = new Supplier
        {
            TenantId = platformTenantId,
            Name     = request.CompanyName.Trim(),
        };

        await _repo.AddSupplierAsync(supplier, ct);

        var profile = new SupplierProfile
        {
            SupplierId      = supplier.Id,
            TenantId        = platformTenantId,
            Region          = request.Region?.Trim(),
            Categories      = request.Categories is { Length: > 0 }
                                  ? JsonSerializer.Serialize(request.Categories)
                                  : null,
            Website         = request.Website?.Trim(),
            DeliveryRegions = request.DeliveryRegions is { Length: > 0 }
                                  ? JsonSerializer.Serialize(request.DeliveryRegions)
                                  : null,
            WorkingHours    = request.WorkingHours?.Trim(),
            PaymentTerms    = request.PaymentTerms?.Trim(),
            IsPublic        = request.IsPublic,
            Plan            = request.Plan,
        };

        await _repo.AddSupplierProfileAsync(profile, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToFullProfileDto(profile, supplier, null, showPremium: true), null);
    }

    public async Task<(SupplierItemDto? Item, string? Error)> AdminAddSupplierItemAsync(
        Guid supplierId, AdminAddSupplierItemDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CustomName))
            return (null, "CustomName is required.");

        var categoryErrors = SupplierItemCategories.Validate(request.Category, request.Attributes);
        if (categoryErrors.Count > 0)
            return (null, string.Join(" ", categoryErrors));

        if (request.MinQty.HasValue && request.MaxQty.HasValue && request.MaxQty.Value < request.MinQty.Value)
            return (null, "MaxQty must be greater than or equal to MinQty.");

        var supplier = await _repo.GetSupplierByRawIdAsync(supplierId, ct);
        if (supplier is null)
            return (null, "Supplier not found.");

        var item = new SupplierItem
        {
            SupplierId          = supplierId,
            TenantId            = supplier.TenantId,
            CustomName          = request.CustomName.Trim(),
            Price               = request.Price,
            MinQty              = request.MinQty,
            Unit                = request.Unit?.Trim(),
            IsAvailable         = request.IsAvailable,
            Category            = request.Category,
            Attributes          = request.Attributes,
            Brand               = request.Brand?.Trim(),
            Manufacturer        = request.Manufacturer?.Trim(),
            ManufacturerCountry = request.ManufacturerCountry?.Trim(),
            MaxQty              = request.MaxQty,
            GrossWeightKg       = request.GrossWeightKg,
            HeightCm            = request.HeightCm,
            DepthCm             = request.DepthCm,
            WidthCm             = request.WidthCm,
        };

        ReplaceBarcodes(item, request.Barcodes, supplier.TenantId);
        ReplaceImages(item, request.ImageUrls, supplier.TenantId);

        await _repo.AddSupplierItemAsync(item, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToItemDto(item), null);
    }

    public async Task<(SupplierItemDto? Item, string? Error)> AdminUpdateSupplierItemAsync(
        Guid supplierId, Guid itemId, AdminUpdateSupplierItemDto request, CancellationToken ct = default)
    {
        var item = await _repo.GetSupplierItemByIdAsync(supplierId, itemId, ct);
        if (item is null)
            return (null, "Item not found.");

        if (request.CustomName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.CustomName))
                return (null, "CustomName cannot be empty.");
        }

        // Validate the resulting category/attributes state (existing values unless patched).
        var effectiveCategory   = request.Category ?? item.Category;
        var effectiveAttributes = request.Attributes ?? item.Attributes;
        var categoryErrors = SupplierItemCategories.Validate(effectiveCategory, effectiveAttributes);
        if (categoryErrors.Count > 0)
            return (null, string.Join(" ", categoryErrors));

        // Validate the resulting MinQty/MaxQty state (existing values unless patched).
        var effectiveMinQty = request.MinQty ?? item.MinQty;
        var effectiveMaxQty = request.MaxQty ?? item.MaxQty;
        if (effectiveMinQty.HasValue && effectiveMaxQty.HasValue && effectiveMaxQty.Value < effectiveMinQty.Value)
            return (null, "MaxQty must be greater than or equal to MinQty.");

        if (request.CustomName is not null)      item.CustomName = request.CustomName.Trim();
        if (request.Price.HasValue)              item.Price       = request.Price;
        if (request.MinQty.HasValue)              item.MinQty      = request.MinQty;
        if (request.Unit is not null)             item.Unit        = request.Unit.Trim();
        if (request.IsAvailable.HasValue)         item.IsAvailable = request.IsAvailable.Value;
        if (request.Category is not null)         item.Category    = request.Category;
        if (request.Attributes is not null)       item.Attributes  = request.Attributes;
        if (request.Brand is not null)               item.Brand               = request.Brand.Trim();
        if (request.Manufacturer is not null)        item.Manufacturer        = request.Manufacturer.Trim();
        if (request.ManufacturerCountry is not null) item.ManufacturerCountry = request.ManufacturerCountry.Trim();
        if (request.MaxQty.HasValue)                  item.MaxQty        = request.MaxQty;
        if (request.GrossWeightKg.HasValue)           item.GrossWeightKg = request.GrossWeightKg;
        if (request.HeightCm.HasValue)                item.HeightCm      = request.HeightCm;
        if (request.DepthCm.HasValue)                 item.DepthCm       = request.DepthCm;
        if (request.WidthCm.HasValue)                 item.WidthCm       = request.WidthCm;

        if (request.Barcodes is not null) ReplaceBarcodes(item, request.Barcodes, item.TenantId);
        if (request.ImageUrls is not null) ReplaceImages(item, request.ImageUrls, item.TenantId);

        await _repo.SaveChangesAsync(ct);

        return (ToItemDto(item), null);
    }

    /// <summary>
    /// Replaces item.Barcodes in-place from a plain string list: first entry becomes
    /// 'primary', the rest 'alternate'. Null, blank, and duplicate (case-insensitive)
    /// entries are skipped. No-op when <paramref name="barcodes"/> is null.
    /// </summary>
    private static void ReplaceBarcodes(SupplierItem item, List<string>? barcodes, Guid tenantId)
    {
        if (barcodes is null) return;

        item.Barcodes.Clear();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isFirst = true;
        foreach (var raw in barcodes)
        {
            var trimmed = raw?.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (!seen.Add(trimmed)) continue;

            item.Barcodes.Add(new SupplierItemBarcode
            {
                SupplierItemId = item.Id,
                TenantId       = tenantId,
                Barcode        = trimmed,
                Kind           = isFirst ? "primary" : "alternate",
            });
            isFirst = false;
        }
    }

    /// <summary>
    /// Replaces item.Images in-place from a plain URL list: first entry becomes
    /// 'main' (SortOrder 0), the rest 'gallery' (SortOrder = list index). Null/blank
    /// entries are skipped. No-op when <paramref name="imageUrls"/> is null.
    /// </summary>
    private static void ReplaceImages(SupplierItem item, List<string>? imageUrls, Guid tenantId)
    {
        if (imageUrls is null) return;

        item.Images.Clear();

        var isFirst = true;
        var sortOrder = 0;
        foreach (var raw in imageUrls)
        {
            var trimmed = raw?.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            item.Images.Add(new SupplierItemImage
            {
                SupplierItemId = item.Id,
                TenantId       = tenantId,
                Url            = trimmed,
                Kind           = isFirst ? "main" : "gallery",
                SortOrder      = sortOrder,
            });
            isFirst = false;
            sortOrder++;
        }
    }

    public async Task<string?> AdminDeleteSupplierItemAsync(
        Guid supplierId, Guid itemId, CancellationToken ct = default)
    {
        var item = await _repo.GetSupplierItemByIdAsync(supplierId, itemId, ct);
        if (item is null)
            return "Item not found.";

        _repo.RemoveSupplierItem(item);
        await _repo.SaveChangesAsync(ct);

        return null;
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
        new(i.Id, i.ItemId, i.CustomName, i.Item?.Name, i.Price, i.MinQty, i.Unit, i.IsAvailable,
            i.Category, i.Attributes,
            i.Brand, i.Manufacturer, i.ManufacturerCountry, i.MaxQty,
            i.GrossWeightKg, i.HeightCm, i.DepthCm, i.WidthCm,
            i.Barcodes.OrderByDescending(b => b.Kind == "primary").ThenBy(b => b.CreatedAt)
                      .Select(b => b.Barcode).ToList(),
            i.Images.OrderBy(img => img.SortOrder)
                    .Select(img => new SupplierItemImageDto(img.Url, img.Kind, img.SortOrder)).ToList());

    private static SupplierReviewDto ToReviewDto(SupplierReview r) =>
        new(r.Id, r.Rating, r.Comment, r.CreatedAt);

    // ── Item category registry (TASK-294) ─────────────────────────────────────

    public IReadOnlyList<SupplierItemCategoryDto> GetItemCategories() =>
        SupplierItemCategories.All.Select(c => new SupplierItemCategoryDto(
            c.Key,
            c.LabelUa,
            c.Fields.Select(f => new SupplierItemCategoryFieldDto(
                f.Key, f.LabelUa, f.Type.ToString().ToLowerInvariant(), f.Required, f.Options)).ToList()))
            .ToList();

    private static string[]? DeserializeStringArray(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<string[]>(json); }
        catch { return null; }
    }
}
