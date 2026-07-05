using System.Text.Json;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Users;
using ShelfGuard.Application.Features.Users.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Supplier cabinet implementation (v4.1, ADR-016).
/// Resolves the calling tenant's owner-managed supplier once per operation and
/// then reuses MarketplaceService Admin* item methods parameterized by that
/// supplier id — cabinet authorization scoping lives in one place.
/// </summary>
public sealed class SupplierCabinetService : ISupplierCabinetService
{
    public const string CabinetNotAvailableError =
        "Supplier cabinet is not available for this tenant.";

    private readonly IMarketplaceRepository _repo;
    private readonly IMarketplaceService _marketplace;
    private readonly IUserService _users;

    public SupplierCabinetService(
        IMarketplaceRepository repo, IMarketplaceService marketplace, IUserService users)
    {
        _repo        = repo;
        _marketplace = marketplace;
        _users       = users;
    }

    // ── Profile ───────────────────────────────────────────────────────────────

    public async Task<(SupplierProfileDto? Profile, string? Error)> GetProfileAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        var (profile, supplier) = resolved.Value;
        var metrics = await _repo.GetMetricsBySupplierIdAsync(supplier.Id, ct);

        return (ToProfileDto(profile, supplier, metrics), null);
    }

    public async Task<(SupplierProfileDto? Profile, string? Error)> UpdateProfileAsync(
        Guid tenantId, CabinetProfileUpdateDto request, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        var (profile, supplier) = resolved.Value;

        // Patch semantics — only provided fields are applied.
        // IsPublic (publish toggle) and Plan are intentionally not editable here.
        if (request.Region is not null)
            profile.Region = request.Region.Trim();
        if (request.Categories is not null)
            profile.Categories = JsonSerializer.Serialize(request.Categories);
        if (request.Website is not null)
            profile.Website = request.Website.Trim();
        if (request.DeliveryRegions is not null)
            profile.DeliveryRegions = JsonSerializer.Serialize(request.DeliveryRegions);
        if (request.WorkingHours is not null)
            profile.WorkingHours = request.WorkingHours.Trim();
        if (request.PaymentTerms is not null)
            profile.PaymentTerms = request.PaymentTerms.Trim();

        profile.UpdatedAt = DateTimeOffset.UtcNow;

        _repo.UpdateProfile(profile);
        await _repo.SaveChangesAsync(ct);

        return (ToProfileDto(profile, supplier, null), null);
    }

    public async Task<(SupplierProfileDto? Profile, string? Error)> TogglePublishAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        var (profile, supplier) = resolved.Value;

        profile.IsPublic  = !profile.IsPublic;
        profile.UpdatedAt = DateTimeOffset.UtcNow;

        _repo.UpdateProfile(profile);
        await _repo.SaveChangesAsync(ct);

        return (ToProfileDto(profile, supplier, null), null);
    }

    // ── Items (reuse MarketplaceService Admin* methods, scoped to own supplier) ──

    public async Task<(IReadOnlyList<SupplierItemDto>? Items, string? Error)> GetItemsAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        var items = await _repo.GetSupplierItemsForOwnerAsync(resolved.Value.Supplier.Id, ct);
        return (items.Select(ToItemDto).ToList(), null);
    }

    public async Task<(SupplierItemDto? Item, string? Error)> AddItemAsync(
        Guid tenantId, AdminAddSupplierItemDto request, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        return await _marketplace.AdminAddSupplierItemAsync(resolved.Value.Supplier.Id, request, ct);
    }

    public async Task<(SupplierItemDto? Item, string? Error)> UpdateItemAsync(
        Guid tenantId, Guid itemId, AdminUpdateSupplierItemDto request, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        return await _marketplace.AdminUpdateSupplierItemAsync(
            resolved.Value.Supplier.Id, itemId, request, ct);
    }

    public async Task<string?> DeleteItemAsync(
        Guid tenantId, Guid itemId, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return CabinetNotAvailableError;

        return await _marketplace.AdminDeleteSupplierItemAsync(resolved.Value.Supplier.Id, itemId, ct);
    }

    // ── Reviews / metrics (read-only) ─────────────────────────────────────────

    public async Task<(PagedResult<PublicSupplierReviewDto>? Reviews, string? Error)> GetReviewsAsync(
        Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        // Unlike the public marketplace endpoint (IMarketplaceService.GetSupplierReviewsAsync),
        // the cabinet must NOT gate on IsPublic — an owner viewing their own reviews tab is
        // already tenant-scoped via ResolveAsync above, and must see their own reviews (even
        // zero of them) regardless of publish status. Go straight to the repository instead of
        // routing through the public, publish-gated method (see bug fix, TASK reviews-cabinet-bug).
        var supplierId = resolved.Value.Supplier.Id;

        var rows  = await _repo.GetReviewsBySupplierAsync(supplierId, page, pageSize, ct);
        var total = await _repo.CountReviewsBySupplierAsync(supplierId, ct);

        var items = rows
            .Select(r => new PublicSupplierReviewDto(
                r.Review.Id, r.Review.Rating, r.Review.Comment, r.Review.CreatedAt, r.ReviewerName))
            .ToList();

        return (new PagedResult<PublicSupplierReviewDto>(items, total, page, pageSize), null);
    }

    public async Task<(SupplierMetricsDto? Metrics, string? Error)> GetMetricsAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        var m = await _repo.GetMetricsBySupplierIdAsync(resolved.Value.Supplier.Id, ct);
        if (m is null)
            return (new SupplierMetricsDto(null, null, null, null, null, null, DateTimeOffset.UtcNow), null);

        return (new SupplierMetricsDto(
            m.Rating, m.AvgDeliveryDays, m.OrderAccuracy, m.QualityScore,
            m.CancellationRate, m.ResponseTimeHours, m.UpdatedAt), null);
    }

    // ── Staff management (self-service) ──────────────────────────────────────

    public Task<IReadOnlyList<UserDto>> GetStaffAsync(Guid tenantId, CancellationToken ct = default) =>
        _users.GetAllAsync(tenantId, ct);

    public Task<(UserDto? User, string? Error)> InviteStaffAsync(
        Guid tenantId, CabinetInviteStaffDto request, string inviterName, CancellationToken ct = default)
    {
        // Role is never accepted from the client — suppliers don't need finer role
        // granularity for MVP, every invited teammate is a supplier_admin.
        var inviteRequest = new InviteUserRequest(
            request.Email, request.FullName, AppRoles.SupplierAdmin, request.Password);

        return _users.InviteAsync(tenantId, inviteRequest, inviterName, ct);
    }

    public async Task<string?> DeactivateStaffAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        // Verify the target belongs to the caller's own tenant before deactivating —
        // never trust a client-supplied id to cross tenant boundaries.
        var (user, error) = await _users.GetByIdAsync(tenantId, userId, ct);
        if (user is null) return error ?? "User not found.";

        return await _users.DeactivateAsync(tenantId, userId, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the calling tenant's owner-managed Supplier/Profile pair, lazily
    /// creating it on first cabinet access if the tenant is business_type = "supplier"
    /// but has none yet (TASK-289 self-heal for tenants onboarded before the
    /// provider-path hook existed, or created directly in the DB). Persistence and
    /// race-safety (concurrent first-access requests) are the repository's responsibility
    /// (mirrors MarketplaceRepository.GetOrCreatePlatformTenantIdAsync, BUG-012).
    /// </summary>
    private async Task<(SupplierProfile Profile, Supplier Supplier)?> ResolveAsync(
        Guid tenantId, CancellationToken ct)
    {
        var existing = await _repo.GetOwnerManagedProfileAsync(tenantId, ct);
        if (existing is not null) return existing;

        var tenantInfo = await _repo.GetTenantOnboardingInfoAsync(tenantId, ct);
        if (tenantInfo is null || !SupplierOnboarding.IsSupplierBusinessType(tenantInfo.Value.BusinessType))
            return null;

        var (supplier, profile) = SupplierOnboarding.CreateOwnerManaged(tenantId, tenantInfo.Value.Name);
        return await _repo.GetOrCreateOwnerManagedProfileAsync(supplier, profile, ct);
    }

    private static SupplierProfileDto ToProfileDto(
        SupplierProfile p, Supplier s, SupplierMetrics? m) =>
        new(
            s.Id,
            s.Name,
            p.Region,
            DeserializeStringArray(p.Categories),
            p.Website,
            DeserializeStringArray(p.DeliveryRegions),
            p.WorkingHours,
            p.PaymentTerms,
            p.IsPublic,
            p.Plan,
            m is not null
                ? new SupplierMetricsDto(
                    m.Rating, m.AvgDeliveryDays, m.OrderAccuracy, m.QualityScore,
                    m.CancellationRate, m.ResponseTimeHours, m.UpdatedAt)
                : null);

    private static SupplierItemDto ToItemDto(SupplierItem i) =>
        new(i.Id, i.ItemId, i.CustomName, i.Item?.Name, i.Price, i.MinQty, i.Unit, i.IsAvailable,
            i.Category, i.Attributes,
            i.Brand, i.Manufacturer, i.ManufacturerCountry, i.MaxQty,
            i.GrossWeightKg, i.HeightCm, i.DepthCm, i.WidthCm,
            i.Barcodes.OrderByDescending(b => b.Kind == "primary").ThenBy(b => b.CreatedAt)
                      .Select(b => b.Barcode).ToList(),
            i.Images.OrderBy(img => img.SortOrder)
                    .Select(img => new SupplierItemImageDto(img.Url, img.Kind, img.SortOrder)).ToList());

    private static string[]? DeserializeStringArray(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<string[]>(json); }
        catch { return null; }
    }
}
