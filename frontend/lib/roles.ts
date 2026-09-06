/**
 * Role constants and permission sets.
 * Source of truth: v1-spec.md section 3.2.
 */

export const AppRoles = {
  Provider:      "provider",
  ProviderAdmin: "provider_admin",
  ProviderAgent: "provider_agent",
  EnterpriseAdmin: "enterprise_admin",
  NetworkManager:  "network_manager",
  StoreManager:    "store_manager",
  Merchandiser:    "merchandiser",
  Storekeeper:     "storekeeper",
  Cashier:         "cashier",
  /** v4.1 (ADR-016): admin of a supplier tenant — supplier cabinet only. */
  SupplierAdmin:   "supplier_admin",
  /**
   * TASK-405 (Loyalty Фаза 0): consumer loyalty-wallet end user (ConsumerAccount, never a
   * staff User row). Deliberately NOT added to any Set below (TENANT_ROLES, CAN_*, etc.) —
   * those enumerate tenant-staff roles and a consumer session has no tenant_id at all.
   */
  Consumer:        "consumer",
} as const;

export type AppRole = (typeof AppRoles)[keyof typeof AppRoles];

// ── Permission sets (mirror backend AppPolicies) ──────────────────────────────

/**
 * All tenant staff roles (excludes provider — provider has no tenant context
 * and must not see tenant-scoped pages).
 */
export const CAN_VIEW_STOCK = new Set<AppRole>([
  AppRoles.EnterpriseAdmin,
  AppRoles.NetworkManager,
  AppRoles.StoreManager,
  AppRoles.Merchandiser,
  AppRoles.Storekeeper,
]);

/** Can access POS / cash register — cashier + storekeeper + store_manager + enterprise_admin */
export const CAN_ACCESS_POS = new Set<AppRole>([
  AppRoles.Cashier,
  AppRoles.Storekeeper,
  AppRoles.StoreManager,
  AppRoles.EnterpriseAdmin,
]);

/** Can view warehouse pages — storekeeper + merchandiser + store_manager + enterprise_admin */
export const CAN_VIEW_WAREHOUSE = new Set<AppRole>([
  AppRoles.Storekeeper,
  AppRoles.Merchandiser,
  AppRoles.StoreManager,
  AppRoles.EnterpriseAdmin,
]);

/** Can manage warehouse (receipts, transfers) — excludes merchandiser */
export const CAN_MANAGE_WAREHOUSE = new Set<AppRole>([
  AppRoles.Storekeeper,
  AppRoles.StoreManager,
  AppRoles.EnterpriseAdmin,
]);

/** Network manager and above — network_manager + enterprise_admin */
export const AT_LEAST_NETWORK_MANAGER = new Set<AppRole>([
  AppRoles.NetworkManager,
  AppRoles.EnterpriseAdmin,
]);

/** Can receive stock / create transfers — excludes merchandiser */
export const CAN_RECEIVE_STOCK = new Set<AppRole>([
  AppRoles.EnterpriseAdmin,
  AppRoles.NetworkManager,
  AppRoles.StoreManager,
  AppRoles.Storekeeper,
]);

/** Can view analytics — managers and above */
export const CAN_VIEW_ANALYTICS = new Set<AppRole>([
  AppRoles.EnterpriseAdmin,
  AppRoles.NetworkManager,
  AppRoles.StoreManager,
]);

/** Can manage users, confirm write-offs, etc. — store_manager and above */
export const AT_LEAST_STORE_MANAGER = new Set<AppRole>([
  AppRoles.EnterpriseAdmin,
  AppRoles.NetworkManager,
  AppRoles.StoreManager,
]);

/** Provider-only — super admin access */
export const PROVIDER_ONLY = new Set<AppRole>([AppRoles.Provider]);

/** All provider team roles (provider + provider_admin + provider_agent) */
export const PROVIDER_TEAM = new Set<AppRole>([
  AppRoles.Provider,
  AppRoles.ProviderAdmin,
  AppRoles.ProviderAgent,
]);

/**
 * Enterprise admin only — used for the Settings "Модулі" tab (GET /api/settings/modules
 * is tenant-scoped and 403s for provider, which has no tenant_id outside impersonation).
 */
export const ENTERPRISE_ADMIN_ONLY = new Set<AppRole>([AppRoles.EnterpriseAdmin]);

/**
 * Provider + enterprise_admin — mirrors backend's AppPolicies.AtLeastEnterpriseAdminRoles.
 * Used for Legal Entities management visibility (TASK-323): these roles can always
 * manage legal entities; other tenant roles need the `legal_entities.manage` permission
 * override (see `canManageLegalEntities`).
 */
export const AT_LEAST_ENTERPRISE_ADMIN = new Set<AppRole>([
  AppRoles.Provider,
  AppRoles.EnterpriseAdmin,
]);

/**
 * True when the current user can manage (create/update/deactivate) legal entities —
 * mirrors backend LegalEntityAuthorization.CanManage: enterprise_admin/provider always,
 * or any role with the `legal_entities.manage` permission override set to true.
 */
export function canManageLegalEntities(
  role: string | undefined,
  permissions?: Record<string, boolean> | null,
): boolean {
  if (hasRole(role, AT_LEAST_ENTERPRISE_ADMIN)) return true;
  return permissions?.["legal_entities.manage"] === true;
}

/**
 * True when the current user can export marketing-analytics (RFM) audiences with
 * unmasked PII (full phone numbers) — mirrors backend MarketingAnalyticsAuthorization
 * .CanExportPii: store_manager+ always, or any role with the
 * `marketing_analytics.export_pii` TenantRole capability override (ADR-020). Same
 * role-OR-permission shape as canManageLegalEntities above. Per task log 406, a caller who
 * lacks this just silently gets a masked export (never a 403) — so the UI uses this to
 * hide/disable the "show full phone" checkbox rather than let it quietly do nothing.
 */
export function canExportMarketingAnalyticsPii(
  role: string | undefined,
  permissions?: Record<string, boolean> | null,
): boolean {
  if (hasRole(role, AT_LEAST_STORE_MANAGER)) return true;
  return permissions?.["marketing_analytics.export_pii"] === true;
}

/**
 * True when the current user can see margin/profitability figures on the analytics pages
 * (`/analytics` category breakdown, `/analytics/pos` product trend) — mirrors backend
 * `AnalyticsAuthorization.CanViewMargin`: network_manager+ always, or any role with the
 * `analytics.view_margin` TenantRole capability override (ADR-020/ADR-027). One tier above
 * canExportMarketingAnalyticsPii's store_manager+ floor — margin figures are derived from
 * supplier purchase prices (`Item.PricePurchase`), a narrower/higher-risk sensitivity than RFM
 * PII. Per ADR-027 the server nulls `marginAmount`/`marginPercent` for a caller who lacks this
 * rather than 403ing the whole response — the UI uses this to keep the margin columns/panels
 * entirely out of the DOM, not just hide/gray them.
 */
export function canViewAnalyticsMargin(
  role: string | undefined,
  permissions?: Record<string, boolean> | null,
): boolean {
  if (hasRole(role, AT_LEAST_NETWORK_MANAGER)) return true;
  return permissions?.["analytics.view_margin"] === true;
}

/**
 * True when the current user can open the Settings → "Інтеграції" tab — mirrors backend
 * `AppPolicies.IntegrationsViewOrCapability` (ADR-020): store_manager and above always, or
 * any role holding the `integrations.view` TenantRole capability. Unlike the margin/PII
 * helpers above (which read `me.permissions`), capability keys live only in the JWT
 * `capabilities` claim / `me.capabilities`, so that's what this checks. The backend still
 * 403s a caller who lacks both — this just keeps the tab (with its ПРРО/API-key config)
 * out of the DOM for lower roles instead of showing a tab that errors.
 */
export function canViewIntegrations(
  role: string | undefined,
  capabilities?: string[] | null,
): boolean {
  if (hasRole(role, AT_LEAST_STORE_MANAGER)) return true;
  return capabilities?.includes("integrations.view") === true;
}

/**
 * Supplier cabinet only (v4.1, ADR-016). supplier_admin is deliberately NOT part
 * of any tenant-staff set (stock/pos/warehouse/…) — the backend returns 403 on
 * all tenant-staff endpoints for this role. Mirror that here: the only pages a
 * supplier_admin sees are /supplier/* and Settings.
 */
export const SUPPLIER_ONLY = new Set<AppRole>([AppRoles.SupplierAdmin]);

/**
 * All tenant roles (all except provider).
 * Use this for pages that make tenant-scoped API calls — provider has no
 * tenant_id and will get 403/empty from every tenant endpoint.
 * NOTE: supplier_admin is intentionally excluded — it has a tenant_id but only
 * the supplier cabinet, none of the tenant-staff pages.
 */
export const TENANT_ROLES = new Set<AppRole>([
  AppRoles.EnterpriseAdmin,
  AppRoles.NetworkManager,
  AppRoles.StoreManager,
  AppRoles.Merchandiser,
  AppRoles.Storekeeper,
  AppRoles.Cashier,
]);

// ── Helper ────────────────────────────────────────────────────────────────────

export function hasRole(userRole: string | undefined, allowed: Set<AppRole>): boolean {
  if (!userRole) return false;
  return allowed.has(userRole as AppRole);
}
