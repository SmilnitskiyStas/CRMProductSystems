namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// Single source of truth for <see cref="ShelfGuard.Domain.Entities.MobileTheme"/>'s writable-field
/// whitelist (TASK-536) — mirrors <see cref="MobileConfigWhitelists"/>'s role for the draft config
/// document, but for the separate theme domain (TASK-532/533 deliberately gave
/// <see cref="MobileConfigWhitelists"/> no theme concept — see <c>MobileConfigValidator</c> and
/// <c>MobileConfigSchemaContractTests</c> remarks). Kept dependency-free so
/// <c>/contracts/mobile-config.schema.json</c>'s <c>theme</c> sub-schema and its agreement test
/// (<c>MobileConfigSchemaContractTests</c>) can read the same constants without pulling in
/// validation logic.
/// </summary>
public static class MobileThemeWhitelists
{
    /// <summary>
    /// Resolves TASK-533's flagged <c>theme.spacing</c> gap (left as a free non-empty string
    /// pending this task — see <c>contracts/mobile-config.schema.json</c>'s top-level description).
    /// Deliberately matches the already-shipped mobile-side precedent exactly —
    /// <c>mobile/features/mobile-config/types.ts</c>'s <c>RetailThemeConfig.spacing</c> union
    /// (<c>'compact' | 'comfortable'</c>) and <c>mobile/features/mobile-config/validation.ts</c>'s
    /// AJV <c>enum: ['compact', 'comfortable']</c> — rather than inventing a third preset (e.g.
    /// "spacious") the mobile client doesn't know about and would reject.
    /// </summary>
    public static readonly IReadOnlySet<string> SpacingPresets = new HashSet<string> { "compact", "comfortable" };

    /// <summary>
    /// Matches <c>mobile/features/mobile-config/validation.ts</c>'s <c>buttons.radius</c> bound
    /// (<c>minimum: 0, maximum: 32</c>) — the only concrete precedent for this bound anywhere in
    /// the repo; MASTER SPEC §7's example (<c>"radius": 14</c>) and
    /// <c>contracts/mobile-config.schema.json</c> only constrain the minimum today.
    /// </summary>
    public const int MinButtonRadius = 0;

    /// <summary>See <see cref="MinButtonRadius"/>.</summary>
    public const int MaxButtonRadius = 32;

    /// <summary>
    /// Matches <c>mobile/features/mobile-config/validation.ts</c>'s <c>cards.radius</c> bound
    /// (<c>minimum: 0, maximum: 40</c>).
    /// </summary>
    public const int MinCardRadius = 0;

    /// <summary>See <see cref="MinCardRadius"/>.</summary>
    public const int MaxCardRadius = 40;

    /// <summary>
    /// Matches <c>mobile/features/mobile-config/validation.ts</c>'s <c>tenant.logoUrl</c>
    /// <c>maxLength</c> (2048) — reused here since <c>MobileTheme.LogoUrl</c> is the same kind of
    /// field (an optional absolute URL shown in the mobile client).
    /// </summary>
    public const int MaxLogoUrlLength = 2048;

    /// <summary>
    /// Same pattern <c>contracts/mobile-config.schema.json</c>'s <c>#/definitions/hexColor</c>
    /// already enforces for the served document — kept as a string constant (not a compiled
    /// <c>Regex</c>) so this class stays dependency-free per its class remarks.
    /// </summary>
    public const string HexColorPattern = "^#[0-9A-Fa-f]{6}$";
}
