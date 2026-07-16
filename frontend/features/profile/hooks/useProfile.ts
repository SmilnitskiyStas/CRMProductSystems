"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { updateProfile, changePassword, createTelegramLinkCode } from "../api/profile";
import { authApi } from "@/features/auth/api/auth";
import { ME_KEY } from "@/features/auth/hooks/useAuth";
import type { AuthUserDto } from "@/features/auth/types";

export function useUpdateProfile() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: updateProfile,
    onSuccess: (updated) => {
      // Patch cached user with the latest name and phone
      qc.setQueryData<AuthUserDto>(ME_KEY, (prev) =>
        prev
          ? { ...prev, fullName: updated.fullName, phone: updated.phone }
          : prev,
      );
    },
  });
}

export function useChangePassword() {
  return useMutation({ mutationFn: changePassword });
}

// ---- 2FA management ----

/** POST /api/auth/2fa/setup — generates a pending secret + otpauth URI (QR). */
export function useTwoFactorSetup() {
  return useMutation({ mutationFn: authApi.setupTwoFactor });
}

/** POST /api/auth/2fa/enable — confirms the pending secret, returns recovery codes. */
export function useTwoFactorEnable() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: authApi.enableTwoFactor,
    onSuccess: () => {
      // Optimistic patch + refetch /api/auth/me for the authoritative state
      qc.setQueryData<AuthUserDto>(ME_KEY, (prev) =>
        prev ? { ...prev, twoFactorEnabled: true } : prev,
      );
      qc.invalidateQueries({ queryKey: ME_KEY });
    },
  });
}

/** POST /api/auth/2fa/disable — requires password + TOTP/recovery code. */
export function useTwoFactorDisable() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: authApi.disableTwoFactor,
    onSuccess: () => {
      qc.setQueryData<AuthUserDto>(ME_KEY, (prev) =>
        prev ? { ...prev, twoFactorEnabled: false } : prev,
      );
      qc.invalidateQueries({ queryKey: ME_KEY });
    },
  });
}

/** Issues a one-time link code (TelegramLinkSection then polls /api/auth/me for the result). */
export function useCreateTelegramLinkCode() {
  return useMutation({ mutationFn: createTelegramLinkCode });
}
