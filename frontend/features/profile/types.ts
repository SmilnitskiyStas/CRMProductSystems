export interface UpdateProfileRequest {
  fullName: string;
  phone?: string;
  /** Optional UI locale ("uk"/"en") — i18n Block 1, TASK-376. Omitted by the regular
   * profile form; only the language switcher (LanguageSwitcher.tsx) sets this. */
  preferredLocale?: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

/** POST /api/telegram/link-code response — matches backend LinkCodeDto (camelCase on the wire). */
export interface TelegramLinkCodeResponse {
  code: string;
  /** e.g. https://t.me/shelfguard_bot?start=CODE — opens the bot with the code pre-filled. */
  deepLink: string;
  expiresAt: string;
}

/**
 * Role keys in display order. Labels live in i18n (`Dashboard.roles.*`, `useTranslations`)
 * as of i18n Block 9 (TASK-388) — see {@link getRoleLabel}. Kept here as the canonical key
 * list since it's shared across `users`/`profile` components and a couple of already-
 * translated call sites outside those features (UserMenu.tsx, settings-user/page.tsx).
 *
 * "staff" (v4.5, ADR-020): minimal base tier, rank 0 — no default operational access, only
 * whatever an assigned TenantRole template grants. Kept short for the compact role pill
 * badges (UsersList/UserDetailPanel) — full explanation lives in InviteUserModal.
 */
export const ROLE_KEYS = [
  "provider",
  "enterprise_admin",
  "network_manager",
  "store_manager",
  "merchandiser",
  "storekeeper",
  "cashier",
  "staff",
] as const;

/** Role key -> i18n leaf key under `Dashboard.roles` (camelCase; role keys themselves are snake_case). */
const ROLE_I18N_KEY: Record<string, string> = {
  provider: "provider",
  enterprise_admin: "enterpriseAdmin",
  network_manager: "networkManager",
  store_manager: "storeManager",
  merchandiser: "merchandiser",
  storekeeper: "storekeeper",
  cashier: "cashier",
  staff: "staff",
};

/**
 * Translated role label. `t` must be scoped to `Dashboard.roles` (`useTranslations("Dashboard.roles")`).
 * Falls back to the raw role key if unrecognized (mirrors the old `ROLE_LABELS[role] ?? role` pattern).
 */
export function getRoleLabel(t: (key: string) => string, role: string | null | undefined): string {
  if (!role) return "";
  const key = ROLE_I18N_KEY[role];
  return key ? t(key) : role;
}
