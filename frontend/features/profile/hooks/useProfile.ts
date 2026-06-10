"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { updateProfile, changePassword, linkTelegram } from "../api/profile";
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

export function useLinkTelegram() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: linkTelegram,
    onSuccess: () => {
      // Mark telegram as linked in the cached user
      qc.setQueryData<AuthUserDto>(ME_KEY, (prev) =>
        prev ? { ...prev, telegramChatId: "linked" } : prev,
      );
    },
  });
}
