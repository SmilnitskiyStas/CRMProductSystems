export interface AuthUserDto {
  id: string;
  email: string;
  fullName: string;
  role: string;
  tenantId: string | null;
  tenantName: string | null;
  storeId: string | null;
  phone?: string | null;
  telegramChatId?: string | null;
  permissions?: Record<string, boolean> | null;
  twoFactorEnabled: boolean;
  /** UI locale the user picked ("uk"/"en"); null/undefined = client falls back to the
   * `sg_locale` cookie or browser language (i18n rollout Block 1, TASK-375/376). */
  preferredLocale?: string | null;
  /** Effective TenantRole sidebar-tab visibility (TASK-391b) — mirrors the JWT "tabs"
   * claim; null/undefined/[] = no TenantRoleId or an empty AllowedTabs template. UI-only
   * signal (no Tier 2 backend enforcement yet) — see Sidebar.tsx/useRequireTab.ts for how
   * it's combined (OR) with the existing role/capability-based visibility checks. */
  tabs?: string[] | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

// ---- Forgot / reset password (public, TASK-457) ----

export interface ForgotPasswordRequest {
  email: string;
}

export interface ResetPasswordRequest {
  token: string;
  newPassword: string;
}

/** Successful login/refresh/2FA-verify — tokens issued. */
export interface LoginSuccessResponse {
  accessToken: string;
  user: AuthUserDto;
}

/** Login for a 2FA-enabled account — no tokens yet, a short-lived (5 min) challenge instead. */
export interface TwoFactorChallengeResponse {
  requiresTwoFactor: true;
  challengeToken: string;
}

export type LoginResponse = LoginSuccessResponse | TwoFactorChallengeResponse;

export function isTwoFactorChallenge(
  res: LoginResponse,
): res is TwoFactorChallengeResponse {
  return "requiresTwoFactor" in res && res.requiresTwoFactor === true;
}

// ---- 2FA management (POST /api/auth/2fa/*) ----

export interface TwoFactorVerifyRequest {
  challengeToken: string;
  /** 6-digit TOTP or a recovery code (XXXX-XXXX, case/dash-insensitive). */
  code: string;
}

export interface TwoFactorSetupResponse {
  secret: string;
  otpauthUri: string;
}

export interface TwoFactorEnableResponse {
  /** 8 one-time recovery codes — shown exactly once, never retrievable again. */
  recoveryCodes: string[];
}

export interface TwoFactorDisableRequest {
  password: string;
  /** 6-digit TOTP or a recovery code. */
  code: string;
}
