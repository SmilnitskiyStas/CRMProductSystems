import { useMutation } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { loginConsumer, registerConsumer } from '../api/consumerAuthApi';
import { useAuthStore } from '../store';

export function useConsumerLogin() {
  const router = useRouter();
  const setConsumerAuth = useAuthStore((s) => s.setConsumerAuth);

  return useMutation({
    mutationFn: loginConsumer,
    onSuccess: async ({ accessToken, user }) => {
      await setConsumerAuth(accessToken, user);
      router.replace('/(consumer)/wallet');
    },
  });
}

export function useConsumerRegister() {
  const router = useRouter();
  const setConsumerAuth = useAuthStore((s) => s.setConsumerAuth);

  return useMutation({
    mutationFn: registerConsumer,
    onSuccess: async ({ accessToken, user }) => {
      await setConsumerAuth(accessToken, user);
      router.replace('/(consumer)/wallet');
    },
  });
}
