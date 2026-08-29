import { resolveAuthLandingRoute } from '../postAuthNavigation';

describe('resolveAuthLandingRoute', () => {
  test('opens workspace for a linked employee', () => {
    expect(resolveAuthLandingRoute({
      workspaceAccessToken: 'workspace-token',
      canAccessWorkspace: true,
    })).toBe('/(app)');
  });

  test('opens workspace for a legacy staff-only account', () => {
    expect(resolveAuthLandingRoute({
      workspaceAccessToken: 'workspace-token',
      canAccessWorkspace: true,
    })).toBe('/(app)');
  });

  test('keeps a consumer in the personal shell', () => {
    expect(resolveAuthLandingRoute({
      workspaceAccessToken: null,
      canAccessWorkspace: false,
    })).toBe('/(personal)');
  });

  test('fails closed when backend flag and token disagree', () => {
    expect(resolveAuthLandingRoute({
      workspaceAccessToken: 'unexpected-token',
      canAccessWorkspace: false,
    })).toBe('/(personal)');
    expect(resolveAuthLandingRoute({
      workspaceAccessToken: null,
      canAccessWorkspace: true,
    })).toBe('/(personal)');
  });
});
