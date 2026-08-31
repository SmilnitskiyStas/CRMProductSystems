namespace ShelfGuard.Application.Features.Marketplace.Dtos;

/// <summary>Compact supplier card for the public marketplace listing.</summary>
public record SupplierListItemDto(
    Guid Id,
    string Name,
    string? Region,
    string Plan,
    string[]? Categories,
    decimal? Rating,
    int? AvgDeliveryDays,
    bool IsPublic);

/// <summary>Full supplier profile. Premium fields are null for unauthenticated / free-plan callers.</summary>
/// <remarks>
/// <see cref="DeliveryCoverage"/> (TASK-650) is NOT premium-gated — it is populated for every
/// caller. <see cref="DeliveryRegions"/> is the deprecated legacy free-text list, fed from the
/// obsolete <c>supplier_profiles.DeliveryRegions</c> column until the T14 backfill runs.
/// </remarks>
public record SupplierProfileDto(
    Guid SupplierId,
    string SupplierName,
    string? Region,
    string[]? Categories,
    string? Website,
    string[]? DeliveryRegions,
    string? WorkingHours,
    string? PaymentTerms,
    bool IsPublic,
    string Plan,
    SupplierMetricsDto? Metrics,
    SupplierReviewStatsDto? ReviewStats = null,
    DeliveryCoverageDto? DeliveryCoverage = null);

public record SupplierMetricsDto(
    decimal? Rating,
    decimal? AvgDeliveryDays,
    decimal? OrderAccuracy,
    decimal? QualityScore,
    decimal? CancellationRate,
    decimal? ResponseTimeHours,
    DateTimeOffset UpdatedAt,
    // TASK-650: worker-computed delivery/response aggregates (nullable — the nightly job may not
    // have run yet, or there may be no data behind a given metric).
    IReadOnlyList<RegionDeliveryStatDto>? DeliveryByRegion = null,
    int? DeliverySampleSize = null,
    int? ResponseSampleSize = null,
    DateTimeOffset? AggregatesComputedAt = null);

// ── Delivery coverage (TASK-650 / plan «eventual-whistling-rabbit») ───────────

/// <summary>One served region plus optional free-text delivery terms for that region.</summary>
public record DeliveryCoverageEntryDto(string RegionCode, string? Terms);

/// <summary>
/// A supplier's declared delivery coverage. <see cref="Served"/> and <see cref="NotServed"/> are
/// mutually-exclusive region-code sets; <see cref="Note"/> is a free-text catch-all. Persisted as
/// a JSONB string on <c>supplier_profiles.DeliveryCoverage</c>; (de)serialized and validated via
/// <see cref="ShelfGuard.Application.Features.Marketplace.DeliveryCoverageJson"/>.
/// </summary>
public record DeliveryCoverageDto(
    IReadOnlyList<DeliveryCoverageEntryDto> Served,
    IReadOnlyList<string> NotServed,
    string? Note);

/// <summary>Measured average delivery time to one destination region (nightly worker job).</summary>
public record RegionDeliveryStatDto(string RegionCode, decimal AvgDeliveryDays, int SampleSize);

/// <summary>
/// A supplier's delivery coverage resolved against one buyer's region (served by
/// <c>GET /api/marketplace/suppliers/{id}/coverage</c>, TASK-651).
/// </summary>
/// <param name="BuyerRegionStatus"><c>"served"</c> | <c>"not_served"</c> | <c>"unknown"</c>.</param>
public record SupplierCoverageForBuyerDto(
    DeliveryCoverageDto Coverage,
    string? BuyerRegionCode,
    string BuyerRegionStatus,
    string? BuyerRegionTerms,
    decimal? MeasuredAvgDeliveryDaysToBuyerRegion,
    int? MeasuredSampleSize);

public record SupplierItemDto(
    Guid Id,
    Guid? ItemId,
    string? CustomName,
    string? ItemName,
    decimal? Price,
    int? MinQty,
    string? Unit,
    bool IsAvailable,
    string? Category = null,
    Dictionary<string, object?>? Attributes = null,
    string? Brand = null,
    string? Manufacturer = null,
    string? ManufacturerCountry = null,
    int? MaxQty = null,
    decimal? GrossWeightKg = null,
    decimal? HeightCm = null,
    decimal? DepthCm = null,
    decimal? WidthCm = null,
    IReadOnlyList<string>? Barcodes = null,
    IReadOnlyList<SupplierItemImageDto>? Images = null);

/// <summary>A single supplier-item image, ordered by SortOrder. Kind is 'main' | 'gallery'.</summary>
public record SupplierItemImageDto(string Url, string Kind, int SortOrder);

public record SupplierReviewCreateDto(int Rating, string? Comment);

public record SupplierReviewDto(
    Guid Id,
    int Rating,
    string? Comment,
    DateTimeOffset CreatedAt);

public record SupplierSearchDto(string ItemName, string? Region);

/// <remarks>
/// Patch semantics — a non-null field replaces, null leaves the stored value untouched.
/// <see cref="DeliveryRegions"/> is retained for wire compatibility only and is IGNORED
/// (TASK-650); send <see cref="DeliveryCoverage"/> instead.
/// </remarks>
public record SupplierProfileUpdateDto(
    string? Region,
    string[]? Categories,
    string? Website,
    string[]? DeliveryRegions,
    string? WorkingHours,
    string? PaymentTerms,
    bool? IsPublic,
    string? Plan,
    DeliveryCoverageDto? DeliveryCoverage = null);

/// <summary>Paginated list response.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// ── Platform admin DTOs (TASK-275) ───────────────────────────────────────────

public record AdminAddSupplierItemDto(
    string CustomName,
    decimal? Price,
    int? MinQty,
    string? Unit,
    bool IsAvailable,
    string? Category = null,
    Dictionary<string, object?>? Attributes = null,
    string? Brand = null,
    string? Manufacturer = null,
    string? ManufacturerCountry = null,
    int? MaxQty = null,
    decimal? GrossWeightKg = null,
    decimal? HeightCm = null,
    decimal? DepthCm = null,
    decimal? WidthCm = null,
    /// <summary>Plain barcode strings. First = primary, rest = alternate. Null/blank/duplicate entries are skipped.</summary>
    List<string>? Barcodes = null,
    /// <summary>Plain image URLs. First = main, rest = gallery (SortOrder = list index). Null/blank entries are skipped.</summary>
    List<string>? ImageUrls = null);

/// <summary>Patch-semantics item update — only non-null fields are applied.
/// Barcodes/ImageUrls collections are only replaced when the corresponding list is
/// explicitly provided (non-null); left untouched otherwise.</summary>
public record AdminUpdateSupplierItemDto(
    string? CustomName,
    decimal? Price,
    int? MinQty,
    string? Unit,
    bool? IsAvailable,
    string? Category = null,
    Dictionary<string, object?>? Attributes = null,
    string? Brand = null,
    string? Manufacturer = null,
    string? ManufacturerCountry = null,
    int? MaxQty = null,
    decimal? GrossWeightKg = null,
    decimal? HeightCm = null,
    decimal? DepthCm = null,
    decimal? WidthCm = null,
    List<string>? Barcodes = null,
    List<string>? ImageUrls = null);

// ── Supplier cabinet (v4.1, TASK-284, ADR-016) ───────────────────────────────

/// <summary>
/// Cabinet profile update. Publish state is toggled via POST /profile/publish,
/// and plan is provider-managed — neither is editable here.
/// </summary>
public record CabinetProfileUpdateDto(
    string? Region,
    string[]? Categories,
    string? Website,
    string[]? DeliveryRegions,
    string? WorkingHours,
    string? PaymentTerms,
    // TASK-650: patch semantics — non-null replaces, null leaves untouched. DeliveryRegions above
    // is kept for wire-compat only and is ignored.
    DeliveryCoverageDto? DeliveryCoverage = null);

// ── Supplier cabinet staff management (self-service) ─────────────────────────

/// <summary>Invite request for a new staff member of the caller's own supplier tenant.
/// Base system role is always supplier_admin server-side. When <paramref name="SupplierRoleId"/>
/// is provided, the invited user's effective permissions are narrowed to that custom
/// supplier_roles row (Permissions resolved to a Dictionary&lt;string,bool&gt;). When omitted,
/// the invited user keeps full access (Permissions = null), same as before TASK-306.</summary>
public record CabinetInviteStaffDto(
    string Email,
    string FullName,
    string Password,
    Guid? SupplierRoleId = null);

// ── Supplier cabinet roles (TASK-306) ────────────────────────────────────────

/// <summary>Custom staff role scoped to the caller's own supplier tenant.</summary>
public record SupplierRoleDto(
    Guid Id,
    string DisplayName,
    string BaseRole,
    string[] Permissions,
    bool IsSystem);

public record CreateSupplierRoleRequest(
    string DisplayName,
    string BaseRole,
    string[] Permissions);

public record UpdateSupplierRoleRequest(
    string DisplayName,
    string BaseRole,
    string[] Permissions);

// ── Supplier cabinet task board (TASK-306) ───────────────────────────────────

/// <summary>A task on the caller's own supplier task board.</summary>
public record SupplierTaskDto(
    Guid Id,
    Guid? ClientTenantId,
    string? ClientTenantName,
    Guid? AssignedToUserId,
    string? AssignedToUserName,
    string Title,
    string? Description,
    string Status,
    DateTime? DueDate,
    Guid? CreatedByUserId,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record CreateSupplierTaskRequest(
    string Title,
    string? Description,
    Guid? ClientTenantId,
    Guid? AssignedToUserId,
    DateTime? DueDate);

public record UpdateSupplierTaskRequest(
    string Title,
    string? Description,
    Guid? ClientTenantId,
    Guid? AssignedToUserId,
    DateTime? DueDate);

public record UpdateSupplierTaskStatusRequest(string Status);

// ── Public reviews (v4.1, TASK-285) ──────────────────────────────────────────

/// <summary>Public review representation — reviewer exposed by display name only (no tenant id).</summary>
public record PublicSupplierReviewDto(
    Guid Id,
    int Rating,
    string? Comment,
    DateTimeOffset CreatedAt,
    string ReviewerName,
    string? ReplyText = null,
    DateTimeOffset? RepliedAt = null);

// ── Supplier cabinet review reply (self-service) ─────────────────────────────

/// <summary>Request body to post/update the supplier's one reply to a review.</summary>
public record CabinetReplyToReviewDto(string ReplyText);

/// <summary>
/// On-read review breakdown for the caller's own supplier. Convention: rating 4-5 =
/// positive, 3 = neutral, 1-2 = negative. Not persisted — computed from the existing
/// ratings list on every call.
/// </summary>
public record SupplierReviewStatsDto(
    int Positive,
    int Neutral,
    int Negative,
    int Total,
    decimal? AverageRating);

// ── AI Supplier Recommendation (TASK-223) ─────────────────────────────────────

public record AiRecommendRequestDto(
    string ItemName,
    string? Region,
    int? RequiredQty,
    string? Notes);

public record AiRecommendResultDto(
    List<SupplierRecommendationDto> Recommendations,
    string Prompt);

public record SupplierRecommendationDto(
    Guid SupplierId,
    string SupplierName,
    int Rank,
    decimal Score,
    string Reasoning,
    SupplierItemDto? MatchedItem,
    SupplierMetricsDto? Metrics);

// ── Supplier cabinet clients tab (TASK-313, calm-singing-marble) ────────────

/// <summary>
/// A client tenant the caller's own supplier has interacted with — union of tenants
/// that left a review and/or have a task linked via ClientTenantId. LastInteractionAt
/// is the max of the most recent review/task dates for that tenant.
/// </summary>
public record SupplierClientDto(
    Guid TenantId,
    string TenantName,
    int ReviewCount,
    decimal? AvgRating,
    int TaskCount,
    DateTimeOffset LastInteractionAt);

// ── Supplier ↔ client chat (TASK-313, calm-singing-marble, TASK-312 schema) ──

/// <summary>A chat session summary, with the other side's tenant id/name denormalized.</summary>
public record SupplierChatSessionDto(
    Guid Id,
    Guid OtherTenantId,
    string OtherTenantName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? LastMessage,
    DateTimeOffset? LastMessageAt,
    int UnreadCount = 0);

public record SupplierChatMessageDto(
    Guid Id,
    Guid SessionId,
    Guid SenderTenantId,
    Guid SenderUserId,
    string SenderName,
    string Body,
    bool IsRead,
    DateTimeOffset CreatedAt);

public record SendSupplierChatMessageRequest(string Body);

// ── Item category registry (TASK-294, ADR-017 §4) ────────────────────────────

public record SupplierItemCategoryFieldDto(
    string Key,
    string LabelUa,
    string Type,
    bool Required,
    IReadOnlyList<string>? Options);

public record SupplierItemCategoryDto(
    string Key,
    string LabelUa,
    IReadOnlyList<SupplierItemCategoryFieldDto> Fields);
