import { Ionicons } from '@expo/vector-icons';
import { Redirect, Tabs } from 'expo-router';
import { ActivityIndicator, View } from 'react-native';
import { useAuthStore } from '@/features/auth/store';
import { AuthBootstrapState } from '@/features/auth/components/AuthBootstrapState';

export default function PersonalLayout() {
  const personalAccessToken = useAuthStore((state) => state.personalAccessToken);
  const workspaceAccessToken = useAuthStore((state) => state.workspaceAccessToken);
  const hydrationStatus = useAuthStore((state) => state.hydrationStatus);

  if (hydrationStatus === 'pending') {
    return <View className="flex-1 items-center justify-center"><ActivityIndicator color="#16a34a" /></View>;
  }
  if (hydrationStatus === 'retryable_error') return <AuthBootstrapState />;
  // TASK-497: (personal) is reachable with EITHER identity — a plain consumer
  // (personalAccessToken only) or the legacy staff-only fallback (workspaceAccessToken
  // only, no personal ConsumerAccount yet). Only a session with neither bounces out.
  if (!personalAccessToken && !workspaceAccessToken) return <Redirect href="/(auth)/select-role" />;

  // Bonuses/history are loyalty-wallet screens backed by the consumer JWT — gate on
  // personalAccessToken being present, never on staff-vs-consumer identity, so both a
  // plain consumer and a linked staff member see them (see TASK-497 product framing).
  const hasPersonalAccess = personalAccessToken !== null;

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: '#16a34a',
        tabBarInactiveTintColor: '#9ca3af',
        tabBarStyle: { borderTopColor: '#f3f4f6', height: 60, paddingTop: 4, paddingBottom: 4 },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'Головна',
          tabBarIcon: ({ color, size }) => <Ionicons name="home-outline" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="wallet"
        options={{
          href: hasPersonalAccess ? '/(personal)/wallet' : null,
          title: 'Бонуси',
          tabBarIcon: ({ color, size }) => <Ionicons name="qr-code-outline" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="history"
        options={{
          href: hasPersonalAccess ? '/(personal)/history' : null,
          title: 'Історія',
          tabBarIcon: ({ color, size }) => <Ionicons name="time-outline" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="account"
        options={{
          title: 'Профіль',
          tabBarIcon: ({ color, size }) => <Ionicons name="person-outline" color={color} size={size} />,
        }}
      />
    </Tabs>
  );
}
