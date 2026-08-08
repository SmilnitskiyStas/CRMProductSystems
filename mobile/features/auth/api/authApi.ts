import { apiClient } from '@/lib/api-client';
import type {
  AuthUser,
  LoginRequest,
  LoginResponse,
  LoginSuccessResponse,
  VerifyTwoFactorRequest,
} from '../types';

/**
 * Wire shape of AuthUserDto. The assigned location is still serialized as
 * storeId by the backend, so the mobile boundary maps it to locationId.
 */
interface AuthUserWire {
  id: string;
  email: string;
  fullName: string;
  role: string;
  tenantId: string | null;
  storeId: string | null;
  permissions?: Record<string, boolean> | null;
  capabilities?: string[] | null;
  tabs?: string[] | null;
}

interface LoginWireResponse {
  accessToken?: string;
  user?: AuthUserWire;
  requiresTwoFactor?: boolean;
  challengeToken?: string;
}

function mapAuthUser(wire: AuthUserWire): AuthUser {
  return {
    id: wire.id,
    email: wire.email,
    fullName: wire.fullName,
    role: wire.role,
    tenantId: wire.tenantId,
    locationId: wire.storeId,
    permissions: wire.permissions ?? {},
    capabilities: wire.capabilities ?? [],
    tabs: wire.tabs ?? [],
  };
}

export async function login(body: LoginRequest): Promise<LoginResponse> {
  const { data } = await apiClient.post<LoginWireResponse>('/auth/login', body);

  if (data.requiresTwoFactor) {
    if (!data.challengeToken) throw new Error('INVALID_TWO_FACTOR_CHALLENGE');
    return { requiresTwoFactor: true, challengeToken: data.challengeToken };
  }

  if (!data.accessToken || !data.user) throw new Error('INVALID_LOGIN_RESPONSE');
  return { accessToken: data.accessToken, user: mapAuthUser(data.user) };
}

export async function verifyTwoFactor(
  body: VerifyTwoFactorRequest
): Promise<LoginSuccessResponse> {
  const { data } = await apiClient.post<{
    accessToken?: string;
    user?: AuthUserWire;
  }>('/auth/2fa/verify', body);

  if (!data.accessToken || !data.user) throw new Error('INVALID_TWO_FACTOR_RESPONSE');
  return { accessToken: data.accessToken, user: mapAuthUser(data.user) };
}

export async function logout(): Promise<void> {
  await apiClient.post('/auth/logout');
}

export async function getMe(): Promise<AuthUser> {
  // Cold bootstrap must reach the retryable offline state promptly instead of
  // leaving route guards on an unbounded/textless loading screen.
  const { data } = await apiClient.get<AuthUserWire>('/auth/me', { timeout: 15_000 });
  return mapAuthUser(data);
}
