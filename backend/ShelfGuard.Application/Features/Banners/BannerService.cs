using ShelfGuard.Application.Features.Banners.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Banners;

public sealed class BannerService : IBannerService
{
    private readonly IBannerRepository _repo;

    public BannerService(IBannerRepository repo) => _repo = repo;

    // ── List / Get ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BannerDto>> GetAllAsync(
        Guid tenantId, BannerListQuery query, CancellationToken ct = default)
    {
        var banners = await _repo.GetAllAsync(tenantId, query.LocationId, query.ActiveOnly, ct);
        return await ToDtosAsync(tenantId, banners, ct);
    }

    public async Task<(BannerDto? Banner, string? Error)> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var banner = await _repo.GetByIdAsync(tenantId, id, ct);
        if (banner is null) return (null, "Banner not found.");

        var dtos = await ToDtosAsync(tenantId, [banner], ct);
        return (dtos[0], null);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    public async Task<(BannerDto? Banner, string? Error)> CreateAsync(
        Guid tenantId, Guid? createdBy, CreateBannerRequest request, CancellationToken ct = default)
    {
        var validationError = Validate(
            request.Title, request.Description, request.DetailMode, request.ExternalUrl,
            request.ValidFrom, request.ValidUntil);
        if (validationError is not null) return (null, validationError);

        var banner = Banner.Create(
            tenantId:        tenantId,
            title:           request.Title.Trim(),
            description:     request.Description,
            body:            request.Body,
            terms:           request.Terms,
            icon:            request.Icon,
            backgroundColor: request.BackgroundColor,
            accentColor:     request.AccentColor,
            detailMode:      request.DetailMode,
            validFrom:       request.ValidFrom,
            validUntil:      request.ValidUntil,
            eyebrow:         request.Eyebrow,
            imageUrl:        request.ImageUrl,
            externalUrl:     request.ExternalUrl,
            sortOrder:       request.SortOrder,
            createdBy:       createdBy,
            publishImmediately: request.PublishImmediately);

        await _repo.AddAsync(banner, ct);
        await _repo.ReplaceLocationsAsync(tenantId, banner.Id, request.LocationIds ?? [], ct);
        await _repo.ReplaceProductsAsync(tenantId, banner.Id, request.ProductIds ?? [], ct);
        await _repo.SaveChangesAsync(ct);

        var dtos = await ToDtosAsync(tenantId, [banner], ct);
        return (dtos[0], null);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    public async Task<(BannerDto? Banner, string? Error)> UpdateAsync(
        Guid tenantId, Guid id, UpdateBannerRequest request, CancellationToken ct = default)
    {
        var banner = await _repo.GetByIdAsync(tenantId, id, ct);
        if (banner is null) return (null, "Banner not found.");

        var validationError = Validate(
            request.Title, request.Description, request.DetailMode, request.ExternalUrl,
            request.ValidFrom, request.ValidUntil);
        if (validationError is not null) return (null, validationError);

        banner.Update(
            title:           request.Title.Trim(),
            description:     request.Description,
            body:            request.Body,
            terms:           request.Terms,
            icon:            request.Icon,
            backgroundColor: request.BackgroundColor,
            accentColor:     request.AccentColor,
            detailMode:      request.DetailMode,
            validFrom:       request.ValidFrom,
            validUntil:      request.ValidUntil,
            eyebrow:         request.Eyebrow,
            imageUrl:        request.ImageUrl,
            externalUrl:     request.ExternalUrl,
            sortOrder:       request.SortOrder);

        _repo.Update(banner);
        await _repo.ReplaceLocationsAsync(tenantId, banner.Id, request.LocationIds ?? [], ct);
        await _repo.ReplaceProductsAsync(tenantId, banner.Id, request.ProductIds ?? [], ct);
        await _repo.SaveChangesAsync(ct);

        var dtos = await ToDtosAsync(tenantId, [banner], ct);
        return (dtos[0], null);
    }

    // ── Publish ──────────────────────────────────────────────────────────────

    public async Task<(BannerDto? Banner, string? Error)> PublishAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var banner = await _repo.GetByIdAsync(tenantId, id, ct);
        if (banner is null) return (null, "Banner not found.");

        banner.Publish(DateTime.UtcNow);
        _repo.Update(banner);
        await _repo.SaveChangesAsync(ct);

        var dtos = await ToDtosAsync(tenantId, [banner], ct);
        return (dtos[0], null);
    }

    // ── Deactivate (soft) ────────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> DeactivateAsync(
        Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var banner = await _repo.GetByIdAsync(tenantId, id, ct);
        if (banner is null) return (false, "Banner not found.");

        banner.SetActive(false);
        _repo.Update(banner);
        await _repo.SaveChangesAsync(ct);

        return (true, null);
    }

    // ── Image upload ─────────────────────────────────────────────────────────

    // Mirrors ItemService.UploadImageAsync exactly (same 5MB cap enforced by the controller,
    // same allowed-extension set, same wwwroot/uploads/{feature}/{id}.{ext} disk layout).
    public async Task<(string? Url, string? Error)> UploadImageAsync(
        Guid tenantId, Guid id, Stream imageStream, string fileName, CancellationToken ct = default)
    {
        var banner = await _repo.GetByIdAsync(tenantId, id, ct);
        if (banner is null) return (null, "Banner not found.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        if (!allowed.Contains(ext)) return (null, "Unsupported image format.");

        var uploadDir = Path.Combine("wwwroot", "uploads", "banners");
        Directory.CreateDirectory(uploadDir);

        var fileNameOnDisk = $"{id}{ext}";
        var filePath = Path.Combine(uploadDir, fileNameOnDisk);

        using var fs = new FileStream(filePath, FileMode.Create);
        await imageStream.CopyToAsync(fs, ct);

        var url = $"/uploads/banners/{fileNameOnDisk}";
        banner.SetImageUrl(url);
        _repo.Update(banner);
        await _repo.SaveChangesAsync(ct);

        return (url, null);
    }

    // ── Analytics ────────────────────────────────────────────────────────────

    public async Task<(BannerAnalyticsDto? Analytics, string? Error)> GetAnalyticsAsync(
        Guid tenantId, Guid id, DateTime? from = null, DateTime? toExclusive = null,
        IReadOnlyCollection<Guid>? storeIds = null,
        CancellationToken ct = default)
    {
        var banner = await _repo.GetByIdAsync(tenantId, id, ct);
        if (banner is null) return (null, "Banner not found.");

        var snapshot = await _repo.GetEventAnalyticsAsync(tenantId, id, from, toExclusive, storeIds, ct);
        var (views, clicks) = (snapshot.Views, snapshot.Clicks);
        var ctr = views > 0 ? Math.Round((decimal)clicks / views, 4) : 0m;

        return (new BannerAnalyticsDto(views, clicks, ctr,
            snapshot.Daily.Select(x => new BannerAnalyticsDailyDto(x.Date, x.Views, x.Clicks)).ToList(),
            snapshot.Stores.Select(x => new BannerAnalyticsStoreDto(x.StoreId, x.StoreName, x.Views, x.Clicks,
                x.Views > 0 ? Math.Round((decimal)x.Clicks / x.Views, 4) : 0m)).ToList()), null);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    private static string? Validate(
        string title, string description, string detailMode, string? externalUrl,
        DateTime? validFrom, DateTime? validUntil)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "Title is required.";

        if (string.IsNullOrWhiteSpace(description))
            return "Description is required.";

        if (detailMode is not (BannerDetailMode.Internal or BannerDetailMode.External))
            return $"Invalid detailMode '{detailMode}'. Allowed: {BannerDetailMode.Internal}, {BannerDetailMode.External}.";

        if (detailMode == BannerDetailMode.External && string.IsNullOrWhiteSpace(externalUrl))
            return "ExternalUrl is required when detailMode is 'external'.";

        if (validFrom.HasValue && validUntil.HasValue && validUntil.Value <= validFrom.Value)
            return "ValidUntil must be after ValidFrom.";

        return null;
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private async Task<List<BannerDto>> ToDtosAsync(
        Guid tenantId, IReadOnlyList<Banner> banners, CancellationToken ct)
    {
        if (banners.Count == 0) return [];

        var ids = banners.Select(b => b.Id).ToList();
        var locationsMap = await _repo.GetLocationIdsForBannersAsync(tenantId, ids, ct);
        var productsMap = await _repo.GetProductIdsForBannersAsync(tenantId, ids, ct);
        var countsMap = await _repo.GetEventCountsForBannersAsync(tenantId, ids, ct: ct);
        var now = DateTime.UtcNow;

        return banners.Select(b =>
        {
            var (views, clicks) = countsMap.GetValueOrDefault(b.Id, (0, 0));
            var isCurrentlyActive = b.IsCurrentlyActive(now);
            return new BannerDto(
                b.Id, b.Title, b.Eyebrow, b.Description, b.Body, b.Terms,
                b.ImageUrl, b.Icon, b.BackgroundColor, b.AccentColor,
                b.DetailMode, b.ExternalUrl,
                b.ValidFrom, b.ValidUntil, b.IsActive, isCurrentlyActive,
                b.SortOrder,
                locationsMap.GetValueOrDefault(b.Id, []),
                productsMap.GetValueOrDefault(b.Id, []),
                views, clicks,
                b.CreatedAt, b.UpdatedAt,
                b.PublishedAt, LifecycleStatusOf(b.PublishedAt, isCurrentlyActive));
        }).ToList();
    }

    /// <summary>"draft" | "running" | "past" — derived the same way <see cref="Banner.IsCurrentlyActive"/>
    /// is, never stored. See <see cref="BannerLifecycleStatus"/>.</summary>
    private static string LifecycleStatusOf(DateTime? publishedAt, bool isCurrentlyActive)
    {
        if (publishedAt is null) return BannerLifecycleStatus.Draft;
        return isCurrentlyActive ? BannerLifecycleStatus.Running : BannerLifecycleStatus.Past;
    }
}
