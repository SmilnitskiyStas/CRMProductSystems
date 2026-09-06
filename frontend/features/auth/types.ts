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
  /** Effective TenantRole capabilities (ADR-020) — mirrors the JWT "capabilities" claim;
   * null/undefined/[] = no TenantRoleId or an empty/archived template. UI-only signal (real
   * enforcement is server-side via RoleOrCapabilityRequirement) — used to mirror
   * role-OR-capability gates like `canViewIntegrations` in `@/lib/roles`. */
  capabilities?: string[] | null;
  twoFactorEnabled: boolean;
  /** UI locale the user picked ("uk"/"en"); null/undefined = client falls back to the
   * `sg_locale` cookie or browser language (i18n rollout Block 1, TASK-375/376). */
  preferredLocale?: string | null;
  /** Effective TenantRole sidebar-tab visibility (TASK-391b) — mirrors the JWT "tabs"
   * claim; null/undefined/[] = no TenantRoleId or an empty AllowedTabs template. UI-only
   * signal (no Tier 2 backend enforcement yet) — see Sidebar.tsx/useRequireTab.ts for how
   * it's combined (OR) with the existing role/capability-based visibility checks. */
  tabs?: string[] | null;
  /** True while the account's current password is a temporary one issued by
   * `POST /api/auth/forgot-password` (TASK-465/466 temp-password redesign) — valid 3h,
   * cleared the moment the user changes it via `POST /api/auth/change-password`, or simply
   * expires on its own. Fresh on every mint site (login/refresh/2fa-verify) and `/auth/me`. */
  passwordIsTemporary: boolean;
  /** ISO UTC datetime the temporary password stops working; null whenever
   * `passwordIsTemporary` is false. */
  temporaryPasswordExpiresAt: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

// ---- Forgot password (public, TASK-457; redesigned to issue a temporary password
// instead of a reset link/token — TASK-465/466) ----

export interface ForgotPasswordRequest {
  email: string;
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
