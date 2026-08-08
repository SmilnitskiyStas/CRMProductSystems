import * as SecureStore from 'expo-secure-store';
import { useAuthStore } from '../store';
import type { AuthUser, ConsumerUser } from '../types';

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

const staffUser: AuthUser = {
  id: 'staff-1',
  email: 'staff@example.com',
  fullName: 'Staff User',
  role: 'store_manager',
  tenantId: 'tenant-1',
  locationId: 'location-1',
  permissions: {},
  capabilities: [],
  tabs: [],
};

const consumerUser: ConsumerUser = {
  id: 'consumer-1',
  fullName: 'Consumer User',
  phone: '+380991234567',
  role: 'consumer',
};

function resetStore() {
  useAuthStore.setState({
    hydrationStatus: 'pending',
    sessionKind: null,
    personalAccessToken: null,
    workspaceAccessToken: null,
    user: null,
    consumerUser: null,
    twoFactorChallenge: null,
  });
}

describe('useAuthStore', () => {
  beforeEach(() => {
    resetStore();
    mockDelete.mockResolvedValue();
    mockGet.mockResolvedValue(null);
    mockSet.mockResolvedValue();
  });

  test('persists and activates a workspace session', async () => {
    useAuthStore.getState().setTwoFactorChallenge('challenge', staffUser.email);
    await useAuthStore.getState().setWorkspaceAuth('staff-token', staffUser);

    expect(mockSet).toHaveBeenCalledWith('workspace_access_token', 'staff-token');
    expect(mockSet).toHaveBeenCalledWith('session_kind', 'staff');
    expect(useAuthStore.getState()).toMatchObject({
      workspaceAccessToken: 'staff-token',
      personalAccessToken: null,
      sessionKind: 'staff',
      user: staffUser,
      consumerUser: null,
      twoFactorChallenge: null,
    });
  });

  test('persists and activates a personal session, leaving sessionKind as consumer', async () => {
    await useAuthStore.getState().setPersonalAuth('consumer-token', consumerUser);

    expect(mockSet).toHaveBeenCalledWith('personal_access_token', 'consumer-token');
    expect(mockSet).toHaveBeenCalledWith('consumer_user', JSON.stringify(consumerUser));
    expect(mockSet).toHaveBeenCalledWith('session_kind', 'consumer');
    expect(useAuthStore.getState()).toMatchObject({
      personalAccessToken: 'consumer-token',
      workspaceAccessToken: null,
      sessionKind: 'consumer',
      consumerUser,
    });
  });

  test('a linked staff member holds both tokens at once, sessionKind stays staff', async () => {
    await useAuthStore.getState().setWorkspaceAuth('staff-token', staffUser);
    await useAuthStore.getState().setPersonalAuth('consumer-token', consumerUser);

    // Setting the personal identity must not clobber the already-established workspace one.
    expect(useAuthStore.getState()).toMatchObject({
      workspaceAccessToken: 'staff-token',
      personalAccessToken: 'consumer-token',
      sessionKind: 'staff',
      user: staffUser,
      consumerUser,
    });
    // Once a workspace session exists, the persisted session_kind must not be downgraded.
    expect(mockSet).not.toHaveBeenCalledWith('session_kind', 'consumer');
  });

  test('setPersonalToken grants personal access with no profile yet (mid-2FA-challenge case)', async () => {
    await useAuthStore.getState().setPersonalToken('mid-challenge-token');

    expect(mockSet).toHaveBeenCalledWith('personal_access_token', 'mid-challenge-token');
    expect(useAuthStore.getState()).toMatchObject({
      personalAccessToken: 'mid-challenge-token',
      consumerUser: null,
    });
  });

  test('keeps a two-factor challenge in memory only', () => {
    useAuthStore.getState().setTwoFactorChallenge('challenge', staffUser.email);

    expect(useAuthStore.getState().twoFactorChallenge).toEqual({
      challengeToken: 'challenge',
      email: staffUser.email,
    });
    expect(mockSet).not.toHaveBeenCalled();

    useAuthStore.getState().clearTwoFactorChallenge();
    expect(useAuthStore.getState().twoFactorChallenge).toBeNull();
  });

  test('restores a persisted workspace-only session', async () => {
    mockGet.mockImplementation(async (key) => {
      if (key === 'workspace_access_token') return 'staff-token';
      if (key === 'session_kind') return 'staff';
      return null;
    });

    await useAuthStore.getState().loadToken();

    expect(useAuthStore.getState()).toMatchObject({
      workspaceAccessToken: 'staff-token',
      personalAccessToken: null,
      sessionKind: 'staff',
      user: null,
    });
  });

  test('restores a persisted consumer session and profile snapshot', async () => {
    mockGet.mockImplementation(async (key) => {
      if (key === 'personal_access_token') return 'consumer-token';
      if (key === 'session_kind') return 'consumer';
      if (key === 'consumer_user') return JSON.stringify(consumerUser);
      return null;
    });

    await useAuthStore.getState().loadToken();

    expect(useAuthStore.getState()).toMatchObject({
      personalAccessToken: 'consumer-token',
      workspaceAccessToken: null,
      sessionKind: 'consumer',
      consumerUser,
      user: null,
    });
  });

  test('restores a session holding both tokens', async () => {
    mockGet.mockImplementation(async (key) => {
      if (key === 'personal_access_token') return 'consumer-token';
      if (key === 'workspace_access_token') return 'staff-token';
      if (key === 'session_kind') return 'staff';
      if (key === 'consumer_user') return JSON.stringify(consumerUser);
      return null;
    });

    await useAuthStore.getState().loadToken();

    expect(useAuthStore.getState()).toMatchObject({
      personalAccessToken: 'consumer-token',
      workspaceAccessToken: 'staff-token',
      sessionKind: 'staff',
      consumerUser,
      user: null,
    });
  });

  test('does nothing when no session was ever persisted', async () => {
    await useAuthStore.getState().loadToken();

    expect(useAuthStore.getState()).toMatchObject({
      personalAccessToken: null,
      workspaceAccessToken: null,
      sessionKind: null,
    });
  });

  test('clears every persisted session value and in-memory identity', async () => {
    useAuthStore.setState({
      sessionKind: 'consumer',
      personalAccessToken: 'consumer-token',
      workspaceAccessToken: null,
      user: null,
      consumerUser,
      twoFactorChallenge: { challengeToken: 'challenge', email: staffUser.email },
    });

    await useAuthStore.getState().clearAuth();

    expect(mockDelete).toHaveBeenCalledWith('personal_access_token');
    expect(mockDelete).toHaveBeenCalledWith('workspace_access_token');
    expect(mockDelete).toHaveBeenCalledWith('session_kind');
    expect(mockDelete).toHaveBeenCalledWith('consumer_user');
    expect(useAuthStore.getState()).toMatchObject({
      personalAccessToken: null,
      workspaceAccessToken: null,
      sessionKind: null,
      user: null,
      consumerUser: null,
      twoFactorChallenge: null,
    });
  });

  test('clears in-memory identity even when SecureStore deletion partially fails', async () => {
    useAuthStore.setState({
      sessionKind: 'staff',
      personalAccessToken: null,
      workspaceAccessToken: 'staff-token',
      user: staffUser,
      consumerUser: null,
      twoFactorChallenge: { challengeToken: 'challenge', email: staffUser.email },
    });
    mockDelete.mockRejectedValueOnce(new Error('keystore unavailable'));

    await expect(useAuthStore.getState().clearAuth()).resolves.toBeUndefined();

    expect(mockDelete).toHaveBeenCalledTimes(4);
    expect(useAuthStore.getState()).toMatchObject({
      personalAccessToken: null,
      workspaceAccessToken: null,
      sessionKind: null,
      user: null,
      consumerUser: null,
      twoFactorChallenge: null,
    });
  });
});
