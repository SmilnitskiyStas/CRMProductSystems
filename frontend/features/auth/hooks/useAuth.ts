"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { authApi } from "../api/auth";
import { clearToken, getToken } from "@/lib/api";
import { clearStoredUser } from "../store";
import { PROVIDER_TEAM } from "@/lib/roles";

export const ME_KEY = ["me"] as const;

/** Fetches current user. Disabled when no token is stored. */
export function useMe() {
  return useQuery({
    queryKey: ME_KEY,
    queryFn: authApi.getMe,
    enabled: typeof window !== "undefined" && Boolean(getToken()),
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** Login mutation — stores token + user on success, redirects to dashboard. */
export function useLogin() {
  const queryClient = useQueryClient();
  const router = useRouter();

  return useMutation({
    mutationFn: authApi.login,
    onSuccess: (data) => {
      queryClient.setQueryData(ME_KEY, data.user);
      router.push(PROVIDER_TEAM.has(data.user.role) ? "/provider" : "/dashboard");
    },
  });
}

/** Logout mutation — clears all auth state and redirects to /login. */
export function useLogout() {
  const queryClient = useQueryClient();
  const router = useRouter();

  return useMutation({
    mutationFn: authApi.logout,
    onSettled: () => {
      clearToken();
      clearStoredUser();
      queryClient.clear();
      router.push("/login");
    },
  });
}
