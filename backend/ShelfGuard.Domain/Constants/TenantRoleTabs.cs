namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Definition of a single sidebar-tab key for the "per-role tab visibility" feature (TASK-391).
/// Used both for the 10 legacy group-level keys and the TASK-398 item-level (href) keys — same
/// shape, "Key" is just a coarser or finer-grained string depending on which list it came from.
/// </summary>
public sealed record TenantRoleTabDefinition(string Key, string LabelUa);

/// <summary>
/// A section of the hierarchical tab catalog (TASK-398) — either a real sidebar <c>NavGroup</c>
/// (<see cref="GroupKey"/> set to one of the group constants below, e.g. "operations") or the
/// standalone Dashboard pseudo-section (<see cref="GroupKey"/> null — Dashboard is a single
/// top-level NavItem in Sidebar.tsx, not a NavGroup, so it has no group-level key to bulk-grant).
/// When <see cref="GroupKey"/> is non-null it is itself independently grantable — a coarse
/// "whole group" tab (the ONLY granularity that existed before TASK-398) — in addition to each
/// child in <see cref="Items"/> being independently grantable at the finer, per-page level.
/// </summary>
public sealed record TenantRoleTabGroupDefinition(
    string? GroupKey, string GroupLabelUa, IReadOnlyList<TenantRoleTabDefinition> Items);

/// <summary>
/// Sidebar-tab visibility keys grantable via a <see cref="Entities.TenantRole"/> template
/// (TASK-391, Stage 1/Feature 1 of the per-role UI-customization initiative — see also
/// <see cref="TenantRoleCapabilities"/> for the sibling "Stage 2" work this deliberately does
/// NOT touch). Stored on <see cref="Entities.TenantRole.AllowedTabs"/>, a SEPARATE column from
/// <see cref="Entities.TenantRole.Capabilities"/>:
///
/// - Capabilities gate backend ACTIONS. They validate against <see cref="TenantRoleCapabilities.All"/>,
///   are comma-joined into the JWT "capabilities" claim, and are checked by
///   RoleOrCapabilityHandler for per-endpoint authorization (backend/ShelfGuard.Infrastructure/Authorization).
/// - AllowedTabs gate sidebar/route VISIBILITY — a page-level axis with its own valid-value
///   set (this file) and its own consumer (Sidebar, route guard — a future JWT "tabs" claim is a
///   separate follow-up task, NOT wired by this file).
///
/// Mixing the two into one list/claim would let tab keys leak into an authorization check that
/// was never meant to understand them, and would let capability keys leak into a sidebar
/// visibility check that was never meant to understand THEM either. Keeping them on two columns
/// with two catalogs keeps each consumer's valid-value set exactly as wide as it needs to be.
///
/// TASK-398 ("per-page, not just per-group" product feedback): the original 10 keys below
/// (<see cref="Dashboard"/> + the 9 <c>NavGroup.key</c> values) only let a template grant/deny a
/// WHOLE nav group at once (e.g. "operations" = Inventory+Stock+Receipts+Transfers+WriteOffs+
/// Locations+IoT, all-or-nothing). They are kept exactly as-is — still valid, still in
/// <see cref="All"/> — for backward compat with every already-configured TenantRole row; nothing
/// about their meaning changes. Added alongside: one key PER sidebar page (the exact href string
/// from <c>NavItem.href</c> in frontend/components/layout/Sidebar.tsx's <c>buildNavGroups</c>,
/// e.g. "/inventory", "/receipts"), letting a template grant a single page inside a group without
/// unlocking the rest of it. Both key flavours validate through the same <see cref="All"/> set
/// (<c>TenantRoleService.Validate</c> needs no branching logic to accept either) and both are
/// exposed together by <see cref="Catalog"/>'s hierarchy — a group node's own key (coarse grant)
/// plus its nested item keys (fine grant) — so the frontend "Видимі розділи" editor (a future,
/// separate task — enforcement/UI wiring is explicitly NOT part of TASK-398) can offer both.
///
/// Keys correspond 1:1 to <c>NavGroup.key</c>/<c>NavItem.href</c> in
/// frontend/components/layout/Sidebar.tsx (buildNavGroups) plus the standalone "dashboard"
/// NavItem (href "/dashboard" — implemented as a top-level item rather than a NavGroup, but just
/// as real and just as worth hiding/showing as any group; keeps its original bare "dashboard" key
/// rather than switching to the href-shaped "/dashboard", since it already served BOTH the group-
/// level and item-level role before TASK-398 — a group of exactly one page needs no second key).
/// <see cref="Catalog"/> labels are copied verbatim from the "Dashboard.sidebar.*" /
/// "Dashboard.sidebar.groups.*.label"/"Dashboard.sidebar.groups.*.<item>" keys in
/// frontend/messages/uk.json, so this file — not a second, possibly-drifting translation — is the
/// source of truth wording.
///
/// Deliberately EXCLUDED from <see cref="All"/>, forever (group keys AND their would-be item
/// keys alike):
///  - "admin"            — a real NavGroup key in Sidebar.tsx, but provider-only (its items are
///                          role-gated to PROVIDER_TEAM/PROVIDER_ONLY there). TenantRole is a
///                          tenant-scoped concept; a tenant role must never be able to unlock the
///                          provider panel. Its items ("/provider", "/admin") are excluded too.
///  - "supplier_cabinet" — a real NavGroup key (from buildSupplierNavGroup), but supplier_admin-only
///                          and governed by the separate SupplierRole/SupplierRolePermissions
///                          mechanism, not TenantRole. Its "/supplier/*" items are excluded too.
///  - "settings"         — a standalone NavItem, always visible to everyone. It holds the user's
///                          own account/notification/locale preferences, not a business module —
///                          nothing there is meant to be hidden per role.
///
/// One item is INCLUDED for TASK-398 completeness (it is a real entry in the Workforce group's
/// `items` array in Sidebar.tsx) but is worth flagging for whoever builds the enforcement/UI
/// follow-up: <see cref="ItemLegalEntities"/> ("/settings/legal-entities"). Sidebar.tsx's own
/// TASK-397 comment deliberately keeps that ONE item's visibility decided solely by
/// `canManageLegalEntities` (role rank OR the `legal_entities.manage` capability override),
/// independent of `tabsSet` in both directions — granting/revoking it via AllowedTabs currently
/// has NO effect there, unlike its 3 Workforce siblings. That carve-out is a deliberate, narrow,
/// security-sensitive gate and must stay that way; it is a pre-existing frontend behavior this
/// backend-only task does not change, just something the future frontend item-level-enforcement
/// task must account for (most likely: keep excluding this one href from the generic tabsSet
/// check exactly as today, item-level or not).
/// </summary>
public static class TenantRoleTabs
{
    // ── Group-level keys (TASK-391) — coarse, whole-NavGroup grants. Kept for backward compat;
    // every already-configured TenantRole row keeps meaning exactly what it always meant. ──────
    public const string Dashboard = "dashboard";
    public const string Operations = "operations";
    public const string Sales = "sales";
    public const string Procurement = "procurement";
    public const string Marketplace = "marketplace";
    public const string AutoService = "auto_service";
    public const string Production = "production";
    public const string Analytics = "analytics";
    public const string Workforce = "workforce";
    public const string Support = "support";

    /// <summary>Every group-level key, i.e. every <see cref="Entities.TenantRole"/>-grantable key
    /// that existed before TASK-398. Deliberately excludes <see cref="Dashboard"/> — Dashboard
    /// has no group of its own to bulk-grant (see the "no second key" rationale above); it is
    /// listed on its own throughout this file instead of folded into this set.</summary>
    public static readonly HashSet<string> GroupKeys =
    [
        Operations, Sales, Procurement, Marketplace,
        AutoService, Production, Analytics, Workforce, Support,
    ];

    // ── Item-level keys (TASK-398) — one per Sidebar.tsx NavItem href, fine-grained grants.
    // Value is always the literal href string from buildNavGroups — verified against
    // frontend/components/layout/Sidebar.tsx directly, not copied from an older, possibly-drifted
    // task brief. Re-verify against that file if this list and Sidebar.tsx ever disagree. ──────

    // Operations (moduleKey "inventory")
    public const string ItemInventory = "/inventory";
    public const string ItemStock = "/stock";
    public const string ItemReceipts = "/receipts";
    public const string ItemTransfers = "/transfers";
    public const string ItemWriteOffs = "/write-offs";
    public const string ItemLocations = "/locations";
    public const string ItemIot = "/iot";

    // Sales (moduleKey "pos")
    public const string ItemPos = "/pos";
    public const string ItemSales = "/sales";
    public const string ItemCustomers = "/customers";
    public const string ItemEvents = "/events";

    // Procurement (moduleKey "procurement")
    public const string ItemOrders = "/orders";
    public const string ItemAiOrders = "/ai-orders";

    // Marketplace (moduleKey "marketplace")
    public const string ItemMarketplaceSuppliers = "/marketplace";
    public const string ItemMarketplaceOrders = "/marketplace/orders";

    // Auto Service (moduleKey "auto_service")
    public const string ItemAutoServiceWorkOrders = "/auto-service";
    public const string ItemAutoServiceCustomers = "/auto-service/customers";
    public const string ItemAutoServiceCatalog = "/auto-service/service-catalog";

    // Production (moduleKey "production")
    public const string ItemProductionRecipes = "/production/recipes";
    public const string ItemProductionOrders = "/production/orders";

    // Analytics (no moduleKey — always visible per role/permission)
    public const string ItemAnalytics = "/analytics";
    public const string ItemAnalyticsPos = "/analytics/pos";

    // Workforce (no moduleKey)
    public const string ItemUsers = "/users";
    public const string ItemSchedules = "/schedules";
    public const string ItemProviderTeam = "/provider/team";

    /// <summary>See the class-level doc comment's "flagged for the follow-up task" note — this
    /// item's frontend visibility is currently decided ONLY by `canManageLegalEntities`,
    /// independent of any tabsSet/AllowedTabs grant. Included here for catalog completeness
    /// (it is a real Workforce NavItem) even though granting it does nothing client-side yet.</summary>
    public const string ItemLegalEntities = "/settings/legal-entities";

    // Support (no moduleKey)
    public const string ItemServiceDesk = "/service-desk";

    /// <summary>Every item-level key. Deliberately excludes the "/provider", "/admin" (Admin
    /// group) and every "/supplier/*" (Supplier Cabinet group) href — see the class-level
    /// "Deliberately EXCLUDED" note; those groups' items must never become individually
    /// grantable to a tenant role either, same rationale as their group keys.</summary>
    public static readonly HashSet<string> ItemKeys =
    [
        ItemInventory, ItemStock, ItemReceipts, ItemTransfers, ItemWriteOffs, ItemLocations, ItemIot,
        ItemPos, ItemSales, ItemCustomers, ItemEvents,
        ItemOrders, ItemAiOrders,
        ItemMarketplaceSuppliers, ItemMarketplaceOrders,
        ItemAutoServiceWorkOrders, ItemAutoServiceCustomers, ItemAutoServiceCatalog,
        ItemProductionRecipes, ItemProductionOrders,
        ItemAnalytics, ItemAnalyticsPos,
        ItemUsers, ItemSchedules, ItemProviderTeam, ItemLegalEntities,
        ItemServiceDesk,
    ];

    /// <summary>
    /// Every assignable tab key — group-level (backward compat) AND item-level (TASK-398)
    /// alike. No string outside this set may enter <see cref="Entities.TenantRole.AllowedTabs"/>
    /// — enforced by <c>TenantRoleService.Validate</c>, which needs no special-casing between the
    /// two flavours since they simply share this one set.
    /// </summary>
    public static readonly HashSet<string> All = [Dashboard, .. GroupKeys, .. ItemKeys];

    /// <summary>
    /// Full catalog as a hierarchy — group sections (real <c>NavGroup</c>s, each carrying its
    /// own bulk-grantable <see cref="TenantRoleTabGroupDefinition.GroupKey"/> plus its nested
    /// per-page <see cref="TenantRoleTabGroupDefinition.Items"/>) and the standalone Dashboard
    /// section (<c>GroupKey</c> null). Ordered to match the sidebar's visual top-to-bottom order,
    /// same as groups' own item order in Sidebar.tsx. Source of truth for
    /// GET /api/tenant-roles/tabs (TASK-398 — supersedes TASK-391b's flat shape).
    /// </summary>
    public static readonly IReadOnlyList<TenantRoleTabGroupDefinition> Catalog =
    [
        new(null, "Дашборд",
        [
            new(Dashboard, "Дашборд"),
        ]),
        new(Operations, "Операції",
        [
            new(ItemInventory, "Каталог"),
            new(ItemStock, "Залишки"),
            new(ItemReceipts, "Прийомка"),
            new(ItemTransfers, "Переміщення"),
            new(ItemWriteOffs, "Списання"),
            new(ItemLocations, "Локації"),
            new(ItemIot, "IoT пристрої"),
        ]),
        new(Sales, "Продажі",
        [
            new(ItemPos, "Каса"),
            new(ItemSales, "Продажі"),
            new(ItemCustomers, "Клієнти"),
            new(ItemEvents, "Події"),
        ]),
        new(Procurement, "Постачання",
        [
            new(ItemOrders, "Замовлення постачання"),
            new(ItemAiOrders, "AI Постачання"),
        ]),
        new(Marketplace, "Маркетплейс",
        [
            new(ItemMarketplaceSuppliers, "Постачальники"),
            new(ItemMarketplaceOrders, "Мої замовлення"),
        ]),
        new(AutoService, "Auto Service",
        [
            new(ItemAutoServiceWorkOrders, "Наряди"),
            new(ItemAutoServiceCustomers, "Клієнти"),
            new(ItemAutoServiceCatalog, "Каталог послуг"),
        ]),
        new(Production, "Виробництво",
        [
            new(ItemProductionRecipes, "Рецепти"),
            new(ItemProductionOrders, "Ордери"),
        ]),
        new(Analytics, "Аналітика",
        [
            new(ItemAnalytics, "Аналітика"),
            new(ItemAnalyticsPos, "POS Аналітика"),
        ]),
        new(Workforce, "Персонал",
        [
            new(ItemUsers, "Персонал"),
            new(ItemSchedules, "Розклад"),
            new(ItemProviderTeam, "Команда"),
            new(ItemLegalEntities, "Юридичні особи"),
        ]),
        new(Support, "Підтримка",
        [
            new(ItemServiceDesk, "Service Desk"),
        ]),
    ];
}
