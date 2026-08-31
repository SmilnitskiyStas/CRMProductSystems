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
    private readonly IUserRepository _userRepo;
    private readonly ISupplierRolesRepository _rolesRepo;
    private readonly ISupplierTaskRepository _taskRepo;

    public SupplierCabinetService(
        IMarketplaceRepository repo, IMarketplaceService marketplace, IUserService users,
        IUserRepository userRepo, ISupplierRolesRepository rolesRepo, ISupplierTaskRepository taskRepo)
    {
        _repo        = repo;
        _marketplace = marketplace;
        _users       = users;
        _userRepo    = userRepo;
        _rolesRepo   = rolesRepo;
        _taskRepo    = taskRepo;
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

        // TASK-650: DeliveryRegions is no longer written — validate structured coverage first.
        if (request.DeliveryCoverage is not null)
        {
            var coverageErrors = DeliveryCoverageJson.Validate(request.DeliveryCoverage);
            if (coverageErrors.Count > 0)
                return (null, string.Join(" ", coverageErrors));
        }

        // Patch semantics — only provided fields are applied.
        // IsPublic (publish toggle) and Plan are intentionally not editable here.
        if (request.Region is not null)
            profile.Region = request.Region.Trim();
        if (request.Categories is not null)
            profile.Categories = JsonSerializer.Serialize(request.Categories);
        if (request.Website is not null)
            profile.Website = request.Website.Trim();
        if (request.DeliveryCoverage is not null)
            profile.DeliveryCoverage = DeliveryCoverageJson.Serialize(request.DeliveryCoverage);
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
                r.Review.Id, r.Review.Rating, r.Review.Comment, r.Review.CreatedAt, r.ReviewerName,
                r.Review.ReplyText, r.Review.RepliedAt))
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

        return (MarketplaceService.ToMetricsDto(m), null);
    }

    public const int MaxReplyTextLength = 2000;
    public const string ReviewNotFoundError = "Review not found.";

    public async Task<(PublicSupplierReviewDto? Review, string? Error)> ReplyToReviewAsync(
        Guid tenantId, Guid reviewId, string replyText, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        if (string.IsNullOrWhiteSpace(replyText))
            return (null, "Reply text is required.");

        var trimmed = replyText.Trim();
        if (trimmed.Length > MaxReplyTextLength)
            return (null, $"Reply text cannot exceed {MaxReplyTextLength} characters.");

        var supplierId = resolved.Value.Supplier.Id;

        // Cross-tenant guard: the review must belong to THIS supplier. Never reveal
        // whether it exists for a different supplier — same "not found" wording
        // used elsewhere in this controller (e.g. DeactivateStaffAsync, item lookups).
        //
        // TASK-643/KI-036: the review row belongs to the REVIEWER tenant, not this supplier's,
        // so the lookup and the reply write must share one provider-role transaction inside the
        // repository — see IMarketplaceRepository.SetReviewReplyAsync. Nothing else is staged on
        // this request, which satisfies that method's flush-first caller contract.
        var review = await _repo.SetReviewReplyAsync(
            supplierId, reviewId, trimmed, DateTimeOffset.UtcNow, ct);
        if (review is null) return (null, ReviewNotFoundError);

        var reviewerName = review.Tenant?.Name ?? string.Empty;
        return (new PublicSupplierReviewDto(
            review.Id, review.Rating, review.Comment, review.CreatedAt, reviewerName,
            review.ReplyText, review.RepliedAt), null);
    }

    public async Task<(SupplierReviewStatsDto? Stats, string? Error)> GetReviewStatsAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        var ratings = await _repo.GetReviewRatingsAsync(resolved.Value.Supplier.Id, ct);
        return (BuildStats(ratings), null);
    }

    internal static SupplierReviewStatsDto BuildStats(IReadOnlyList<short> ratings)
    {
        if (ratings.Count == 0)
            return new SupplierReviewStatsDto(0, 0, 0, 0, null);

        var positive = ratings.Count(r => r >= 4);
        var neutral  = ratings.Count(r => r == 3);
        var negative = ratings.Count(r => r <= 2);
        var average  = Math.Round((decimal)ratings.Average(r => (int)r), 2);

        return new SupplierReviewStatsDto(positive, neutral, negative, ratings.Count, average);
    }

    // ── Clients (self-service, TASK-313) ─────────────────────────────────────

    public async Task<(IReadOnlyList<SupplierClientDto>? Clients, string? Error)> GetClientsAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(tenantId, ct);
        if (resolved is null) return (null, CabinetNotAvailableError);

        var supplierId = resolved.Value.Supplier.Id;

        // Data volume per supplier is not large enough to justify SQL-side aggregation
        // (calm-singing-marble plan) — pull the full review set and merge in-memory.
        var reviewCount = await _repo.CountReviewsBySupplierAsync(supplierId, ct);
        var reviewRows = reviewCount > 0
            ? await _repo.GetReviewsBySupplierAsync(supplierId, 1, reviewCount, ct)
            : Array.Empty<(SupplierReview Review, string ReviewerName)>();

        var taskRows = await _taskRepo.GetDistinctClientTenantsAsync(tenantId, ct);

        var merged = new Dictionary<Guid, ClientAccumulator>();

        foreach (var (review, reviewerName) in reviewRows)
        {
            if (!merged.TryGetValue(review.TenantId, out var acc))
            {
                acc = new ClientAccumulator { TenantName = reviewerName };
                merged[review.TenantId] = acc;
            }
            acc.ReviewCount++;
            acc.RatingSum += review.Rating;
            if (review.CreatedAt > acc.LastInteractionAt)
                acc.LastInteractionAt = review.CreatedAt;
        }

        foreach (var (clientTenantId, name, taskCount, lastTaskAt) in taskRows)
        {
            if (!merged.TryGetValue(clientTenantId, out var acc))
            {
                acc = new ClientAccumulator { TenantName = name ?? string.Empty };
                merged[clientTenantId] = acc;
            }
            else if (string.IsNullOrEmpty(acc.TenantName) && name is not null)
            {
                acc.TenantName = name;
            }
            acc.TaskCount = taskCount;
            var lastTaskAtOffset = new DateTimeOffset(DateTime.SpecifyKind(lastTaskAt, DateTimeKind.Utc));
            if (lastTaskAtOffset > acc.LastInteractionAt)
                acc.LastInteractionAt = lastTaskAtOffset;
        }

        var clients = merged
            .Select(kv => new SupplierClientDto(
                kv.Key,
                kv.Value.TenantName,
                kv.Value.ReviewCount,
                kv.Value.ReviewCount > 0 ? Math.Round(kv.Value.RatingSum / kv.Value.ReviewCount, 2) : null,
                kv.Value.TaskCount,
                kv.Value.LastInteractionAt))
            .OrderByDescending(c => c.LastInteractionAt)
            .ToList();

        return (clients, null);
    }

    private sealed class ClientAccumulator
    {
        public string TenantName { get; set; } = string.Empty;
        public int ReviewCount { get; set; }
        public decimal RatingSum { get; set; }
        public int TaskCount { get; set; }
        public DateTimeOffset LastInteractionAt { get; set; } = DateTimeOffset.MinValue;
    }

    // ── Staff management (self-service) ──────────────────────────────────────

    public Task<IReadOnlyList<UserDto>> GetStaffAsync(Guid tenantId, CancellationToken ct = default) =>
        _users.GetAllAsync(tenantId, ct: ct);

    public async Task<(UserDto? User, string? Error)> InviteStaffAsync(
        Guid tenantId, Guid actingUserId, CabinetInviteStaffDto request, string inviterName, CancellationToken ct = default)
    {
        // Base system role is never accepted from the client — every invited teammate
        // is a supplier_admin. Finer-grained access (TASK-306) is layered on top via
        // SupplierRoleId → Permissions, following the same convention as ProviderTeamService.
        SupplierRole? role = null;
        if (request.SupplierRoleId.HasValue)
        {
            role = await _rolesRepo.GetByIdAsync(tenantId, request.SupplierRoleId.Value, ct);
            if (role is null)
                return (null, "Role not found.");
        }

        var inviteRequest = new InviteUserRequest(
            request.Email, request.FullName, AppRoles.SupplierAdmin, request.Password);

        var (user, error) = await _users.InviteAsync(tenantId, actingUserId, inviteRequest, inviterName, ct);
        if (user is null) return (null, error);

        // No role given → full access (Permissions = null), same as before TASK-306.
        if (role is null) return (user, null);

        var createdUser = await _userRepo.GetByIdAsync(user.Id, ct);
        if (createdUser is null) return (user, null);

        var permissions = role.Permissions.ToDictionary(p => p, _ => true);
        createdUser.SetPermissions(permissions);
        createdUser.SetSupplierRole(role.Id);
        _userRepo.Update(createdUser);
        await _userRepo.SaveChangesAsync(ct);

        return (user with { Permissions = permissions }, null);
    }

    public async Task<string?> DeactivateStaffAsync(
        Guid tenantId, Guid actingUserId, Guid userId, CancellationToken ct = default)
    {
        // Verify the target belongs to the caller's own tenant before deactivating —
        // never trust a client-supplied id to cross tenant boundaries.
        var (user, error) = await _users.GetByIdAsync(tenantId, userId, ct);
        if (user is null) return error ?? "User not found.";

        return await _users.DeactivateAsync(tenantId, actingUserId, userId, ct);
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
#pragma warning disable CS0618 // deprecated legacy free-text list — still surfaced until the T14 backfill runs
            DeserializeStringArray(p.DeliveryRegions),
#pragma warning restore CS0618
            p.WorkingHours,
            p.PaymentTerms,
            p.IsPublic,
            p.Plan,
            m is not null ? MarketplaceService.ToMetricsDto(m) : null,
            ReviewStats: null,
            DeliveryCoverage: DeliveryCoverageJson.Parse(p.DeliveryCoverage));

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
