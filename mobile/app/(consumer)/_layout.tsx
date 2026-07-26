import { Tabs, Redirect } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';

/**
 * Consumer loyalty-wallet tabs (TASK-405/407) — parallel to (app)'s staff tabs, never
 * rendered for a staff session. Three tabs per the plan: wallet (QR + balance), history
 * (ledger), account (profile/logout).
 */
export default function ConsumerLayout() {
  const token = useAuthStore((s) => s.accessToken);
  const sessionKind = useAuthStore((s) => s.sessionKind);

  if (!token) return <Redirect href="/(auth)/select-role" />;
  // A staff session landing here (stale deep link, old Redirect target, ...) belongs in the
  // staff tabs — mirrors the opposite guard in (app)/_layout.tsx.
  if (sessionKind === 'staff') return <Redirect href="/(app)" />;

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: '#16a34a',
        tabBarInactiveTintColor: '#9ca3af',
        tabBarStyle: {
          borderTopWidth: 1,
          borderTopColor: '#f3f4f6',
          paddingTop: 4,
          paddingBottom: 4,
          height: 60,
        },
      }}
    >
      <Tabs.Screen
        name="wallet"
        options={{
          title: 'Гаманець',
          tabBarIcon: ({ color, size }) => <Ionicons name="qr-code-outline" size={size} color={color} />,
        }}
      />
      <Tabs.Screen
        name="history"
        options={{
          title: 'Історія',
          tabBarIcon: ({ color, size }) => <Ionicons name="time-outline" size={size} color={color} />,
        }}
      />
      <Tabs.Screen
        name="account"
        options={{
          title: 'Профіль',
          tabBarIcon: ({ color, size }) => <Ionicons name="person-outline" size={size} color={color} />,
        }}
      />
    </Tabs>
  );
}
