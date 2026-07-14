namespace ShelfGuard.Domain.Constants;

/// <summary>
/// Definition of a single capability key for the "assign role templates" UI (TASK-346).
/// </summary>
public sealed record TenantRoleCapabilityDefinition(string Key, string LabelUa);

/// <summary>
/// A specialty group of capabilities (e.g. "HR", "Бухгалтер / Фінансист", "Закупка").
/// </summary>
public sealed record TenantRoleCapabilityGroup(string Specialty, IReadOnlyList<TenantRoleCapabilityDefinition> Capabilities);

/// <summary>
/// Granular capability keys grantable via a <see cref="Entities.TenantRole"/> template
/// (ADR-020, TASK-345/346). Format "module.action", same shape as
/// <see cref="TenantUserPermissions"/> but resolved through
/// <see cref="Entities.User.TenantRoleId"/> → <see cref="Entities.TenantRole.Capabilities"/>
/// instead of a per-user override dict, and carried in the JWT "capabilities" claim
/// (comma-joined — see <c>JwtService.GenerateAccessToken</c>, same shape as the existing
/// "permissions" claim).
///
/// Checked by <c>RoleOrCapabilityRequirement</c> / <c>RoleOrCapabilityHandler</c>
/// (backend/ShelfGuard.Infrastructure/Authorization) as an OR alongside each action's
/// existing role array — a capability never replaces or loosens the existing role gate for
/// roles that don't hold it, it only adds an alternate path in.
///
/// <c>legal_entities.manage</c> deliberately has NO constant here — per ADR-020 point 3 it
/// reuses <see cref="TenantUserPermissions.LegalEntitiesManage"/> as-is (same string, same
/// grant mechanics), so there is exactly one definition for that key, not two. It IS included
/// in <see cref="All"/> and <see cref="Groups"/> so template validation/UI still surface it.
/// </summary>
public static class TenantRoleCapabilities
{
    // ── HR ────────────────────────────────────────────────────────────────

    /// <summary>Invite/update/deactivate users on UsersController. Deliberately excludes
    /// permission overrides, temporary access grants, and tenant-role assignment — those stay
    /// AtLeastEnterpriseAdmin-only with no capability bypass (anti-escalation, ADR-020 point 3/6).</summary>
    public const string UsersManage = "users.manage";

    /// <summary>Create/update/delete schedules and shifts on SchedulesController.</summary>
    public const string SchedulesManage = "schedules.manage";

    // ── Бухгалтер / Фінансист ────────────────────────────────────────────

    /// <summary>Read-only access to every GET on AnalyticsController (the whole controller is read-only).</summary>
    public const string AnalyticsView = "analytics.view";

    /// <summary>Read integration configs (GetAll/GetByService — secrets stay masked, same as today).</summary>
    public const string IntegrationsView = "integrations.view";

    /// <summary>Create/update/delete integration configs (Upsert/Delete).</summary>
    public const string IntegrationsManage = "integrations.manage";

    // ── Закупка (Purchasing) ─────────────────────────────────────────────

    /// <summary>Run the order-quantity calculation (OrdersController.Calculate).</summary>
    public const string OrdersManage = "orders.manage";

    /// <summary>Read suppliers (GetAll/GetById).</summary>
    public const string SuppliersView = "suppliers.view";

    /// <summary>Create/update/delete suppliers.</summary>
    public const string SuppliersManage = "suppliers.manage";

    /// <summary>Read receipts (GetAll/GetById). Create/UpdateItems/Receive/Cancel stay
    /// role-gated only — write-heavy stock path, deliberately excluded (ADR-020 point 3).</summary>
    public const string ReceiptsView = "receipts.view";

    /// <summary>Read AI order suggestions (GetList/GetById).</summary>
    public const string AiOrdersView = "ai_orders.view";

    /// <summary>Generate/update/accept/reject AI order suggestions.</summary>
    public const string AiOrdersManage = "ai_orders.manage";

    /// <summary>
    /// Every assignable capability key, including the reused
    /// <see cref="TenantUserPermissions.LegalEntitiesManage"/> — the single validation source
    /// for TenantRolesController create/update. No string outside this set may enter
    /// <see cref="Entities.TenantRole.Capabilities"/>.
    /// </summary>
    public static readonly HashSet<string> All =
    [
        UsersManage, SchedulesManage,
        AnalyticsView, IntegrationsView, IntegrationsManage,
        TenantUserPermissions.LegalEntitiesManage,
        OrdersManage, SuppliersView, SuppliersManage, ReceiptsView,
        AiOrdersView, AiOrdersManage,
    ];

    /// <summary>
    /// Capability catalog grouped by specialty + a short Ukrainian label per key — backend is
    /// the source of truth for grouping (ADR-017 §4 pattern), exposed via
    /// GET /api/tenant-roles/capabilities so the frontend never hardcodes the grouping.
    /// </summary>
    public static readonly IReadOnlyList<TenantRoleCapabilityGroup> Groups =
    [
        new("HR",
        [
            new(UsersManage, "Керування користувачами (запросити / редагувати / деактивувати)"),
            new(SchedulesManage, "Керування графіками змін"),
        ]),
        new("Бухгалтер / Фінансист",
        [
            new(AnalyticsView, "Перегляд аналітики"),
            new(IntegrationsView, "Перегляд інтеграцій"),
            new(IntegrationsManage, "Керування інтеграціями"),
            new(TenantUserPermissions.LegalEntitiesManage, "Керування юридичними особами"),
        ]),
        new("Закупка",
        [
            new(OrdersManage, "Розрахунок замовлень"),
            new(SuppliersView, "Перегляд постачальників"),
            new(SuppliersManage, "Керування постачальниками"),
            new(ReceiptsView, "Перегляд поставок"),
            new(AiOrdersView, "Перегляд AI-замовлень"),
            new(AiOrdersManage, "Керування AI-замовленнями"),
        ]),
    ];
}
