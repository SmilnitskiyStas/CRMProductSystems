namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Definition of a single sidebar-tab key for the "per-role tab visibility" feature (TASK-391).
/// </summary>
public sealed record TenantRoleTabDefinition(string Key, string LabelUa);

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
/// - AllowedTabs gate sidebar/route VISIBILITY — a coarser, page-level axis with its own valid-value
///   set (this file) and its own consumer (Sidebar, route guard — a future JWT "tabs" claim is a
///   separate follow-up task, NOT wired by this file).
///
/// Mixing the two into one list/claim would let tab keys leak into an authorization check that
/// was never meant to understand them, and would let capability keys leak into a sidebar
/// visibility check that was never meant to understand THEM either. Keeping them on two columns
/// with two catalogs keeps each consumer's valid-value set exactly as wide as it needs to be.
///
/// Keys correspond 1:1 to <c>NavGroup.key</c> in frontend/components/layout/Sidebar.tsx
/// (buildNavGroups) plus the standalone "dashboard" NavItem (href "/dashboard" — implemented as
/// a top-level item rather than a NavGroup, but just as real and just as worth hiding/showing as
/// any group). <see cref="Catalog"/> labels are copied verbatim from the "Dashboard.sidebar.*" /
/// "Dashboard.sidebar.groups.*.label" keys in frontend/messages/uk.json, so this file — not a
/// second, possibly-drifting translation — is the source of truth wording.
///
/// Deliberately EXCLUDED from <see cref="All"/>, forever:
///  - "admin"            — a real NavGroup key in Sidebar.tsx, but provider-only (its items are
///                          role-gated to PROVIDER_TEAM/PROVIDER_ONLY there). TenantRole is a
///                          tenant-scoped concept; a tenant role must never be able to unlock the
///                          provider panel.
///  - "supplier_cabinet" — a real NavGroup key (from buildSupplierNavGroup), but supplier_admin-only
///                          and governed by the separate SupplierRole/SupplierRolePermissions
///                          mechanism, not TenantRole.
///  - "settings"         — a standalone NavItem, always visible to everyone. It holds the user's
///                          own account/notification/locale preferences, not a business module —
///                          nothing there is meant to be hidden per role.
/// </summary>
public static class TenantRoleTabs
{
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

    /// <summary>
    /// Every assignable tab key. No string outside this set may enter
    /// <see cref="Entities.TenantRole.AllowedTabs"/> — enforced by TenantRoleService.Validate.
    /// </summary>
    public static readonly HashSet<string> All =
    [
        Dashboard, Operations, Sales, Procurement, Marketplace,
        AutoService, Production, Analytics, Workforce, Support,
    ];

    /// <summary>
    /// Full catalog with Ukrainian labels, ordered to match the sidebar's visual top-to-bottom
    /// order. Source of truth for a future GET /api/tenant-roles/tabs endpoint — deliberately NOT
    /// added by this task (TASK-391 schema stage); left for the follow-up backend-developer step.
    /// </summary>
    public static readonly IReadOnlyList<TenantRoleTabDefinition> Catalog =
    [
        new(Dashboard, "Дашборд"),
        new(Operations, "Операції"),
        new(Sales, "Продажі"),
        new(Procurement, "Постачання"),
        new(Marketplace, "Маркетплейс"),
        new(AutoService, "Auto Service"),
        new(Production, "Виробництво"),
        new(Analytics, "Аналітика"),
        new(Workforce, "Персонал"),
        new(Support, "Підтримка"),
    ];
}
