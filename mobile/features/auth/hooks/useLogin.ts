import { useMutation } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { verifyTwoFactor } from '../api/authApi';
import { useAuthStore } from '../store';

export function useVerifyTwoFactor() {
  const router = useRouter();
  const setWorkspaceAuth = useAuthStore((s) => s.setWorkspaceAuth);

  return useMutation({
    mutationFn: verifyTwoFactor,
    onSuccess: async ({ accessToken, user }) => {
      // TASK-497: 2FA verify is unchanged (still POST /api/auth/2fa/verify, still returns
      // the staff AuthUserDto shape) — but this must MERGE into the session, not clobber
      // it. setWorkspaceAuth only ever touches the workspace token/profile, so whatever
      // personalAccessToken/consumerUser the initial login step already stored (the person
      // proved their personal password before reaching this 2FA step) survives untouched.
      await setWorkspaceAuth(accessToken, user);
      router.replace('/(personal)');
    },
  });
}
