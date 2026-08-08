import { runSessionBootstrap, type SessionBootstrapDependencies } from '../bootstrap';
import type { AuthUser, ConsumerUser } from '../types';

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
  fullName: 'Consumer',
  phone: '+380000000000',
  role: 'consumer',
};

function dependencies(
  overrides: Partial<SessionBootstrapDependencies> = {}
): SessionBootstrapDependencies {
  return {
    loadPersistedSession: jest.fn().mockResolvedValue(undefined),
    getState: jest.fn(() => ({
      personalAccessToken: null,
      workspaceAccessToken: null,
      user: null,
      consumerUser: null,
    })),
    setStaffUser: jest.fn(),
    fetchStaffUser: jest.fn().mockResolvedValue(staffUser),
    terminate: jest.fn().mockResolvedValue(undefined),
    finishHydration: jest.fn(),
    markRetryable: jest.fn(),
    clearPrivateCache: jest.fn(),
    hydratePrivateCache: jest.fn().mockResolvedValue(undefined),
    restoreOfflineReads: jest.fn().mockResolvedValue('missing'),
    ...overrides,
  };
}

describe('runSessionBootstrap', () => {
  it('does not finish hydration before delayed persisted-token loading completes', async () => {
    let release!: () => void;
    const delayedLoad = new Promise<void>((resolve) => {
      release = resolve;
    });
    const deps = dependencies({ loadPersistedSession: jest.fn(() => delayedLoad) });

    const bootstrap = runSessionBootstrap(deps);
    await Promise.resolve();
    expect(deps.finishHydration).not.toHaveBeenCalled();

    release();
    await bootstrap;
    expect(deps.finishHydration).toHaveBeenCalledTimes(1);
  });

  it('waits for delayed /auth/me before exposing a restored staff session', async () => {
    let resolveUser!: (user: AuthUser) => void;
    const delayedUser = new Promise<AuthUser>((resolve) => {
      resolveUser = resolve;
    });
    const deps = dependencies({
      getState: jest.fn(() => ({
        personalAccessToken: null,
        workspaceAccessToken: 'persisted-token',
        user: null,
        consumerUser: null,
      })),
      fetchStaffUser: jest.fn(() => delayedUser),
    });

    const bootstrap = runSessionBootstrap(deps);
    await Promise.resolve();
    expect(deps.finishHydration).not.toHaveBeenCalled();

    resolveUser(staffUser);
    await bootstrap;
    expect(deps.hydratePrivateCache).toHaveBeenCalledWith(staffUser);
    expect(deps.setStaffUser).toHaveBeenCalledWith(staffUser);
    expect(deps.terminate).not.toHaveBeenCalled();
    expect(deps.finishHydration).toHaveBeenCalledTimes(1);
  });

  it('restores a valid consumer-only session without calling staff /auth/me', async () => {
    const deps = dependencies({
      getState: jest.fn(() => ({
        personalAccessToken: 'consumer-token',
        workspaceAccessToken: null,
        user: null,
        consumerUser,
      })),
    });

    await runSessionBootstrap(deps);
    expect(deps.fetchStaffUser).not.toHaveBeenCalled();
    expect(deps.terminate).not.toHaveBeenCalled();
    expect(deps.finishHydration).toHaveBeenCalledTimes(1);
  });

  it('restores a session holding both tokens, validating only the workspace half', async () => {
    const deps = dependencies({
      getState: jest.fn(() => ({
        personalAccessToken: 'consumer-token',
        workspaceAccessToken: 'persisted-token',
        user: null,
        consumerUser,
      })),
    });

    await runSessionBootstrap(deps);
    expect(deps.fetchStaffUser).toHaveBeenCalledTimes(1);
    expect(deps.setStaffUser).toHaveBeenCalledWith(staffUser);
    expect(deps.terminate).not.toHaveBeenCalled();
    expect(deps.finishHydration).toHaveBeenCalledTimes(1);
  });

  it('terminates a session whose personal token has no restorable profile snapshot', async () => {
    const deps = dependencies({
      getState: jest.fn(() => ({
        personalAccessToken: 'consumer-token',
        workspaceAccessToken: null,
        user: null,
        consumerUser: null,
      })),
    });

    await runSessionBootstrap(deps);
    expect(deps.fetchStaffUser).not.toHaveBeenCalled();
    expect(deps.terminate).toHaveBeenCalledTimes(1);
    expect(deps.finishHydration).toHaveBeenCalledTimes(1);
  });

  it('terminates an invalid restored staff session before finishing hydration', async () => {
    const deps = dependencies({
      getState: jest.fn(() => ({
        personalAccessToken: null,
        workspaceAccessToken: 'expired-token',
        user: null,
        consumerUser: null,
      })),
      fetchStaffUser: jest.fn().mockRejectedValue({ response: { status: 401 } }),
    });

    await runSessionBootstrap(deps);
    expect(deps.terminate).toHaveBeenCalledTimes(1);
    expect(deps.setStaffUser).not.toHaveBeenCalled();
    expect(deps.finishHydration).toHaveBeenCalledTimes(1);
    expect(deps.markRetryable).not.toHaveBeenCalled();
  });

  it('keeps a restored session retryable when /auth/me is offline, then recovers', async () => {
    const state = {
      personalAccessToken: null as string | null,
      workspaceAccessToken: 'persisted-token',
      user: null as AuthUser | null,
      consumerUser: null as ConsumerUser | null,
    };
    const deps = dependencies({
      getState: jest.fn(() => state),
      fetchStaffUser: jest
        .fn()
        .mockRejectedValueOnce(new Error('Network Error'))
        .mockResolvedValue(staffUser),
      setStaffUser: jest.fn((user: AuthUser) => {
        state.user = user;
      }),
    });

    await runSessionBootstrap(deps);
    expect(deps.terminate).not.toHaveBeenCalled();
    expect(deps.markRetryable).toHaveBeenCalledTimes(1);
    expect(deps.clearPrivateCache).toHaveBeenCalledTimes(1);
    expect(deps.finishHydration).not.toHaveBeenCalled();

    await runSessionBootstrap(deps);
    expect(deps.setStaffUser).toHaveBeenCalledWith(staffUser);
    expect(deps.finishHydration).toHaveBeenCalledTimes(1);
  });

  it('enters the bounded offline-read shell after process death when a verified cache is restored', async () => {
    const deps = dependencies({
      getState: jest.fn(() => ({
        personalAccessToken: null, workspaceAccessToken: 'persisted-token',
        user: null, consumerUser: null,
      })),
      fetchStaffUser: jest.fn().mockRejectedValue(new Error('Network Error')),
      restoreOfflineReads: jest.fn().mockResolvedValue('ready'),
    });
    await runSessionBootstrap(deps);
    expect(deps.restoreOfflineReads).toHaveBeenCalledTimes(1);
    expect(deps.markRetryable).not.toHaveBeenCalled();
    expect(deps.finishHydration).not.toHaveBeenCalled();
    expect(deps.terminate).not.toHaveBeenCalled();
  });

  it('terminates a restored session when its offline snapshot is invalid', async () => {
    const deps = dependencies({
      getState: jest.fn(() => ({
        personalAccessToken: null, workspaceAccessToken: 'persisted-token',
        user: null, consumerUser: null,
      })),
      fetchStaffUser: jest.fn().mockRejectedValue(new Error('Network Error')),
      restoreOfflineReads: jest.fn().mockResolvedValue('invalid'),
    });
    await runSessionBootstrap(deps);
    expect(deps.terminate).toHaveBeenCalledTimes(1);
    expect(deps.finishHydration).toHaveBeenCalledTimes(1);
    expect(deps.markRetryable).not.toHaveBeenCalled();
  });

  it('does not expose ready state until terminal cleanup finishes', async () => {
    let finishTermination!: () => void;
    const termination = new Promise<void>((resolve) => {
      finishTermination = resolve;
    });
    const deps = dependencies({
      getState: jest.fn(() => ({
        personalAccessToken: null,
        workspaceAccessToken: 'expired-token',
        user: null,
        consumerUser: null,
      })),
      fetchStaffUser: jest.fn().mockRejectedValue({ response: { status: 403 } }),
      terminate: jest.fn(() => termination),
    });

    const bootstrap = runSessionBootstrap(deps);
    await Promise.resolve();
    await Promise.resolve();
    expect(deps.finishHydration).not.toHaveBeenCalled();

    finishTermination();
    await bootstrap;
    expect(deps.finishHydration).toHaveBeenCalledTimes(1);
  });

  it('finishes force-stop-equivalent initialization with no persisted session', async () => {
    const deps = dependencies();

    await runSessionBootstrap(deps);
    expect(deps.fetchStaffUser).not.toHaveBeenCalled();
    expect(deps.terminate).not.toHaveBeenCalled();
    expect(deps.finishHydration).toHaveBeenCalledTimes(1);
  });
});
