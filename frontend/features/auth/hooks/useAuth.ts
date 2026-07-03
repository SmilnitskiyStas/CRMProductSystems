"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { authApi } from "../api/auth";
import { clearToken, getToken } from "@/lib/api";
import { clearStoredUser } from "../store";
import { useStoreContext } from "@/lib/useStoreContext";
import { AppRoles, PROVIDER_TEAM, type AppRole } from "@/lib/roles";

export const ME_KEY = ["me"] as const;

/** Fetches current user. Disabled when no token is stored. */
export function useMe() {
  return useQuery({
    queryKey: ME_KEY,
    queryFn: authApi.getMe,
    enabled: typeof window !== "undefined" && Boolean(getToken()),
    staleTime: 0,
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
      // Clear ALL cached data from any previous user/tenant before setting
      // the new user — prevents cross-tenant data leakage via React Query cache.
      queryClient.clear();
      // Reset persisted store selection — the new tenant may not have the
      // same stores, and a stale selectedStoreId would point to a wrong tenant.
      useStoreContext.setState({ selectedStoreId: null });
      queryClient.setQueryData(ME_KEY, data.user);
      const role = data.user.role as AppRole;
      // supplier_admin has no dashboard — land directly in the cabinet (v4.1, ADR-016)
      router.push(
        PROVIDER_TEAM.has(role)
          ? "/provider"
          : role === AppRoles.SupplierAdmin
          ? "/supplier/profile"
          : "/dashboard"
      );
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
      useStoreContext.setState({ selectedStoreId: null });
      router.push("/login");
    },
  });
}
