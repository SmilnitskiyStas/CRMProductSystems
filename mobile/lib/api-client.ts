import { requestMfaCode } from '@/features/auth/mfaPrompt';
import axios, {
  type AxiosError,
  type InternalAxiosRequestConfig,
} from 'axios';
import * as SecureStore from 'expo-secure-store';
import {
  getSessionEpoch,
  terminateSession,
  updateInMemoryWorkspaceAccessToken,
  terminatePersonalSession,
  withSessionPersistence,
} from '@/features/auth/session';
import { useAuthStore } from '@/features/auth/store';

const BASE_URL = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5000/api';
const WORKSPACE_ACCESS_TOKEN_KEY = 'workspace_access_token';
const PERSONAL_ACCESS_TOKEN_KEY = 'personal_access_token';
const PERSONAL_REFRESH_TOKEN_KEY = 'personal_refresh_token';
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
  _mfaRetry?: boolean;
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
    '/auth/mfa-enrollment/setup',
    '/auth/mfa-enrollment/enable',
    '/auth/2fa/step-up',
    '/auth/refresh',
    '/consumer-auth/login',
    '/consumer-auth/register',
    '/consumer-auth/refresh',
    '/auth/forgot-password',
    '/auth/reset-password',
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

let mfaPromise: Promise<string> | null = null;
function confirmRecentMfa(): Promise<string> {
  if (!mfaPromise) {
    const epoch = getSessionEpoch();
    const before = useAuthStore.getState().workspaceAccessToken;
    mfaPromise = (async () => {
      const code = await requestMfaCode();
      if (!code) throw new Error('MFA_CONFIRMATION_CANCELLED');
      if (epoch !== getSessionEpoch() || before !== useAuthStore.getState().workspaceAccessToken) throw new Error('SESSION_CHANGED_DURING_MFA');
      const { data } = await apiClient.post<{ accessToken: string }>('/auth/2fa/step-up', { code });
      await withSessionPersistence(async () => {
        if (epoch !== getSessionEpoch() || before !== useAuthStore.getState().workspaceAccessToken) throw new Error('SESSION_CHANGED_DURING_MFA');
        await SecureStore.setItemAsync(WORKSPACE_ACCESS_TOKEN_KEY, data.accessToken);
        if (epoch !== getSessionEpoch()) throw new Error('SESSION_CHANGED_DURING_MFA');
        updateInMemoryWorkspaceAccessToken(data.accessToken);
      });
      return data.accessToken;
    })().finally(() => { mfaPromise = null; });
  }
  return mfaPromise;
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
    if (error.response?.status === 403 && (error.response.data as { code?: string })?.code === 'mfa_step_up_required'
        && original && !original._mfaRetry && hasBearerToken(original)) {
      original._mfaRetry = true;
      const epoch = getSessionEpoch();
      const before = await SecureStore.getItemAsync(WORKSPACE_ACCESS_TOKEN_KEY);
      if (`Bearer ${before}` !== original.headers.Authorization) return Promise.reject(error);
      try {
        const token = await confirmRecentMfa();
        if (epoch !== getSessionEpoch()) return Promise.reject(error);
        original.headers.Authorization = `Bearer ${token}`;
        return apiClient(original);
      } catch (confirmationError) { return Promise.reject(confirmationError); }
    }
    const isAuthenticatedUnauthorized =
      error.response?.status === 401 &&
      hasBearerToken(original) &&
      !isPublicAuthRequest(original);

    if (isAuthenticatedUnauthorized && `Bearer ${await SecureStore.getItemAsync(WORKSPACE_ACCESS_TOKEN_KEY)}` !== original?.headers.Authorization)
      return Promise.reject(error); // The response belongs to a previous login/refresh.

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

      await withSessionPersistence(async () => {
        if (requestEpoch !== getSessionEpoch()) throw new Error('SESSION_TERMINATED_DURING_REFRESH');
        const current = await SecureStore.getItemAsync(WORKSPACE_ACCESS_TOKEN_KEY);
        if (`Bearer ${current}` !== original.headers.Authorization && current !== accessToken)
          throw new Error('SESSION_CHANGED_DURING_REFRESH');
        await SecureStore.setItemAsync(WORKSPACE_ACCESS_TOKEN_KEY, accessToken);
        if (requestEpoch !== getSessionEpoch()) throw new Error('SESSION_TERMINATED_DURING_REFRESH');
        updateInMemoryWorkspaceAccessToken(accessToken);
      });
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
 * (memberships, QR/code, history, join-program). Uses only personal access/refresh
 * proofs. Consumer refresh never falls back to a workspace cookie or staff identity.
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

let personalRefreshPromise: Promise<string> | null = null;

function refreshPersonalAccessToken(): Promise<string> {
  if (personalRefreshPromise) return personalRefreshPromise;
  const epoch = getSessionEpoch();
  personalRefreshPromise = (async () => {
    const proof = await SecureStore.getItemAsync(PERSONAL_REFRESH_TOKEN_KEY);
    if (!proof) throw new Error('REFRESH_TOKEN_MISSING');
    const { data } = await axios.post<{ accessToken?: string; refreshToken?: string }>(
      `${BASE_URL}/consumer-auth/refresh`, { refreshToken: proof }, { timeout: API_TIMEOUT_MS, withCredentials: false });
    if (!data.accessToken || !data.refreshToken) throw new Error('REFRESH_TOKEN_MISSING');
    await withSessionPersistence(async () => {
      if (epoch !== getSessionEpoch()) throw new Error('SESSION_TERMINATED_DURING_REFRESH');
      if (await SecureStore.getItemAsync(PERSONAL_REFRESH_TOKEN_KEY) !== proof) throw new Error('SESSION_CHANGED_DURING_REFRESH');
      await SecureStore.setItemAsync(PERSONAL_REFRESH_TOKEN_KEY, data.refreshToken!);
      await SecureStore.setItemAsync(PERSONAL_ACCESS_TOKEN_KEY, data.accessToken!);
      if (epoch !== getSessionEpoch()) throw new Error('SESSION_TERMINATED_DURING_REFRESH');
      useAuthStore.setState({ personalAccessToken: data.accessToken! });
    });
    return data.accessToken;
  })().finally(() => { personalRefreshPromise = null; });
  return personalRefreshPromise;
}

personalApiClient.interceptors.response.use(response => response, async (error: AxiosError) => {
  const original = error.config as RetryableRequestConfig | undefined;
  if (error.response?.status !== 401 || !hasBearerToken(original) || isPublicAuthRequest(original) || !original)
    return Promise.reject(error);
  if (`Bearer ${await SecureStore.getItemAsync(PERSONAL_ACCESS_TOKEN_KEY)}` !== original.headers.Authorization)
    return Promise.reject(error);
  if (original._retry) { await terminatePersonalSession(); return Promise.reject(error); }
  original._retry = true;
  const epoch = getSessionEpoch();
  try {
    const token = await refreshPersonalAccessToken();
    if (epoch !== getSessionEpoch()) throw new Error('SESSION_TERMINATED_DURING_REFRESH');
    original.headers.Authorization = `Bearer ${token}`;
    return personalApiClient(original);
  } catch (refreshError) {
    if (epoch === getSessionEpoch() && isTerminalRefreshFailure(refreshError)) await terminatePersonalSession();
    return Promise.reject(refreshError);
  }
});
