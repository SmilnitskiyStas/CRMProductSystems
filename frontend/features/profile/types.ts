export interface UpdateProfileRequest {
  fullName: string;
  phone?: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface TelegramLinkResponse {
  linkUrl: string;   // e.g. t.me/ShelfGuardBot?start=CODE
  code: string;
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
