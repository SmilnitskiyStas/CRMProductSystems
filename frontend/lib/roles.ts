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
 * All tenant roles (all except provider).
 * Use this for pages that make tenant-scoped API calls — provider has no
 * tenant_id and will get 403/empty from every tenant endpoint.
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
