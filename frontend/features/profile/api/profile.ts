import { api } from "@/lib/api";
import type { UpdateProfileRequest, ChangePasswordRequest } from "../types";
import type { AuthUserDto } from "@/features/auth/types";

/** PUT /api/auth/me — update own profile (name, phone) */
export async function updateProfile(data: UpdateProfileRequest): Promise<AuthUserDto> {
  return api.put<AuthUserDto>("/api/auth/me", data);
}

/** POST /api/auth/change-password */
export async function changePassword(data: ChangePasswordRequest): Promise<void> {
  await api.post<void>("/api/auth/change-password", data);
}

/** POST /api/auth/telegram/link — link Telegram chat by chatId */
export async function linkTelegram(chatId: string): Promise<void> {
  await api.post<void>("/api/auth/telegram/link", { chatId });
}
