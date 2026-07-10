import { api, setToken, clearToken } from "@/lib/api";
import { setStoredUser, clearStoredUser } from "../store";
import {
  isTwoFactorChallenge,
  type AuthUserDto,
  type LoginRequest,
  type LoginResponse,
  type LoginSuccessResponse,
  type TwoFactorDisableRequest,
  type TwoFactorEnableResponse,
  type TwoFactorSetupResponse,
  type TwoFactorVerifyRequest,
} from "../types";

export const authApi = {
  login: async (payload: LoginRequest): Promise<LoginResponse> => {
    const res = await api.post<LoginResponse>("/api/auth/login", payload);
    // 2FA challenge — no tokens issued yet, the user must pass the code step first.
    if (isTwoFactorChallenge(res)) return res;
    setToken(res.accessToken);
    setStoredUser(res.user);
    return res;
  },

  /** Step 2 of a 2FA login — exchanges challenge + code for real tokens. */
  verifyTwoFactor: async (payload: TwoFactorVerifyRequest): Promise<LoginSuccessResponse> => {
    const res = await api.post<LoginSuccessResponse>("/api/auth/2fa/verify", payload);
    setToken(res.accessToken);
    setStoredUser(res.user);
    return res;
  },

  refresh: async (): Promise<LoginSuccessResponse> => {
    const res = await api.post<LoginSuccessResponse>("/api/auth/refresh");
    setToken(res.accessToken);
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

  getMe: (): Promise<AuthUserDto> => api.get<AuthUserDto>("/api/auth/me"),

  // ---- 2FA management (authorized) ----

  /** Generates a pending TOTP secret. 2FA stays off until enable succeeds. */
  setupTwoFactor: (): Promise<TwoFactorSetupResponse> =>
    api.post<TwoFactorSetupResponse>("/api/auth/2fa/setup"),

  /** Confirms the pending secret with a code → returns one-time recovery codes. */
  enableTwoFactor: (code: string): Promise<TwoFactorEnableResponse> =>
    api.post<TwoFactorEnableResponse>("/api/auth/2fa/enable", { code }),

  disableTwoFactor: (payload: TwoFactorDisableRequest): Promise<void> =>
    api.post<void>("/api/auth/2fa/disable", payload),
};
