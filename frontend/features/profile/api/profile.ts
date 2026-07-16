import { api } from "@/lib/api";
import type { UpdateProfileRequest, ChangePasswordRequest, TelegramLinkCodeResponse } from "../types";
import type { AuthUserDto } from "@/features/auth/types";

/** PUT /api/auth/me — update own profile (name, phone) */
export async function updateProfile(data: UpdateProfileRequest): Promise<AuthUserDto> {
  return api.put<AuthUserDto>("/api/auth/me", data);
}

/** POST /api/auth/change-password */
export async function changePassword(data: ChangePasswordRequest): Promise<void> {
  await api.post<void>("/api/auth/change-password", data);
}

/**
 * POST /api/telegram/link-code — issues a one-time 15-minute code. The user opens the
 * returned deep link (or sends /start <code> to the bot manually); the worker's
 * telegram-listener validates the code and writes users.TelegramChatId itself — this is the
 * only step the frontend takes, there is no "submit a chat id" call anymore (2026-07-15
 * security fix: the old POST /api/auth/telegram/link accepted a raw client-supplied chat_id
 * with no proof of ownership and has been removed).
 */
export async function createTelegramLinkCode(): Promise<TelegramLinkCodeResponse> {
  return api.post<TelegramLinkCodeResponse>("/api/telegram/link-code");
}
