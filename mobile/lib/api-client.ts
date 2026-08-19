import axios, {
  type AxiosError,
  type InternalAxiosRequestConfig,
} from 'axios';
import * as SecureStore from 'expo-secure-store';
import {
  getSessionEpoch,
  terminateSession,
  updateInMemoryWorkspaceAccessToken,
} from '@/features/auth/session';

const BASE_URL = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5000/api';
const WORKSPACE_ACCESS_TOKEN_KEY = 'workspace_access_token';
const PERSONAL_ACCESS_TOKEN_KEY = 'personal_access_token';
export const API_TIMEOUT_MS = 15_000;

export function resolveApiAssetUrl(path: string | null | undefined): string | null {
  if (!path) return null;
  const normalized = path.trim();
  if (/^https:\/\//i.test(normalized)) return normalized;
  if (__DEV__ && /^http:\/\//i.test(normalized)) return normalized;
  if (/^[a-z][a-z0-9+.-]*:/i.test(normalized) || normalized.startsWith('//')) return null;
  const origin = BASE_URL.replace(/\/api\/?$/, '');
  return `${origin}${normalized.startsWith('/') ? normalized : `/${normalized}`}`;
}

type RetryableRequestConfig = InternalAxiosRequestConfig & {
  _retry?: boolean;
};

let refreshPromise: Promise<string> | null = null;

/**
 * Workspace-scoped client (TASK-497) — every feature module except the consumer loyalty
 * wallet (customers, pos, stock, transfers, ...) uses this, unchanged. Attaches the staff
 * JWT and owns the refresh-and-retry flow. Kept as the default export/name so every
 * existing `import { apiClient } from '@/lib/api-client'` call site keeps compiling.
 */
export const apiClient = axios.create({
  baseURL: BASE_URL,
  withCredentials: true,
  timeout: API_TIMEOUT_MS,
});

apiClient.interceptors.request.use(async (config) => {
  const token = await SecureStore.getItemAsync(WORKSPACE_ACCESS_TOKEN_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

function hasBearerToken(config: RetryableRequestConfig | undefined): boolean {
  const authorization = config?.headers?.Authorization;
  return typeof authorization === 'string' && authorization.startsWith('Bearer ');
}

function isPublicAuthRequest(config: RetryableRequestConfig | undefined): boolean {
  const url = config?.url ?? '';
  return [
    '/auth/login',
    '/auth/2fa/verify',
    '/auth/refresh',
    '/consumer-auth/login',
    '/consumer-auth/register',
    '/mobile-auth/login',
    '/mobile-auth/register',
  ].some((path) => url.endsWith(path));
}

async function requestRefreshedAccessToken(): Promise<string> {
  if (!refreshPromise) {
    refreshPromise = axios
      .post<{ accessToken?: string }>(`${BASE_URL}/auth/refresh`, null, {
        withCredentials: true,
        timeout: API_TIMEOUT_MS,
      })
      .then(({ data }) => {
        if (!data.accessToken) throw new Error('REFRESH_TOKEN_MISSING');
        return data.accessToken;
      })
      .finally(() => {
        refreshPromise = null;
      });
  }

  return refreshPromise;
}

function isTerminalRefreshFailure(error: unknown): boolean {
  if ((error as Error)?.message === 'REFRESH_TOKEN_MISSING') return true;
  const status = (error as AxiosError)?.response?.status;
  return status === 400 || status === 401 || status === 403;
}

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as RetryableRequestConfig | undefined;
    const isAuthenticatedUnauthorized =
      error.response?.status === 401 &&
      hasBearerToken(original) &&
      !isPublicAuthRequest(original);

    if (isAuthenticatedUnauthorized && original?._retry) {
      await terminateSession();
      return Promise.reject(error);
    }

    const shouldRefresh =
      isAuthenticatedUnauthorized &&
      original !== undefined &&
      !original._retry;

    if (!shouldRefresh) return Promise.reject(error);

    original._retry = true;
    const requestEpoch = getSessionEpoch();

    try {
      const accessToken = await requestRefreshedAccessToken();
      if (requestEpoch !== getSessionEpoch()) {
        throw new Error('SESSION_TERMINATED_DURING_REFRESH');
      }

      await SecureStore.setItemAsync(WORKSPACE_ACCESS_TOKEN_KEY, accessToken);
      updateInMemoryWorkspaceAccessToken(accessToken);
      original.headers.Authorization = `Bearer ${accessToken}`;
      return apiClient(original);
    } catch (refreshError) {
      if ((refreshError as Error)?.message === 'SESSION_TERMINATED_DURING_REFRESH') {
        return Promise.reject(error);
      }
      if (isTerminalRefreshFailure(refreshError)) {
        await terminateSession();
      }
      // Preserve a still-persisted session for transport/timeout/5xx refresh failures.
      // Reject the refresh error itself so cold bootstrap can distinguish retryable
      // unavailability from the original authenticated 401.
      return Promise.reject(refreshError);
    }
  }
);

/**
 * Personal-scoped client (TASK-497) — the consumer loyalty wallet API surface only
 * (memberships, QR/code, history, join-program). Attaches the consumer JWT. No
 * refresh-and-retry: the backend issues no refresh token for a personal/consumer session
 * (see ConsumerAuthService.cs's "no 2FA/refresh-token flow" note) — a 401 here just
 * propagates to the caller, same as any other failed request. This is what structurally
 * guarantees the consumer JWT is never used for workspace API calls and vice versa: the
 * scoping is per-module (which client a feature's api/ file imports), not a runtime flag
 * that could drift out of sync with whatever screen is currently focused.
 */
export const personalApiClient = axios.create({
  baseURL: BASE_URL,
  withCredentials: true,
  timeout: API_TIMEOUT_MS,
});

personalApiClient.interceptors.request.use(async (config) => {
  const token = await SecureStore.getItemAsync(PERSONAL_ACCESS_TOKEN_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});
