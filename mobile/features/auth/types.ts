export interface AuthUser {
  id: string;
  email: string;
  fullName: string;
  role: string;
  tenantId: string | null;
  locationId: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  user: AuthUser;
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

export interface ConsumerLoginRequest {
  phone: string;
  password: string;
}

export interface ConsumerRegisterRequest {
  phone: string;
  password: string;
  fullName: string;
  email?: string;
}

export interface ConsumerAuthResult {
  accessToken: string;
  user: ConsumerUser;
}
