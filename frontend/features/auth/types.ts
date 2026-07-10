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
}

export interface LoginRequest {
  email: string;
  password: string;
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
