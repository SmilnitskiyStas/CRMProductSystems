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

export const ROLE_LABELS: Record<string, string> = {
  provider:          "Провайдер",
  enterprise_admin:  "Адмін мережі",
  network_manager:   "Менеджер мережі",
  store_manager:     "Менеджер магазину",
  merchandiser:      "Мерчандайзер",
  storekeeper:       "Комірник",
  cashier:           "Касир",
  /** v4.5 (ADR-020): minimal base tier, rank 0 — no default operational access, only
   *  whatever an assigned TenantRole template grants. Kept short for the compact role
   *  pill badges (UsersList/UserDetailPanel) — full explanation lives in InviteUserModal. */
  staff:             "Спеціаліст (без доступу)",
};
