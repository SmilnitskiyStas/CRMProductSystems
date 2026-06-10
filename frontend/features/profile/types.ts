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
};
