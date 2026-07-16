import { apiClient } from '@/lib/api-client';
import type { AuthUser, LoginRequest, LoginResponse } from '../types';

/**
 * Wire shape of `AuthUserDto` (backend `Features/Auth/Dtos/AuthDtos.cs`). The
 * assigned-store/location field is still named `StoreId` on the backend `User`
 * entity (never renamed in the v4 Store→Location pass, unlike product_stock etc.)
 * so it serializes as `storeId`, not `locationId`.
 */
interface AuthUserWire {
  id: string;
  email: string;
  fullName: string;
  role: string;
  tenantId: string | null;
  storeId: string | null;
}

/** Raw `/auth/login` response — 2FA-enabled accounts return `requiresTwoFactor` instead. */
interface LoginWireResponse {
  accessToken?: string;
  user?: AuthUserWire;
  requiresTwoFactor?: boolean;
}

function mapAuthUser(wire: AuthUserWire): AuthUser {
  return {
    id: wire.id,
    email: wire.email,
    fullName: wire.fullName,
    role: wire.role,
    tenantId: wire.tenantId,
    locationId: wire.storeId,
  };
}

export async function login(body: LoginRequest): Promise<LoginResponse> {
  const { data } = await apiClient.post<LoginWireResponse>('/auth/login', body);

  // 2FA-enabled accounts get {requiresTwoFactor, challengeToken} with no tokens yet.
  // Mobile has no 2FA step UI (TASK-330/331 only built it for web) — fail loudly
  // instead of silently calling setAuth(undefined, undefined) and navigating in.
  if (data.requiresTwoFactor || !data.accessToken || !data.user) {
    throw new Error('TWO_FACTOR_REQUIRED');
  }

  return { accessToken: data.accessToken, user: mapAuthUser(data.user) };
}

export async function logout(): Promise<void> {
  await apiClient.post('/auth/logout');
}

export async function getMe(): Promise<AuthUser> {
  const { data } = await apiClient.get<AuthUserWire>('/auth/me');
  return mapAuthUser(data);
}
