import { Redirect, Stack } from 'expo-router';
import { ActivityIndicator, View } from 'react-native';
import { useAuthStore } from '@/features/auth/store';
import { AuthBootstrapState } from '@/features/auth/components/AuthBootstrapState';

export default function AuthLayout() {
  const hydrationStatus = useAuthStore((state) => state.hydrationStatus);
  const personalAccessToken = useAuthStore((state) => state.personalAccessToken);
  const workspaceAccessToken = useAuthStore((state) => state.workspaceAccessToken);
  const twoFactorChallenge = useAuthStore((state) => state.twoFactorChallenge);

  if (hydrationStatus === 'pending') {
    return (
      <View className="flex-1 items-center justify-center bg-white">
        <ActivityIndicator color="#16a34a" />
      </View>
    );
  }
  if (hydrationStatus === 'retryable_error') return <AuthBootstrapState />;
  // TASK-497: either token means the person already has a usable session (personal-only,
  // workspace-only legacy fallback, or both) — send them straight to /(personal). EXCEPT
  // while a 2FA challenge is pending: a challenge reached via the consumer/mobile-auth path
  // already stores personalAccessToken (see useMobileLogin/useConsumerRegister) so the
  // person can use personal/loyalty mode while finishing the workspace 2FA step — that step
  // is the two-factor screen, which itself lives inside this (auth) stack, so it must stay
  // reachable instead of being redirected away the instant the personal token lands.
  if (!twoFactorChallenge && (personalAccessToken || workspaceAccessToken)) {
    return <Redirect href="/(personal)" />;
  }

  return (
    <Stack initialRouteName="select-role" screenOptions={{ headerShown: false }}>
      <Stack.Screen name="select-role" />
      <Stack.Screen name="two-factor" />
      <Stack.Screen name="consumer-login" />
      <Stack.Screen name="consumer-register" />
    </Stack>
  );
}
