import { useEffect } from 'react';
import { Stack } from 'expo-router';
import { useAuthStore } from '@/features/auth/store';
import { usePosDraftStore } from '@/features/pos/draftStore';

export default function PosLayout() {
  const user = useAuthStore((state) => state.user);

  useEffect(() => {
    if (user?.tenantId) {
      void usePosDraftStore
        .getState()
        .bindOwner({ tenantId: user.tenantId, userId: user.id });
    }
  }, [user?.id, user?.tenantId]);

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        animation: 'slide_from_right',
      }}
    >
      <Stack.Screen name="index" />
      <Stack.Screen name="scanner" />
      <Stack.Screen name="loyalty" />
      <Stack.Screen name="payment" />
      <Stack.Screen name="receipt" />
    </Stack>
  );
}
