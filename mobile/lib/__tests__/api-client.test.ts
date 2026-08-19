import axios from 'axios';
import MockAdapter from 'axios-mock-adapter';
import * as SecureStore from 'expo-secure-store';
import { useAuthStore } from '@/features/auth/store';
import { queryClient } from '@/lib/query-client';
import { terminateSession } from '@/features/auth/session';
import { API_TIMEOUT_MS, apiClient, personalApiClient, resolveApiAssetUrl } from '../api-client';

jest.mock('expo-secure-store', () => ({
  deleteItemAsync: jest.fn(),
  getItemAsync: jest.fn(),
  setItemAsync: jest.fn(),
}));

const mockDelete = SecureStore.deleteItemAsync as jest.MockedFunction<
  typeof SecureStore.deleteItemAsync
>;
const mockGet = SecureStore.getItemAsync as jest.MockedFunction<typeof SecureStore.getItemAsync>;
const mockSet = SecureStore.setItemAsync as jest.MockedFunction<typeof SecureStore.setItemAsync>;

let apiMock: MockAdapter;
let personalApiMock: MockAdapter;
let refreshMock: MockAdapter;
let storedWorkspaceToken: string | null;
let storedPersonalToken: string | null;

function setAuthenticatedState() {
  useAuthStore.setState({
    workspaceAccessToken: 'old-token',
    personalAccessToken: null,
    sessionKind: 'staff',
    user: {
      id: 'user-1',
      email: 'manager@example.com',
      fullName: 'Manager',
      role: 'store_manager',
      tenantId: 'tenant-1',
      locationId: 'location-1',
      permissions: {},
      capabilities: [],
      tabs: [],
    },
    consumerUser: null,
    twoFactorChallenge: null,
  });
}

describe('apiClient authentication lifecycle', () => {
  beforeEach(() => {
    apiMock = new MockAdapter(apiClient);
    personalApiMock = new MockAdapter(personalApiClient);
    refreshMock = new MockAdapter(axios);
    setAuthenticatedState();
    queryClient.setQueryData(['private'], { tenantSecret: true });
    storedWorkspaceToken = 'old-token';
    storedPersonalToken = null;
    mockGet.mockImplementation(async (key) => {
      if (key === 'workspace_access_token') return storedWorkspaceToken;
      if (key === 'personal_access_token') return storedPersonalToken;
      return null;
    });
    mockSet.mockImplementation(async (key, value) => {
      if (key === 'workspace_access_token') storedWorkspaceToken = value;
      if (key === 'personal_access_token') storedPersonalToken = value;
    });
    mockDelete.mockImplementation(async (key) => {
      if (key === 'workspace_access_token') storedWorkspaceToken = null;
      if (key === 'personal_access_token') storedPersonalToken = null;
    });
  });

  afterEach(() => {
    apiMock.restore();
    personalApiMock.restore();
    refreshMock.restore();
    queryClient.clear();
  });

  test('refreshes once, persists the token, and retries the original request', async () => {
    apiMock.onGet('/protected').replyOnce(401).onGet('/protected').reply(200, { ok: true });
    refreshMock.onPost(/\/auth\/refresh$/).reply(200, { accessToken: 'new-token' });

    await expect(apiClient.get('/protected')).resolves.toMatchObject({ data: { ok: true } });

    expect(refreshMock.history.post).toHaveLength(1);
    expect(mockSet).toHaveBeenCalledWith('workspace_access_token', 'new-token');
    expect(useAuthStore.getState().workspaceAccessToken).toBe('new-token');
    expect(apiMock.history.get[1]?.headers?.Authorization).toBe('Bearer new-token');
  });

  test('coalesces concurrent 401 responses into one refresh request', async () => {
    apiMock.onGet('/one').replyOnce(401).onGet('/one').reply(200, { id: 1 });
    apiMock.onGet('/two').replyOnce(401).onGet('/two').reply(200, { id: 2 });
    refreshMock.onPost(/\/auth\/refresh$/).reply(async () => {
      await Promise.resolve();
      return [200, { accessToken: 'shared-token' }];
    });

    const [one, two] = await Promise.all([apiClient.get('/one'), apiClient.get('/two')]);

    expect(one.data).toEqual({ id: 1 });
    expect(two.data).toEqual({ id: 2 });
    expect(refreshMock.history.post).toHaveLength(1);
    expect(useAuthStore.getState().workspaceAccessToken).toBe('shared-token');
  });

  test('clears identity and private query cache when refresh fails', async () => {
    apiMock.onGet('/protected').reply(401);
    refreshMock.onPost(/\/auth\/refresh$/).reply(401);

    await expect(apiClient.get('/protected')).rejects.toMatchObject({
      response: { status: 401 },
    });

    expect(useAuthStore.getState()).toMatchObject({
      workspaceAccessToken: null,
      personalAccessToken: null,
      sessionKind: null,
      user: null,
      consumerUser: null,
      twoFactorChallenge: null,
    });
    expect(queryClient.getQueryData(['private'])).toBeUndefined();
    expect(mockDelete).toHaveBeenCalledWith('workspace_access_token');
    expect(mockDelete).toHaveBeenCalledWith('personal_access_token');
    expect(mockDelete).toHaveBeenCalledWith('session_kind');
    expect(mockDelete).toHaveBeenCalledWith('consumer_user');
  });

  test('preserves the secure session and private cache when refresh is transiently unavailable', async () => {
    apiMock.onGet('/protected').reply(401);
    refreshMock.onPost(/\/auth\/refresh$/).reply(503);

    await expect(apiClient.get('/protected')).rejects.toMatchObject({
      response: { status: 503 },
    });

    expect(useAuthStore.getState()).toMatchObject({
      workspaceAccessToken: 'old-token',
      sessionKind: 'staff',
    });
    expect(storedWorkspaceToken).toBe('old-token');
    expect(queryClient.getQueryData(['private'])).toEqual({ tenantSecret: true });
    expect(mockDelete).not.toHaveBeenCalled();
  });

  test('does not attempt refresh for an unauthenticated login 401', async () => {
    useAuthStore.setState({
      workspaceAccessToken: null,
      personalAccessToken: null,
      sessionKind: null,
      user: null,
      consumerUser: null,
    });
    storedWorkspaceToken = null;
    apiMock.onPost('/auth/login').reply(401);

    await expect(
      apiClient.post('/auth/login', { email: 'wrong@example.com', password: 'wrong' })
    ).rejects.toMatchObject({ response: { status: 401 } });

    expect(refreshMock.history.post).toHaveLength(0);
    expect(mockDelete).not.toHaveBeenCalled();
  });

  // TASK-497 fix #3: /mobile-auth/login and /mobile-auth/register must be treated as public
  // auth requests, same as /auth/login and /consumer-auth/* — a 401 is a real "wrong
  // credentials" answer, not an expired-session signal, so it must not trigger a refresh.
  test('does not attempt refresh for an unauthenticated /mobile-auth/login 401', async () => {
    useAuthStore.setState({
      workspaceAccessToken: null,
      personalAccessToken: null,
      sessionKind: null,
      user: null,
      consumerUser: null,
    });
    storedWorkspaceToken = null;
    apiMock.onPost('/mobile-auth/login').reply(401, { error: 'Invalid credentials' });

    await expect(
      apiClient.post('/mobile-auth/login', { identifier: 'wrong@example.com', password: 'wrong' })
    ).rejects.toMatchObject({ response: { status: 401, data: { error: 'Invalid credentials' } } });

    expect(refreshMock.history.post).toHaveLength(0);
    expect(mockDelete).not.toHaveBeenCalled();
  });

  test('does not attempt refresh for an unauthenticated /mobile-auth/register 401', async () => {
    useAuthStore.setState({
      workspaceAccessToken: null,
      personalAccessToken: null,
      sessionKind: null,
      user: null,
      consumerUser: null,
    });
    storedWorkspaceToken = null;
    apiMock.onPost('/mobile-auth/register').reply(401, { error: 'Invalid credentials' });

    await expect(
      apiClient.post('/mobile-auth/register', { phone: '+380501234567', password: 'wrong', fullName: 'X' })
    ).rejects.toMatchObject({ response: { status: 401 } });

    expect(refreshMock.history.post).toHaveLength(0);
    expect(mockDelete).not.toHaveBeenCalled();
  });

  // Regression guard: an ordinary authenticated (Bearer-bearing) protected endpoint must
  // still go through the existing refresh-and-retry flow — the /mobile-auth/* allowlist
  // addition must not accidentally widen to other routes.
  test('still refreshes and retries a 401 from an ordinary protected workspace endpoint', async () => {
    apiMock.onGet('/customers').replyOnce(401).onGet('/customers').reply(200, { items: [] });
    refreshMock.onPost(/\/auth\/refresh$/).reply(200, { accessToken: 'refreshed-token' });

    await expect(apiClient.get('/customers')).resolves.toMatchObject({ data: { items: [] } });

    expect(refreshMock.history.post).toHaveLength(1);
    expect(useAuthStore.getState().workspaceAccessToken).toBe('refreshed-token');
  });

  test('does not refresh or clear the challenge after an invalid two-factor code', async () => {
    useAuthStore.getState().setTwoFactorChallenge('challenge', 'manager@example.com');
    apiMock.onPost('/auth/2fa/verify').reply(401);

    await expect(
      apiClient.post('/auth/2fa/verify', {
        challengeToken: 'challenge',
        code: '000000',
      })
    ).rejects.toMatchObject({ response: { status: 401 } });

    expect(refreshMock.history.post).toHaveLength(0);
    expect(useAuthStore.getState().twoFactorChallenge).toEqual({
      challengeToken: 'challenge',
      email: 'manager@example.com',
    });
    expect(useAuthStore.getState().workspaceAccessToken).toBe('old-token');
  });

  test('terminates the session if the retried request still returns 401', async () => {
    apiMock.onGet('/protected').reply(401);
    refreshMock.onPost(/\/auth\/refresh$/).reply(200, { accessToken: 'rejected-token' });

    await expect(apiClient.get('/protected')).rejects.toMatchObject({
      response: { status: 401 },
    });

    expect(refreshMock.history.post).toHaveLength(1);
    expect(useAuthStore.getState().workspaceAccessToken).toBeNull();
    expect(queryClient.getQueryData(['private'])).toBeUndefined();
  });

  test('does not resurrect a session when logout occurs during refresh', async () => {
    let resolveRefresh: ((response: [number, { accessToken: string }]) => void) | undefined;
    let markRefreshStarted: (() => void) | undefined;
    const refreshResponse = new Promise<[number, { accessToken: string }]>((resolve) => {
      resolveRefresh = resolve;
    });
    const refreshStarted = new Promise<void>((resolve) => {
      markRefreshStarted = resolve;
    });
    apiMock.onGet('/protected').replyOnce(401);
    refreshMock.onPost(/\/auth\/refresh$/).reply(() => {
      markRefreshStarted?.();
      return refreshResponse;
    });

    const request = apiClient.get('/protected');
    await refreshStarted;
    await terminateSession();
    resolveRefresh?.([200, { accessToken: 'late-token' }]);

    await expect(request).rejects.toMatchObject({ response: { status: 401 } });
    expect(useAuthStore.getState().workspaceAccessToken).toBeNull();
    expect(mockSet).not.toHaveBeenCalledWith('workspace_access_token', 'late-token');
  });
});

describe('personalApiClient', () => {
  beforeEach(() => {
    personalApiMock = new MockAdapter(personalApiClient);
    storedPersonalToken = 'personal-token';
    mockGet.mockImplementation(async (key) => {
      if (key === 'personal_access_token') return storedPersonalToken;
      if (key === 'workspace_access_token') return null;
      return null;
    });
  });

  afterEach(() => {
    personalApiMock.restore();
  });

  test('attaches the personal access token, never the workspace one', async () => {
    personalApiMock.onGet('/consumer/loyalty/memberships').reply(200, []);

    await personalApiClient.get('/consumer/loyalty/memberships');

    expect(personalApiMock.history.get[0]?.headers?.Authorization).toBe('Bearer personal-token');
  });

  test('does not attempt a refresh on a personal-session 401 — there is no refresh token for a consumer session', async () => {
    const refreshMockLocal = new MockAdapter(axios);
    personalApiMock.onGet('/consumer/loyalty/memberships').reply(401);

    await expect(personalApiClient.get('/consumer/loyalty/memberships')).rejects.toMatchObject({
      response: { status: 401 },
    });

    expect(refreshMockLocal.history.post).toHaveLength(0);
    refreshMockLocal.restore();
  });
});

describe('API hardening defaults', () => {
  test('sets a finite timeout on workspace and personal clients', () => {
    expect(apiClient.defaults.timeout).toBe(API_TIMEOUT_MS);
    expect(personalApiClient.defaults.timeout).toBe(API_TIMEOUT_MS);
    expect(API_TIMEOUT_MS).toBeGreaterThan(0);
  });

  test('rejects executable and protocol-relative asset URLs', () => {
    expect(resolveApiAssetUrl('javascript:alert(1)')).toBeNull();
    expect(resolveApiAssetUrl('data:text/html,unsafe')).toBeNull();
    expect(resolveApiAssetUrl('//evil.example/image.png')).toBeNull();
    expect(resolveApiAssetUrl('https://cdn.example/image.png')).toBe('https://cdn.example/image.png');
  });
});
