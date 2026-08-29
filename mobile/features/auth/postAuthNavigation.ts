export type AuthLandingRoute = '/(app)' | '/(personal)';

interface AuthAccessResult {
  workspaceAccessToken: string | null;
  canAccessWorkspace: boolean;
}

/**
 * Workspace access is valid only when the backend explicitly grants it and supplies the
 * matching staff token. A personal token may coexist with it, but must never downgrade a
 * linked employee to the consumer shell.
 */
export function resolveAuthLandingRoute(result: AuthAccessResult): AuthLandingRoute {
  return result.canAccessWorkspace && Boolean(result.workspaceAccessToken)
    ? '/(app)'
    : '/(personal)';
}
