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
      // Patch cached user with the latest name, phone and preferred locale (the
      // language switcher — LanguageSwitcher.tsx, i18n Block 1 — reuses this same
      // mutation so DashboardIntlProvider's cookie->user.preferredLocale fallback
      // stays in sync with the server).
      qc.setQueryData<AuthUserDto>(ME_KEY, (prev) =>
        prev
          ? { ...prev, fullName: updated.fullName, phone: updated.phone, preferredLocale: updated.preferredLocale }
          : prev,
      );
    },
  });
}

export function useChangePassword() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: changePassword,
    onSuccess: () => {
      // Backend clears the temp-password marker on a successful change (TASK-465) —
      // invalidate so TemporaryPasswordBanner.tsx (and anything else reading
      // passwordIsTemporary) drops it as soon as possible instead of waiting for some
      // unrelated refetch/relogin. changePassword itself returns void (204), so there's
      // no fresh user payload to patch in directly, unlike useUpdateProfile above.
      qc.invalidateQueries({ queryKey: ME_KEY });
    },
  });
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
