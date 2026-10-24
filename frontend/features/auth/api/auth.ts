import { api, setToken, clearToken, getSessionEpoch, ApiError } from "@/lib/api";
import { setStoredUser, clearStoredUser } from "../store";
import {
  isTwoFactorChallenge,
  isMfaEnrollment,
  type AuthUserDto,
  type ForgotPasswordRequest,
  type ResetPasswordRequest,
  type LoginRequest,
  type LoginResponse,
  type LoginSuccessResponse,
  type TwoFactorDisableRequest,
  type TwoFactorEnableResponse,
  type TwoFactorSetupResponse,
  type TwoFactorVerifyRequest,
} from "../types";

/**
 * The backend always ships a `permissions` object on the auth user, never null —
 * `AuthService.BuildEffectivePermissionsAsync` returns an empty dict when the user has
 * no per-user overrides and no active grants. An empty object means exactly "no
 * overrides" — identical to null — but `{}` is truthy, so every `!me.permissions` /
 * `me.permissions && …` owner check across the app (Sidebar, the `/supplier/*` page
 * guards, `resolvePermissions`) would otherwise read it as "restricted to nothing".
 * That mismatch hid the whole permission-gated supplier cabinet (items, team,
 * schedules, …) from owner `supplier_admin` users, who have no role-default fallback.
 * Normalise `{}` → null once, here, at every mint site.
 */
function normalizeAuthUser<T extends AuthUserDto>(user: T): T {
  if (user.permissions && Object.keys(user.permissions).length === 0) {
    user.permissions = null;
  }
  return user;
}

export const authApi = {
  setupEnrollment: (enrollmentToken: string): Promise<TwoFactorSetupResponse> =>
    api.post("/api/auth/mfa-enrollment/setup", { enrollmentToken }),
  enableEnrollment: async (enrollmentToken: string, code: string): Promise<TwoFactorEnableResponse> => {
    const epoch = getSessionEpoch();
    const result = await api.post<TwoFactorEnableResponse>("/api/auth/mfa-enrollment/enable", { enrollmentToken, code });
    if (epoch !== getSessionEpoch()) throw new ApiError(401, "Session changed.");
    setToken(result.accessToken);
    setStoredUser(normalizeAuthUser(result.user));
    return result;
  },
  login: async (payload: LoginRequest): Promise<LoginResponse> => {
    const res = await api.post<LoginResponse>("/api/auth/login", payload);
    // 2FA challenge — no tokens issued yet, the user must pass the code step first.
    if (isTwoFactorChallenge(res) || isMfaEnrollment(res)) return res;
    setToken(res.accessToken);
    normalizeAuthUser(res.user);
    setStoredUser(res.user);
    return res;
  },

  /** Step 2 of a 2FA login — exchanges challenge + code for real tokens. */
  verifyTwoFactor: async (payload: TwoFactorVerifyRequest): Promise<LoginSuccessResponse> => {
    const res = await api.post<LoginSuccessResponse>("/api/auth/2fa/verify", payload);
    setToken(res.accessToken);
    normalizeAuthUser(res.user);
    setStoredUser(res.user);
    return res;
  },

  refresh: async (): Promise<LoginSuccessResponse> => {
    const res = await api.post<LoginSuccessResponse>("/api/auth/refresh");
    setToken(res.accessToken);
    normalizeAuthUser(res.user);
    setStoredUser(res.user);
    return res;
  },

  logout: async (): Promise<void> => {
    try {
      await api.post<void>("/api/auth/logout");
    } finally {
      clearToken();
      clearStoredUser();
    }
  },

  getMe: async (): Promise<AuthUserDto> =>
    normalizeAuthUser(await api.get<AuthUserDto>("/api/auth/me")),

  logoutAll: async (): Promise<void> => {
    await api.post<void>("/api/auth/logout-all");
    clearToken(); clearStoredUser();
  },

  // ---- Forgot password (public) ----

  /** Requests a short-lived email link. Existing credentials/sessions remain unchanged. */
  forgotPassword: (payload: ForgotPasswordRequest): Promise<void> =>
    api.post<void>("/api/auth/forgot-password", payload),

  resetPassword: async (payload: ResetPasswordRequest): Promise<void> => {
    await api.post<void>("/api/auth/reset-password", payload);
    clearToken();
    clearStoredUser();
  },

  // ---- 2FA management (authorized) ----

  /** Generates a pending TOTP secret. 2FA stays off until enable succeeds. */
  setupTwoFactor: (): Promise<TwoFactorSetupResponse> =>
    api.post<TwoFactorSetupResponse>("/api/auth/2fa/setup"),

  /** Confirms the pending secret with a code → returns one-time recovery codes. */
  enableTwoFactor: async (code: string): Promise<TwoFactorEnableResponse> => {
    const epoch = getSessionEpoch();
    const result = await api.post<TwoFactorEnableResponse>("/api/auth/2fa/enable", { code });
    if (epoch !== getSessionEpoch()) throw new ApiError(401, "Session changed.");
    setToken(result.accessToken);
    setStoredUser(normalizeAuthUser(result.user));
    return result;
  },

  disableTwoFactor: async (payload: TwoFactorDisableRequest): Promise<LoginSuccessResponse> => {
    const epoch = getSessionEpoch();
    const result = await api.post<LoginSuccessResponse>("/api/auth/2fa/disable", payload);
    if (epoch !== getSessionEpoch()) throw new ApiError(401, "Session changed.");
    setToken(result.accessToken);
    setStoredUser(normalizeAuthUser(result.user));
    return result;
  },
};
