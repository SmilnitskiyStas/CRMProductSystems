"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { authApi } from "../api/auth";
import { clearToken, getToken, markLoggedOut } from "@/lib/api";
import { clearStoredUser } from "../store";
import { isTwoFactorChallenge, type LoginSuccessResponse } from "../types";
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

/** Shared post-login handler — used by both password login and 2FA verify. */
function useCompleteLogin() {
  const queryClient = useQueryClient();
  const router = useRouter();

  return (data: LoginSuccessResponse) => {
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
  };
}

/** Login mutation — stores token + user on success, redirects to dashboard.
 *  For a 2FA-enabled account the API returns a challenge instead of tokens —
 *  no redirect happens here; LoginForm switches to the code step. */
export function useLogin() {
  const completeLogin = useCompleteLogin();

  return useMutation({
    mutationFn: authApi.login,
    onSuccess: (data) => {
      if (isTwoFactorChallenge(data)) return;
      completeLogin(data);
    },
  });
}

/** Step 2 of a 2FA login — verifies the code, then behaves like a normal login. */
export function useVerifyTwoFactor() {
  const completeLogin = useCompleteLogin();

  return useMutation({
    mutationFn: authApi.verifyTwoFactor,
    onSuccess: completeLogin,
  });
}

/** Requests a temporary password by email. Always resolves once the HTTP request itself
 *  succeeds — the backend returns 204 unconditionally, regardless of whether the email
 *  exists/is active, so there is no auth side effect and no onSuccess handler here. */
export function useForgotPassword() {
  return useMutation({
    mutationFn: authApi.forgotPassword,
  });
}

/** Logout mutation — clears all auth state and redirects to /login. */
export function useLogout() {
  const queryClient = useQueryClient();
  const router = useRouter();

  return useMutation({
    mutationFn: () => {
      // Flag BEFORE the logout call: in-flight polling requests (chat widget,
      // notifications) will 401 once the refresh cookie is revoked — the flag
      // keeps them from hijacking the redirect with ?reason=session_expired (BUG-011).
      markLoggedOut();
      return authApi.logout();
    },
    onSettled: () => {
      clearToken();
      clearStoredUser();
      queryClient.clear();
      useStoreContext.setState({ selectedStoreId: null });
      router.push("/login");
    },
  });
}
