namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Promotional banner shown in the consumer app's home feed (Consumer App plan,
/// `quirky-questing-hoare.md`, TASK-520) — replaces the hardcoded CONSUMER_NEWS prototype in
/// `mobile/features/loyalty/news.ts` with a real, admin-managed record.
///
/// Visual: an uploaded <see cref="ImageUrl"/>, falling back to <see cref="Icon"/> +
/// <see cref="BackgroundColor"/>/<see cref="AccentColor"/> when no image is set — mirrors the
/// mock UI's existing fallback behavior.
/// Tap target: the admin picks per-banner whether tapping opens an internal detail screen or
/// an external URL (<see cref="DetailMode"/>/<see cref="ExternalUrl"/>).
/// <see cref="Body"/>/<see cref="Terms"/> are plain text, not JSON — paragraphs/bullet lines
/// are joined by "\n" and split back out at read time (`.Split('\n').Where(...)`), matching
/// how the mock data already shapes these fields.
///
/// <see cref="IsActive"/> is a manual pause/soft-hide switch, not a workflow-approval status
/// like <see cref="Discount.Status"/> — there is no pending/approved lifecycle here, so
/// "currently showing" is entirely computed via <see cref="IsCurrentlyActive"/> rather than
/// stored as a separate field. The DELETE endpoint (TASK-521) only ever flips this to false;
/// there is no hard delete.
/// </summary>
public sealed class Banner
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    public string Title { get; private set; } = string.Empty;
    public string? Eyebrow { get; private set; }
    public string Description { get; private set; } = string.Empty;

    /// <summary>Paragraphs joined by "\n" — not JSON.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>Bullet lines joined by "\n" — not JSON.</summary>
    public string Terms { get; private set; } = string.Empty;

    public string? ImageUrl { get; private set; }

    /// <summary>Ionicons name used as a visual fallback when ImageUrl is not set.</summary>
    public string Icon { get; private set; } = "pricetag-outline";

    /// <summary>Hex color, e.g. "#1E40AF" — background behind the icon fallback.</summary>
    public string BackgroundColor { get; private set; } = "#1E40AF";

    /// <summary>Hex color, e.g. "#F59E0B" — accent used for CTA/highlights.</summary>
    public string AccentColor { get; private set; } = "#F59E0B";

    /// <summary>internal | external — see <see cref="BannerDetailMode"/>.</summary>
    public string DetailMode { get; private set; } = BannerDetailMode.Internal;

    /// <summary>Only meaningful when DetailMode = "external".</summary>
    public string? ExternalUrl { get; private set; }

    public DateTime ValidFrom { get; private set; }

    /// <summary>Null = no end date — shows continuously from ValidFrom.</summary>
    public DateTime? ValidUntil { get; private set; }

    /// <summary>Manual pause/soft-hide — never a hard-delete flag.</summary>
    public bool IsActive { get; private set; } = true;

    public int SortOrder { get; private set; }

    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>
    /// Null = draft, never published. Non-null = timestamp of the banner's first publish.
    /// Intentionally separate from <see cref="IsActive"/> (manual pause) and
    /// <see cref="ValidFrom"/>/<see cref="ValidUntil"/> (display window) — a banner can be
    /// published and currently paused, or published and past its window, at the same time.
    /// Set via <see cref="Publish"/> (TASK-524's publish endpoint), never via <see cref="Update"/>.
    /// </summary>
    public DateTime? PublishedAt { get; private set; }

    // Navigation (optional, for eager-loading) — mirrors Discount.Tenant/Creator.
    public Tenant? Tenant { get; private set; }
    public User? Creator { get; private set; }

    private Banner() { }

    public static Banner Create(
        Guid tenantId,
        string title,
        string description,
        string body,
        string terms,
        string icon,
        string backgroundColor,
        string accentColor,
        string detailMode,
        DateTime? validFrom = null,
        DateTime? validUntil = null,
        string? eyebrow = null,
        string? imageUrl = null,
        string? externalUrl = null,
        int sortOrder = 0,
        Guid? createdBy = null,
        bool publishImmediately = true)
    {
        var createdAt = DateTime.UtcNow;
        return new Banner
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title,
            Eyebrow = eyebrow,
            Description = description,
            Body = body,
            Terms = terms,
            ImageUrl = imageUrl,
            Icon = icon,
            BackgroundColor = backgroundColor,
            AccentColor = accentColor,
            DetailMode = detailMode,
            ExternalUrl = externalUrl,
            ValidFrom = validFrom ?? DateTime.UtcNow,
            ValidUntil = validUntil,
            IsActive = true,
            SortOrder = sortOrder,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
            PublishedAt = publishImmediately ? createdAt : null,
        };
    }

    /// <summary>Full-replace update of editable fields (backs PUT /api/banners/{id}, TASK-521).</summary>
    public void Update(
        string title,
        string description,
        string body,
        string terms,
        string icon,
        string backgroundColor,
        string accentColor,
        string detailMode,
        DateTime validFrom,
        DateTime? validUntil,
        string? eyebrow,
        string? imageUrl,
        string? externalUrl,
        int sortOrder)
    {
        Title = title;
        Eyebrow = eyebrow;
        Description = description;
        Body = body;
        Terms = terms;
        ImageUrl = imageUrl;
        Icon = icon;
        BackgroundColor = backgroundColor;
        AccentColor = accentColor;
        DetailMode = detailMode;
        ExternalUrl = externalUrl;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        SortOrder = sortOrder;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Sets the uploaded image path (backs POST /api/banners/{id}/image, TASK-521).</summary>
    public void SetImageUrl(string? imageUrl)
    {
        ImageUrl = imageUrl;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Manual pause/resume — never a hard delete (backs DELETE /api/banners/{id}, TASK-521).</summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Idempotent: only sets <see cref="PublishedAt"/> when it is currently null, so publishing
    /// an already-published banner never overwrites the original publish timestamp. Backs
    /// TASK-524's POST /api/banners/{id}/publish.
    /// </summary>
    public void Publish(DateTime utcNow)
    {
        if (PublishedAt is not null)
        {
            return;
        }

        PublishedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// True when this banner should currently be shown: manually active AND inside its
    /// validity window. Mirrors <see cref="DemandEvent.IsActiveOn"/>'s computed-status pattern.
    /// </summary>
    public bool IsCurrentlyActive(DateTime utcNow) =>
        IsActive && utcNow >= ValidFrom && (ValidUntil == null || utcNow <= ValidUntil);
}

public static class BannerDetailMode
{
    public const string Internal = "internal";
    public const string External = "external";
}
