import '../global.css';
import { useEffect } from 'react';
import { Stack } from 'expo-router';
import { QueryClientProvider } from '@tanstack/react-query';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { queryClient } from '@/lib/query-client';
import { bootstrapSession } from '@/features/auth/bootstrap';

export default function RootLayout() {
  useEffect(() => {
    void bootstrapSession();
  }, []);

  return (
    <QueryClientProvider client={queryClient}>
      <SafeAreaProvider>
        <Stack screenOptions={{ headerShown: false }}>
          <Stack.Screen name="(auth)" />
          <Stack.Screen name="(personal)" />
          <Stack.Screen name="(app)" />
          <Stack.Screen name="join/[slug]" />
        </Stack>
      </SafeAreaProvider>
    </QueryClientProvider>
  );
}
