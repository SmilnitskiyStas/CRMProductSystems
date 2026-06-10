import { useMutation } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { login } from '../api/authApi';
import { useAuthStore } from '../store';

export function useLogin() {
  const router = useRouter();
  const setAuth = useAuthStore((s) => s.setAuth);

  return useMutation({
    mutationFn: login,
    onSuccess: async ({ accessToken, user }) => {
      await setAuth(accessToken, user);
      router.replace('/(app)');
    },
  });
}
