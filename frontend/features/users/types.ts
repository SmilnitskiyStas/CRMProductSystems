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
}

export interface UpdateUserRequest {
  fullName: string;
  phone?: string | null;
  role: string;
  storeId?: string | null;
}

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

/** Human-readable action labels for the activity log */
export const ACTION_LABELS: Record<string, string> = {
  "user.invited":              "Запрошено нового користувача",
  "user.updated":              "Оновлено дані",
  "user.deactivated":          "Деактивовано",
  "user.profile_updated":      "Оновлено профіль",
  "user.password_changed":     "Змінено пароль",
  "user.telegram_linked":      "Підключено Telegram",
  "user.permissions_updated":  "Оновлено доступи",
};

// ── Page definitions for permissions editor ───────────────────────────────────

export interface PageDef {
  slug: string;
  label: string;
  /** Roles that have access by default (undefined = all tenant roles) */
  defaultRoles: string[];
}

/** All pages that can be permission-overridden, with their default role access */
export const PAGES: PageDef[] = [
  { slug: "dashboard",  label: "Дашборд",      defaultRoles: ["enterprise_admin","network_manager","store_manager","merchandiser","storekeeper"] },
  { slug: "inventory",  label: "Каталог",       defaultRoles: ["enterprise_admin","network_manager","store_manager","merchandiser","storekeeper"] },
  { slug: "stock",      label: "Залишки",       defaultRoles: ["enterprise_admin","network_manager","store_manager","merchandiser","storekeeper"] },
  { slug: "receipts",   label: "Прийомка",      defaultRoles: ["enterprise_admin","network_manager","store_manager","storekeeper"] },
  { slug: "transfers",  label: "Переміщення",   defaultRoles: ["enterprise_admin","network_manager","store_manager","storekeeper"] },
  { slug: "write-offs", label: "Списання",      defaultRoles: ["enterprise_admin","network_manager","store_manager","merchandiser","storekeeper"] },
  { slug: "analytics",  label: "Аналітика",     defaultRoles: ["enterprise_admin","network_manager","store_manager"] },
  { slug: "users",      label: "Персонал",      defaultRoles: ["enterprise_admin","network_manager","store_manager"] },
  { slug: "settings",   label: "Налаштування",  defaultRoles: ["enterprise_admin","network_manager","store_manager","merchandiser","storekeeper"] },
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
