/**
 * Role constants and permission sets.
 * Source of truth: v1-spec.md section 3.2.
 */

export const AppRoles = {
  Provider: "provider",
  EnterpriseAdmin: "enterprise_admin",
  NetworkManager: "network_manager",
  StoreManager: "store_manager",
  Merchandiser: "merchandiser",
  Storekeeper: "storekeeper",
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
]);

// ── Helper ────────────────────────────────────────────────────────────────────

export function hasRole(userRole: string | undefined, allowed: Set<AppRole>): boolean {
  if (!userRole) return false;
  return allowed.has(userRole as AppRole);
}
