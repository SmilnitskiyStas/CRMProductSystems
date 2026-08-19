namespace ShelfGuard.Application.Features.MobileConfig.BlockRegistry;

/// <summary>
/// Static, compile-time catalog of every block type the consumer app-builder platform supports
/// (TASK-538, CLAUDE CODE SPEC §12). Block *types* are not tenant-editable data — no retailer ever
/// creates a new block type, only arranges instances of these fixed types on their pages
/// (TASK-539/541) — so this is deliberately in-code static metadata, not a DB table/migration.
/// <see cref="Definitions"/>'s <see cref="BlockDefinition.Type"/> set is kept in exact lockstep with
/// <see cref="MobileConfigWhitelists.BlockTypes"/> (TASK-532's flat allowlist that
/// <c>MobileConfigValidator</c> already enforces on <c>pages.*.blocks[].type</c>) — see
/// <c>BlockRegistryTests.Registry_type_set_matches_MobileConfigWhitelists_BlockTypes_exactly</c>,
/// the same "agreement test" pattern <c>MobileConfigSchemaContractTests</c> already uses to guard
/// TASK-533's contract against drift from TASK-532's whitelist.
///
/// <b>Props-validation scope decision (TASK-538):</b> this registry's <see cref="BlockPropDefinition"/>
/// entries are exposed to the web admin (<c>GET /api/v1/mobile/blocks</c>) for TASK-539/540 to build
/// the App Builder canvas and Property Editor against, but they are deliberately NOT wired into
/// <c>MobileConfigValidator</c>'s save-time enforcement in this task. Reasons, recorded here rather
/// than silently left unaddressed:
/// <list type="number">
/// <item>TASK-532's already-shipped <c>MobileConfigValidatorTests</c> (23 passing tests) encode an
/// explicit, tested contract that a block's <c>props</c> is free-form JSON at this stage — e.g.
/// <c>Validate_accepts_a_well_formed_document</c> uses <c>"props": {}</c> for a <c>heroBanner</c>
/// block and asserts the whole document is valid; <c>Validate_accepts_multiple_whitelisted_block_
/// types_on_multiple_pages</c> uses an arbitrary <c>"showQr"</c> prop key on <c>loyaltyBalance</c>
/// that this registry's own (first-ever, independently-authored) <c>loyaltyBalance</c> prop schema
/// does not contain. Retrofitting strict presence/unknown-key rejection now would either break that
/// already-shipped, already-tested behavior, or force rewriting TASK-532's tests around prop names
/// I invented for this task with no real producer (App Builder UI) to confirm them against yet —
/// the same class of "don't guess past a real mismatch" risk TASK-536 hit and resolved by checking
/// the actual mobile client instead of inventing a value.
/// </item>
/// <item>TASK-539 (App Builder canvas) and TASK-540 (Property Editor) — the actual UIs that will
/// ever *produce* a block's <c>props</c> — do not exist yet. Locking save-time enforcement to this
/// registry's shapes before those exist risks the registry diverging from what they actually need,
/// forcing a second breaking change once real usage exists.
/// </item>
/// </list>
/// This is the "much larger validation engine vs. keep scoped" fork flagged as acceptable to defer:
/// the per-field checks themselves are simple, but retrofitting them safely onto an already-shipped,
/// tested, intentionally-free-form field is not a small change. Flagged explicitly as follow-up work
/// (see task log 538) rather than left unaddressed with no note.
/// </summary>
public static class BlockRegistry
{
    /// <summary>The 12 Core Blocks V1 types (CODEX SPEC ЕТАП 6), matching <see cref="MobileConfigWhitelists.BlockTypes"/> exactly.</summary>
    public static readonly IReadOnlyList<BlockDefinition> Definitions = new List<BlockDefinition>
    {
        new(
            Type: "heroBanner",
            DisplayName: "Hero Banner",
            Icon: "image",
            Category: BlockCategories.Banner,
            Props: new List<BlockPropDefinition>
            {
                new("title", BlockPropTypes.String, Required: false, Default: "", MaxLength: 80),
                new("subtitle", BlockPropTypes.String, Required: false, Default: "", MaxLength: 140),
                new("imageUrl", BlockPropTypes.Url, Required: true, Default: "", MaxLength: MobileThemeWhitelists.MaxLogoUrlLength),
                new("ctaLabel", BlockPropTypes.String, Required: false, Default: "", MaxLength: 30),
                new("ctaLink", BlockPropTypes.Url, Required: false, Default: "", MaxLength: MobileThemeWhitelists.MaxLogoUrlLength),
                new("heightPx", BlockPropTypes.Int, Required: false, Default: 190, Min: 120, Max: 260),
            },
            SupportedDataSource: "none — static hero content (title/subtitle/imageUrl/CTA) authored " +
                "directly via props; no backend read."),

        new(
            Type: "bannerCarousel",
            DisplayName: "Banner Carousel",
            Icon: "images",
            Category: BlockCategories.Banner,
            Props: new List<BlockPropDefinition>
            {
                new("limit", BlockPropTypes.Int, Required: false, Default: 5, Min: 1, Max: 10),
                new("autoPlay", BlockPropTypes.Bool, Required: false, Default: true),
                new("cardWidthPx", BlockPropTypes.Int, Required: false, Default: 280, Min: 200, Max: 360),
            },
            SupportedDataSource: "banners — the tenant's active Banner list, ordered for display " +
                "(ConsumerContentController GET /api/consumer/{tenantId}/banners)."),

        new(
            Type: "loyaltyCard",
            DisplayName: "Loyalty Card",
            Icon: "credit-card",
            Category: BlockCategories.Loyalty,
            Props: new List<BlockPropDefinition>
            {
                new("showQrCode", BlockPropTypes.Bool, Required: false, Default: true),
                new("showTier", BlockPropTypes.Bool, Required: false, Default: true),
            },
            SupportedDataSource: "loyalty — the consumer's LoyaltyMembership and wallet code for " +
                "this tenant (ConsumerLoyaltyController GET /api/consumer/loyalty/code)."),

        new(
            Type: "loyaltyBalance",
            DisplayName: "Loyalty Balance",
            Icon: "wallet",
            Category: BlockCategories.Loyalty,
            Props: new List<BlockPropDefinition>
            {
                new("showPointsLabel", BlockPropTypes.Bool, Required: false, Default: true),
                new("ctaLabel", BlockPropTypes.String, Required: false, Default: "Переглянути", MaxLength: 30),
            },
            SupportedDataSource: "loyalty — the consumer's point balance and ledger history for " +
                "this tenant (ConsumerLoyaltyController GET /api/consumer/loyalty/{tenantId}/history)."),

        new(
            Type: "promotionCarousel",
            DisplayName: "Promotion Carousel",
            Icon: "percent",
            Category: BlockCategories.Promotions,
            Props: new List<BlockPropDefinition>
            {
                new("title", BlockPropTypes.String, Required: false, Default: "Акції", MaxLength: 60),
                new("limit", BlockPropTypes.Int, Required: false, Default: 10, Min: 1, Max: 20),
                new("showViewAll", BlockPropTypes.Bool, Required: false, Default: true),
                new("cardStyle", BlockPropTypes.Enum, Required: false, Default: "compact",
                    AllowedValues: new List<string> { "compact", "expanded" }),
                new("cardWidthPx", BlockPropTypes.Int, Required: false, Default: 210, Min: 150, Max: 270),
            },
            SupportedDataSource: "promotions — active discounted products for the consumer's store " +
                "(ConsumerContentController GET /api/consumer/{tenantId}/promotions)."),

        new(
            Type: "promotionGrid",
            DisplayName: "Promotion Grid",
            Icon: "layout-grid",
            Category: BlockCategories.Promotions,
            Props: new List<BlockPropDefinition>
            {
                new("title", BlockPropTypes.String, Required: false, Default: "Акції", MaxLength: 60),
                new("limit", BlockPropTypes.Int, Required: false, Default: 12, Min: 1, Max: 30),
                new("columns", BlockPropTypes.Int, Required: false, Default: 2, Min: 2, Max: 4),
            },
            SupportedDataSource: "promotions — active discounted products for the consumer's store " +
                "(ConsumerContentController GET /api/consumer/{tenantId}/promotions)."),

        new(
            Type: "productCarousel",
            DisplayName: "Product Carousel",
            Icon: "shopping-bag",
            Category: BlockCategories.Products,
            Props: new List<BlockPropDefinition>
            {
                new("title", BlockPropTypes.String, Required: false, Default: "Товари", MaxLength: 60),
                new("limit", BlockPropTypes.Int, Required: false, Default: 10, Min: 1, Max: 20),
                new("showViewAll", BlockPropTypes.Bool, Required: false, Default: true),
                new("cardWidthPx", BlockPropTypes.Int, Required: false, Default: 170, Min: 120, Max: 220),
            },
            SupportedDataSource: "catalog — paginated active product catalog, optionally filtered " +
                "by category (ConsumerContentController GET /api/consumer/{tenantId}/catalog)."),

        new(
            Type: "productGrid",
            DisplayName: "Product Grid",
            Icon: "grid-3x3",
            Category: BlockCategories.Products,
            Props: new List<BlockPropDefinition>
            {
                new("title", BlockPropTypes.String, Required: false, Default: "Товари", MaxLength: 60),
                new("limit", BlockPropTypes.Int, Required: false, Default: 12, Min: 1, Max: 30),
                new("columns", BlockPropTypes.Int, Required: false, Default: 2, Min: 2, Max: 4),
            },
            SupportedDataSource: "catalog — paginated active product catalog, optionally filtered " +
                "by category (ConsumerContentController GET /api/consumer/{tenantId}/catalog)."),

        new(
            Type: "sectionHeader",
            DisplayName: "Section Header",
            Icon: "heading",
            Category: BlockCategories.Layout,
            Props: new List<BlockPropDefinition>
            {
                new("title", BlockPropTypes.String, Required: true, Default: "", MaxLength: 60),
                new("subtitle", BlockPropTypes.String, Required: false, Default: "", MaxLength: 100),
                new("alignment", BlockPropTypes.Enum, Required: false, Default: "left",
                    AllowedValues: new List<string> { "left", "center" }),
            },
            SupportedDataSource: "none — static heading/subheading text authored directly via " +
                "props; no backend read."),

        new(
            Type: "quickActions",
            DisplayName: "Quick Actions",
            Icon: "zap",
            Category: BlockCategories.Layout,
            // Reuses MobileConfigWhitelists.NavigationTypes as the allowed shortcut targets rather
            // than inventing a second, parallel vocabulary — a quick action is just a shortcut to
            // one of the app's already-whitelisted navigation destinations.
            Props: new List<BlockPropDefinition>
            {
                new("actions", BlockPropTypes.StringArray, Required: false,
                    Default: new List<string> { "catalog", "loyalty", "promotions" },
                    AllowedValues: MobileConfigWhitelists.NavigationTypes.ToList(),
                    MinItems: 1, MaxItems: 6),
            },
            SupportedDataSource: "none — a static list of shortcut targets, each an existing " +
                "whitelisted navigation route (MobileConfigWhitelists.NavigationTypes); no backend read."),

        new(
            Type: "newsList",
            DisplayName: "News List",
            Icon: "newspaper",
            Category: BlockCategories.News,
            Props: new List<BlockPropDefinition>
            {
                new("title", BlockPropTypes.String, Required: false, Default: "Новини", MaxLength: 60),
                new("limit", BlockPropTypes.Int, Required: false, Default: 5, Min: 1, Max: 20),
            },
            SupportedDataSource: "news — NOT YET IMPLEMENTED backend-side: MobileConfigWhitelists." +
                "FeatureKeys/NavigationTypes reserve a \"news\" key, but no News domain entity or " +
                "endpoint exists in this repo today. Registered per CODEX SPEC's Core Blocks V1 set " +
                "regardless — flagged as a real, current gap, not invented."),

        new(
            Type: "storeList",
            DisplayName: "Store List",
            Icon: "map-pin",
            Category: BlockCategories.Stores,
            Props: new List<BlockPropDefinition>
            {
                new("title", BlockPropTypes.String, Required: false, Default: "Магазини", MaxLength: 60),
                new("limit", BlockPropTypes.Int, Required: false, Default: 10, Min: 1, Max: 30),
                new("showDistance", BlockPropTypes.Bool, Required: false, Default: true),
            },
            SupportedDataSource: "stores — the tenant's Location list within the consumer's joined " +
                "network. No dedicated consumer-facing store-list endpoint exists yet today " +
                "(ConsumerLoyaltyController's preferred-store selection references StoreId directly, " +
                "with no GET-list counterpart) — flagged as a gap for a future consumer endpoint."),
    };
}
