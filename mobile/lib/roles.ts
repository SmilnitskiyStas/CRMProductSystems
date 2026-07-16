/**
 * Canonical role string constants.
 * Source of truth: backend `UserService.ValidRoles` / `AppPolicies.cs`, mirrored on
 * web at `frontend/lib/roles.ts`. These are the exact lowercase snake_case strings
 * issued in the JWT `role` claim and returned as `AuthUserDto.Role` — NOT PascalCase.
 *
 * Found during Block 14 mobile audit (2026-07-15): every role-gate in the mobile app
 * previously used invented PascalCase names ('StoreManager', 'Director', 'Admin', ...)
 * that never match a real backend role, so every `.includes(user.role)` check below
 * always evaluated to false. Centralizing here so the bug class can't reappear
 * per-screen.
 */
export const AppRoles = {
  Provider: 'provider',
  ProviderAdmin: 'provider_admin',
  ProviderAgent: 'provider_agent',
  EnterpriseAdmin: 'enterprise_admin',
  NetworkManager: 'network_manager',
  StoreManager: 'store_manager',
  Merchandiser: 'merchandiser',
  Storekeeper: 'storekeeper',
  Cashier: 'cashier',
  SupplierAdmin: 'supplier_admin',
} as const;

export type AppRole = (typeof AppRoles)[keyof typeof AppRoles];

/** Can access POS / cash register — mirrors frontend/lib/roles.ts CAN_ACCESS_POS. */
export const CAN_ACCESS_POS: readonly string[] = [
  AppRoles.Cashier,
  AppRoles.Storekeeper,
  AppRoles.StoreManager,
  AppRoles.EnterpriseAdmin,
];

/** store_manager and above — mirrors frontend/lib/roles.ts AT_LEAST_STORE_MANAGER. */
export const AT_LEAST_STORE_MANAGER: readonly string[] = [
  AppRoles.StoreManager,
  AppRoles.NetworkManager,
  AppRoles.EnterpriseAdmin,
];

/** store_manager and above, plus provider/provider_admin (support & impersonation views). */
export const AT_LEAST_STORE_MANAGER_OR_PROVIDER: readonly string[] = [
  ...AT_LEAST_STORE_MANAGER,
  AppRoles.Provider,
  AppRoles.ProviderAdmin,
];

export function hasRole(role: string | undefined | null, allowed: readonly string[]): boolean {
  if (!role) return false;
  return allowed.includes(role);
}
