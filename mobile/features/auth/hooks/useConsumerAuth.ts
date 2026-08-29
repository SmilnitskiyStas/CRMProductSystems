import { useMutation } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { mobileRegister } from '../api/mobileAuthApi';
import { useAuthStore } from '../store';
import { resolveAuthLandingRoute } from '../postAuthNavigation';

export function useConsumerRegister() {
  const router = useRouter();
  const setWorkspaceAuth = useAuthStore((s) => s.setWorkspaceAuth);
  const setPersonalAuth = useAuthStore((s) => s.setPersonalAuth);
  const setPersonalToken = useAuthStore((s) => s.setPersonalToken);
  const setTwoFactorChallenge = useAuthStore((s) => s.setTwoFactorChallenge);

  return useMutation({
    mutationFn: mobileRegister,
    onSuccess: async (result, variables) => {
      if ('requiresTwoFactor' in result) {
        // TASK-497: the person already proved their personal password before the 2FA
        // branch — unlock personal/loyalty mode immediately while the workspace 2FA step
        // is still pending. Absent only in the legacy staff-only-fallback-with-2FA case.
        if (result.personalAccessToken) {
          await setPersonalToken(result.personalAccessToken);
        }
        setTwoFactorChallenge(result.challengeToken, variables.email ?? variables.phone);
        router.push('/(auth)/two-factor');
        return;
      }

      // Both identities are independent and additive — a linked staff member gets both.
      if (result.workspaceAccessToken) {
        await setWorkspaceAuth(result.workspaceAccessToken, {
          id: result.user.id,
          email: result.user.email ?? variables.email ?? '',
          fullName: result.user.fullName,
          role: result.access.role,
          tenantId: result.user.tenantId,
          locationId: result.user.storeId,
          permissions: result.access.permissions,
          capabilities: result.access.capabilities,
          tabs: result.access.tabs,
        });
      }
      if (result.personalAccessToken) {
        await setPersonalAuth(result.personalAccessToken, {
          id: result.user.id,
          fullName: result.user.fullName,
          phone: result.user.phone ?? variables.phone,
          role: 'consumer',
        });
      }
      router.replace(resolveAuthLandingRoute({
        workspaceAccessToken: result.workspaceAccessToken,
        canAccessWorkspace: result.access.canAccessWorkspace,
      }));
    },
  });
}
