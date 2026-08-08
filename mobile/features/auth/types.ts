export interface AuthUser {
  id: string;
  email: string;
  fullName: string;
  role: string;
  tenantId: string | null;
  locationId: string | null;
  permissions: Record<string, boolean>;
  capabilities: string[];
  tabs: string[];
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginSuccessResponse {
  accessToken: string;
  user: AuthUser;
}

export interface TwoFactorChallengeResponse {
  requiresTwoFactor: true;
  challengeToken: string;
  /**
   * TASK-497: set whenever the challenge was reached via the consumer/mobile-auth path —
   * the person already proved their personal password before the 2FA branch, so the app
   * stores this immediately and lets them into personal/loyalty mode while the workspace
   * 2FA step is still pending. Absent only for the legacy staff-only-fallback-with-2FA case.
   */
  personalAccessToken?: string | null;
}

export type LoginResponse = LoginSuccessResponse | TwoFactorChallengeResponse;

export interface VerifyTwoFactorRequest {
  challengeToken: string;
  code: string;
}

export function isTwoFactorChallenge(
  response: LoginResponse
): response is TwoFactorChallengeResponse {
  return 'requiresTwoFactor' in response && response.requiresTwoFactor;
}

// ── Consumer (loyalty wallet) session — TASK-405/407 ────────────────────────
// A wholly separate identity from AuthUser above: ConsumerAccount is global (no
// tenant_id), issued by POST /api/consumer-auth/register|login, never a staff User row.

export interface ConsumerUser {
  id: string; // ConsumerAccountId
  fullName: string;
  phone: string;
  /**
   * Always the literal 'consumer' — mirrors backend AppRoles.Consumer
   * (ShelfGuard.Domain/Constants/AppRoles.cs) and mobile/lib/roles.ts's AppRoles.Consumer.
   * Session branching itself keys off useAuthStore's `sessionKind` (see store.ts), but this
   * lets navigation code that wants an explicit `role === 'consumer'` check read it directly
   * off the user object instead of re-deriving it from sessionKind.
   */
  role: 'consumer';
}

export interface ConsumerRegisterRequest {
  phone: string;
  password: string;
  fullName: string;
  email?: string;
}

export interface MobileLoginRequest {
  identifier: string;
  password: string;
}

// TASK-497: an employee is first a normal loyalty-program participant (ConsumerAccount)
// who *additionally* gets workspace access when linked to an active staff User. Both
// identities are usable at once, backed by two separate JWTs the app holds simultaneously.
export interface MobileLoginSuccess {
  /** Consumer/loyalty JWT — null only in the legacy staff-only fallback (no personal
   * ConsumerAccount exists yet for this identifier at all). */
  personalAccessToken: string | null;
  /** Staff/tenant JWT — null for a plain consumer with no linked, active staff User. */
  workspaceAccessToken: string | null;
  user: {
    id: string;
    fullName: string;
    email: string | null;
    phone: string | null;
    tenantId: string | null;
    storeId: string | null;
  };
  access: {
    canAccessWorkspace: boolean;
    role: string;
    permissions: Record<string, boolean>;
    capabilities: string[];
    tabs: string[];
  };
}

export type MobileLoginResponse = MobileLoginSuccess | TwoFactorChallengeResponse;
