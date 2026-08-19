namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Typed, whitelist-validated theme record — one row per <see cref="MobileConfiguration"/> (i.e.
/// per tenant, not per version). This is the working record the future Theme Editor (TASK-537)
/// reads/writes directly; <see cref="MobileConfigurationVersion.ConfigurationJson"/> remains the
/// serialized, immutable snapshot produced from it (and from future page/block/navigation tables)
/// at publish time.
///
/// Design decision (TASK-531): CLAUDE CODE SPEC ЕТАП 3 lists <c>MobileTheme</c> as its own domain
/// entity, separate from the generic <c>ConfigurationJson</c> blob, even though MASTER SPEC §11's
/// example API response also nests a <c>theme</c> object inside the same document. Scoping this
/// table per-<see cref="MobileConfiguration"/> (not per-version) resolves that: the Theme Editor
/// always edits one live, directly-queryable/validated row, while every publish serializes its
/// current values into the new version's <c>ConfigurationJson.theme</c> — same relationship a
/// future <c>MobilePage</c>/<c>MobileNavigationItem</c> table would have. See
/// <c>.claude/docs/domain-model.md</c> for the full rationale.
/// </summary>
public sealed class MobileTheme
{
    public Guid Id { get; private set; }
    public Guid MobileConfigurationId { get; private set; }

    /// <summary>
    /// Denormalized from the parent <see cref="MobileConfiguration"/> so RLS can scope this table
    /// directly on its own column, without a join — same house pattern as
    /// <c>LoyaltyLedgerEntry.TenantId</c> (see database-schema.md).
    /// </summary>
    public Guid TenantId { get; private set; }

    public string? LogoUrl { get; private set; }

    /// <summary>Hex color, e.g. "#FFD600".</summary>
    public string PrimaryColor { get; private set; } = "#1E40AF";

    /// <summary>Hex color, e.g. "#222222".</summary>
    public string SecondaryColor { get; private set; } = "#222222";

    /// <summary>Hex color, e.g. "#FFFFFF".</summary>
    public string BackgroundColor { get; private set; } = "#FFFFFF";

    /// <summary>Hex color, e.g. "#F7F7F7".</summary>
    public string SurfaceColor { get; private set; } = "#F7F7F7";

    /// <summary>Hex color, e.g. "#111111".</summary>
    public string TextPrimaryColor { get; private set; } = "#111111";

    /// <summary>Hex color, e.g. "#777777".</summary>
    public string TextSecondaryColor { get; private set; } = "#777777";

    public int ButtonRadius { get; private set; } = 14;
    public int CardRadius { get; private set; } = 18;

    /// <summary>e.g. "comfortable" — whitelisted preset key, not a free-form spacing value.</summary>
    public string SpacingPreset { get; private set; } = "comfortable";

    public DateTime UpdatedAt { get; private set; }

    // Navigation
    public MobileConfiguration? MobileConfiguration { get; private set; }

    private MobileTheme() { }

    /// <summary>Creates the default theme row for a newly created MobileConfiguration.</summary>
    public static MobileTheme CreateDefault(Guid mobileConfigurationId, Guid tenantId)
    {
        return new MobileTheme
        {
            Id = Guid.NewGuid(),
            MobileConfigurationId = mobileConfigurationId,
            TenantId = tenantId,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>Full-replace update of every whitelisted theme field (backs the future Theme Editor PUT, TASK-536).</summary>
    public void Update(
        string? logoUrl,
        string primaryColor,
        string secondaryColor,
        string backgroundColor,
        string surfaceColor,
        string textPrimaryColor,
        string textSecondaryColor,
        int buttonRadius,
        int cardRadius,
        string spacingPreset)
    {
        LogoUrl = logoUrl;
        PrimaryColor = primaryColor;
        SecondaryColor = secondaryColor;
        BackgroundColor = backgroundColor;
        SurfaceColor = surfaceColor;
        TextPrimaryColor = textPrimaryColor;
        TextSecondaryColor = textSecondaryColor;
        ButtonRadius = buttonRadius;
        CardRadius = cardRadius;
        SpacingPreset = spacingPreset;
        UpdatedAt = DateTime.UtcNow;
    }
}
