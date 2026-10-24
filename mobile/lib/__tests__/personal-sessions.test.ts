import axios from 'axios';
import MockAdapter from 'axios-mock-adapter';
import * as SecureStore from 'expo-secure-store';
import { personalApiClient } from '../api-client';
import { terminateSession } from '@/features/auth/session';
import { useAuthStore } from '@/features/auth/store';

jest.mock('expo-secure-store', () => ({ getItemAsync: jest.fn(), setItemAsync: jest.fn(), deleteItemAsync: jest.fn() }));
let values: Record<string, string>;
let api: MockAdapter;
let refresh: MockAdapter;

beforeEach(() => {
  values = { personal_access_token: 'old-personal', personal_refresh_token: 'old-proof', workspace_access_token: 'staff-token' };
  (SecureStore.getItemAsync as jest.Mock).mockImplementation(async (key: string) => values[key] ?? null);
  (SecureStore.setItemAsync as jest.Mock).mockImplementation(async (key: string, value: string) => { values[key] = value; });
  (SecureStore.deleteItemAsync as jest.Mock).mockImplementation(async (key: string) => { delete values[key]; });
  useAuthStore.setState({ personalAccessToken: 'old-personal', workspaceAccessToken: 'staff-token', sessionKind: 'staff' });
  api = new MockAdapter(personalApiClient); refresh = new MockAdapter(axios);
});
afterEach(() => { api.restore(); refresh.restore(); });

test('parallel personal 401s share one consumer refresh and never use the staff cookie endpoint', async () => {
  api.onGet('/wallet').reply(config => config.headers?.Authorization === 'Bearer new-personal' ? [200, {}] : [401]);
  refresh.onPost(/\/consumer-auth\/refresh$/).reply(200, { accessToken: 'new-personal', refreshToken: 'new-proof' });
  await Promise.all([personalApiClient.get('/wallet'), personalApiClient.get('/wallet')]);
  expect(refresh.history.post).toHaveLength(1);
  expect(JSON.parse(refresh.history.post[0].data)).toEqual({ refreshToken: 'old-proof' });
  expect(values.personal_refresh_token).toBe('new-proof');
  expect(values.personal_access_token).toBe('new-personal');
  expect(values.workspace_access_token).toBe('staff-token');
});

test('a rejected personal proof clears only the personal identity', async () => {
  api.onGet('/wallet').reply(401); refresh.onPost(/\/consumer-auth\/refresh$/).reply(401);
  await expect(personalApiClient.get('/wallet')).rejects.toMatchObject({ response: { status: 401 } });
  expect(values.personal_refresh_token).toBeUndefined(); expect(values.personal_access_token).toBeUndefined();
  expect(useAuthStore.getState().personalAccessToken).toBeNull();
  expect(useAuthStore.getState().workspaceAccessToken).toBe('staff-token');
});

test.each([409, 429, 503])('transient refresh status %s preserves both identities', async (status) => {
  api.onGet('/wallet').reply(401); refresh.onPost(/\/consumer-auth\/refresh$/).reply(status);
  await expect(personalApiClient.get('/wallet')).rejects.toMatchObject({ response: { status } });
  expect(values.personal_refresh_token).toBe('old-proof'); expect(values.workspace_access_token).toBe('staff-token');
});

test('logout while refresh is in flight cannot restore personal credentials', async () => {
  let complete!: (response: [number, object]) => void; let started!: () => void;
  const waiting = new Promise<[number, object]>(resolve => { complete = resolve; });
  const ready = new Promise<void>(resolve => { started = resolve; });
  api.onGet('/wallet').reply(401); refresh.onPost(/\/consumer-auth\/refresh$/).reply(() => { started(); return waiting; });
  const request = personalApiClient.get('/wallet'); await ready;
  await terminateSession(); complete([200, { accessToken: 'late-personal', refreshToken: 'late-proof' }]);
  await expect(request).rejects.toThrow('SESSION_TERMINATED_DURING_REFRESH');
  expect(values.personal_access_token).toBeUndefined(); expect(values.personal_refresh_token).toBeUndefined();
});

test('a late personal refresh cannot overwrite a newer account login', async () => {
  let complete!: (response: [number, object]) => void; let started!: () => void;
  const waiting = new Promise<[number, object]>(resolve => { complete = resolve; });
  const ready = new Promise<void>(resolve => { started = resolve; });
  api.onGet('/wallet').reply(401); refresh.onPost(/\/consumer-auth\/refresh$/).reply(() => { started(); return waiting; });
  const request = personalApiClient.get('/wallet'); await ready;
  await useAuthStore.getState().setPersonalAuth('new-account-token', { id: 'new-account', fullName: 'Synthetic', phone: '', role: 'consumer' }, 'new-account-proof');
  complete([200, { accessToken: 'late-old-token', refreshToken: 'late-old-proof' }]);
  await expect(request).rejects.toThrow('SESSION_CHANGED_DURING_REFRESH');
  expect(values.personal_access_token).toBe('new-account-token'); expect(values.personal_refresh_token).toBe('new-account-proof');
});

test('logout also wins when it starts during SecureStore persistence', async () => {
  let release!: () => void; let started!: () => void;
  const blocked = new Promise<void>(resolve => { release = resolve; }); const ready = new Promise<void>(resolve => { started = resolve; });
  (SecureStore.setItemAsync as jest.Mock).mockImplementation(async (key: string, value: string) => {
    if (key === 'personal_access_token') { started(); await blocked; } values[key] = value;
  });
  api.onGet('/wallet').reply(401); refresh.onPost(/\/consumer-auth\/refresh$/).reply(200, { accessToken: 'late', refreshToken: 'proof' });
  const request = personalApiClient.get('/wallet'); await ready;
  const logout = terminateSession(); release();
  await expect(request).rejects.toThrow('SESSION_TERMINATED_DURING_REFRESH'); await logout;
  expect(values.personal_access_token).toBeUndefined(); expect(values.personal_refresh_token).toBeUndefined();
});
