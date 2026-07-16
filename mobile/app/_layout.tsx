import '../global.css';
import { useEffect } from 'react';
import { Stack } from 'expo-router';
import { QueryClientProvider } from '@tanstack/react-query';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { queryClient } from '@/lib/query-client';
import { useAuthStore } from '@/features/auth/store';
import { getMe } from '@/features/auth/api/authApi';

export default function RootLayout() {
  const loadToken = useAuthStore((s) => s.loadToken);
  const setUser = useAuthStore((s) => s.setUser);
  const clearAuth = useAuthStore((s) => s.clearAuth);

  useEffect(() => {
    (async () => {
      await loadToken();
      // loadToken only restores the access token from SecureStore, not `user` —
      // every role-gated screen (POS tab, write-off/transfer/customer manager
      // actions, ...) reads `user` from the store, so repopulate it here on cold
      // start via the already-existing GET /auth/me. If the token is stale/expired,
      // the api-client's 401 interceptor will attempt a refresh first; if that also
      // fails, clear auth so the user lands cleanly on the login screen.
      const token = useAuthStore.getState().accessToken;
      if (token && !useAuthStore.getState().user) {
        try {
          const me = await getMe();
          setUser(me);
        } catch {
          await clearAuth();
        }
      }
    })();
  }, [loadToken, setUser, clearAuth]);

  return (
    <QueryClientProvider client={queryClient}>
      <SafeAreaProvider>
        <Stack screenOptions={{ headerShown: false }}>
          <Stack.Screen name="(auth)" />
          <Stack.Screen name="(app)" />
        </Stack>
      </SafeAreaProvider>
    </QueryClientProvider>
  );
}
