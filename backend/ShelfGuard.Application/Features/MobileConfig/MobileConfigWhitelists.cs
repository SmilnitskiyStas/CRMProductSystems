namespace ShelfGuard.Application.Features.MobileConfig;

/// <summary>
/// The single source of truth for every whitelist <see cref="MobileConfigValidator"/> enforces
/// against <c>MobileConfigurationVersion.ConfigurationJson</c> (TASK-532). Declarative JSON only,
/// never executable code — every value a tenant admin can put into the document must resolve to
/// one of these known keys, so the mobile renderer's Component/Navigation/Feature registries can
/// never be asked to render something they don't recognize.
///
/// Kept deliberately dependency-free (no JSON types) so TASK-533's
/// <c>/contracts/mobile-config.schema.json</c> and its "schema matches validator" test can both
/// read these same constants without pulling in validation logic.
/// </summary>
public static class MobileConfigWhitelists
{
    /// <summary>Only schema version that exists today. See <see cref="SupportedSchemaVersions"/>.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Every <c>schemaVersion</c> value <see cref="MobileConfigValidator"/> accepts. A set (not
    /// just <see cref="CurrentSchemaVersion"/>) so a future schema bump can add a value here
    /// without the validator needing a structural change.
    /// </summary>
    public static readonly IReadOnlySet<int> SupportedSchemaVersions = new HashSet<int> { CurrentSchemaVersion };

    /// <summary>
    /// Allowed keys of the <c>features</c> object (MASTER SPEC §9). Values must be boolean.
    /// </summary>
    public static readonly IReadOnlySet<string> FeatureKeys = new HashSet<string>
    {
        "loyalty", "promotions", "catalog", "coupons", "news", "receipts", "delivery", "personalOffers",
    };

    /// <summary>
    /// Allowed <c>navigation[].type</c> values (CLAUDE CODE SPEC ЕТАП 9 / MASTER SPEC §8 example).
    /// <c>label</c> stays a free string; <c>icon</c> is whitelisted separately by
    /// <see cref="NavigationIcons"/> (TASK-542, Navigation Builder icon picker).
    /// </summary>
    public static readonly IReadOnlySet<string> NavigationTypes = new HashSet<string>
    {
        "home", "promotions", "catalog", "loyalty", "coupons", "stores", "news", "profile",
    };

    /// <summary>
    /// Allowed <c>navigation[].icon</c> values (TASK-542, Navigation Builder). Deliberately matches
    /// the already-shipped mobile-side icon set exactly —
    /// <c>mobile/features/mobile-config/validation.ts</c>'s AJV
    /// <c>icon: { enum: ['home', 'tag', 'grid', 'qr', 'ticket', 'map', 'news', 'user'] }</c> for
    /// navigation items — rather than inventing a different set the mobile renderer's icon registry
    /// doesn't know about and would fail to render.
    /// </summary>
    public static readonly IReadOnlySet<string> NavigationIcons = new HashSet<string>
    {
        "home", "tag", "grid", "qr", "ticket", "map", "news", "user",
    };

    /// <summary>MASTER SPEC §8: "мінімум 2 пункти; максимум 5 основних пунктів."</summary>
    public const int NavigationMinItems = 2;

    /// <summary>MASTER SPEC §8: "мінімум 2 пункти; максимум 5 основних пунктів."</summary>
    public const int NavigationMaxItems = 5;

    /// <summary>
    /// Allowed <c>pages</c> keys — the initial configurable-pages list (CLAUDE CODE SPEC ЕТАП 8).
    /// System-controlled pages (Profile, Authentication, Security) are deliberately absent: they
    /// are never retailer-configurable, so they can never appear in this document at all.
    /// </summary>
    public static readonly IReadOnlySet<string> PageNames = new HashSet<string>
    {
        "home", "promotions", "catalog", "news",
    };

    /// <summary>
    /// Allowed <c>pages.*.blocks[].type</c> values — CODEX SPEC ЕТАП 6 "Core Blocks V1", the
    /// actual first-implementation set the mobile renderer ships with. Deliberately narrower than
    /// MASTER SPEC §6's full future block list (e.g. no <c>couponCarousel</c>/<c>textBlock</c>
    /// yet) — widen this set only in lockstep with the mobile Component Registry actually gaining
    /// a renderer for the new type.
    /// </summary>
    public static readonly IReadOnlySet<string> BlockTypes = new HashSet<string>
    {
        "heroBanner", "bannerCarousel",
        "loyaltyCard", "loyaltyBalance",
        "promotionCarousel", "promotionGrid",
        "productCarousel", "productGrid",
        "sectionHeader", "quickActions",
        "newsList", "storeList",
    };
}
