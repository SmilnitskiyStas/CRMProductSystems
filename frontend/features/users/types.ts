export interface UserDto {
  id: string;
  email: string;
  fullName: string;
  phone?: string | null;
  role: string;
  storeId?: string | null;
  isActive: boolean;
  hasTelegram: boolean;
  createdAt: string;
  lastActiveAt?: string | null;
  /** Per-user page-access overrides. null = all defaults apply. */
  permissions?: Record<string, boolean> | null;
  /** Display name of the user who invited/created this account. Null for seed users. */
  invitedByName?: string | null;
  /** Юридична особа, до якої прив'язаний користувач (nullable). */
  legalEntityId?: string | null;
  /** Assigned custom capability-template role (ADR-020). Null = no template beyond the base Role. */
  tenantRoleId?: string | null;
}

export interface UpdatePermissionsRequest {
  /** Map of page slug → access bool. Empty {} clears all overrides. */
  overrides: Record<string, boolean>;
}

export interface InviteUserRequest {
  email: string;
  fullName: string;
  role: string;
  password: string;
  storeId?: string | null;
  legalEntityId?: string | null;
}

export interface UpdateUserRequest {
  fullName: string;
  phone?: string | null;
  role: string;
  storeId?: string | null;
  legalEntityId?: string | null;
}

// ── Temporary permission grants (ADR-019, TASK-342/344) ───────────────────────

/** A temporary, self-expiring page-access override granted on top of the user's role/permissions. */
export interface PermissionGrantDto {
  id: string;
  userId: string;
  permissionKey: string;
  expiresAt: string;
  grantedByUserId: string;
  grantedByName?: string | null;
  grantedAt: string;
  revokedAt?: string | null;
}

export interface GrantTemporaryPermissionRequest {
  /** Page slug — same set as {@link PAGES}. */
  permissionKey: string;
  /** Must be in the future and no more than 90 days out (enforced server-side; mirrored client-side for fast feedback). */
  expiresAt: string;
}

/** ADR-019: temporary grants may not extend more than this far into the future. */
export const MAX_GRANT_DURATION_DAYS = 90;

export interface ActivityLogDto {
  id: string;
  action: string;
  entityType?: string | null;
  entityId?: string | null;
  meta?: string | null;
  ipAddress?: string | null;
  isImpersonated: boolean;
  createdAt: string;
}

/**
 * Activity-log action labels moved to i18n (`Dashboard.users.activityLog.actions.*`,
 * `useTranslations`) as of i18n Block 9 (TASK-388) — see {@link getActionLabel}. Mirrors
 * the `actionLabel(t, action)` pattern already used by
 * `features/provider/components/ProviderLogsPanel.tsx`'s own (separate) audit-log view.
 */
export const KNOWN_ACTIONS = [
  "user.invited",
  "user.updated",
  "user.deactivated",
  "user.profile_updated",
  "user.password_changed",
  "user.telegram_linked",
  "user.permissions_updated",
] as const;

/**
 * Translated activity-log action label. `t` must be scoped to `Dashboard.users.activityLog`
 * (`useTranslations("Dashboard.users.activityLog")`). Falls back to the raw action string for
 * anything outside {@link KNOWN_ACTIONS} (e.g. future action types added server-side first).
 */
export function getActionLabel(t: (key: string) => string, action: string): string {
  return (KNOWN_ACTIONS as readonly string[]).includes(action) ? t(`actions.${action}`) : action;
}

// ── Page definitions for permissions editor ───────────────────────────────────

export interface PageDef {
  slug: string;
  /** Roles that have access by default (undefined = all tenant roles) */
  defaultRoles: string[];
}

/**
 * All pages that can be permission-overridden, with their default role access. Display
 * labels live in i18n (`Dashboard.users.pageNames.*`, `t(page.slug)`) as of i18n Block 9
 * (TASK-388) — every slug here is a plain identifier (no dots), so it doubles directly as
 * the translation leaf key with no separate key-map needed.
 */
export const PAGES: PageDef[] = [
  { slug: "dashboard",  defaultRoles: ["enterprise_admin","network_manager","store_manager","merchandiser","storekeeper"] },
  { slug: "inventory",  defaultRoles: ["enterprise_admin","network_manager","store_manager","merchandiser","storekeeper"] },
  { slug: "stock",      defaultRoles: ["enterprise_admin","network_manager","store_manager","merchandiser","storekeeper"] },
  { slug: "receipts",   defaultRoles: ["enterprise_admin","network_manager","store_manager","storekeeper"] },
  { slug: "transfers",  defaultRoles: ["enterprise_admin","network_manager","store_manager","storekeeper"] },
  { slug: "write-offs", defaultRoles: ["enterprise_admin","network_manager","store_manager","merchandiser","storekeeper"] },
  { slug: "analytics",  defaultRoles: ["enterprise_admin","network_manager","store_manager"] },
  { slug: "users",      defaultRoles: ["enterprise_admin","network_manager","store_manager"] },
  { slug: "settings",   defaultRoles: ["enterprise_admin","network_manager","store_manager","merchandiser","storekeeper"] },
];

/** Role rank for hierarchy checks on the frontend (mirrors backend) */
export const ROLE_RANK: Record<string, number> = {
  enterprise_admin: 4,
  network_manager:  3,
  store_manager:    2,
  storekeeper:      1,
  merchandiser:     1,
  cashier:          1,
};

/** Returns the default access for a page based purely on role */
export function roleHasPageAccess(role: string, slug: string): boolean {
  const page = PAGES.find((p) => p.slug === slug);
  if (!page) return false;
  return page.defaultRoles.includes(role);
}

/** Returns effective access: override if set, otherwise role default */
export function effectivePageAccess(
  role: string,
  slug: string,
  permissions?: Record<string, boolean> | null,
): boolean {
  if (permissions && slug in permissions) return permissions[slug];
  return roleHasPageAccess(role, slug);
}
