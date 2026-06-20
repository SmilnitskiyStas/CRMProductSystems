import { Tabs, Redirect } from 'expo-router';
import { View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { NotificationBell } from '@/features/notifications/components/NotificationBell';

const CASHIER_ROLES = ['Cashier', 'StoreManager', 'Director', 'Admin'];

export default function AppLayout() {
  const token = useAuthStore((s) => s.accessToken);
  const user = useAuthStore((s) => s.user);

  if (!token) return <Redirect href="/(auth)/login" />;

  const canAccessPos = user ? CASHIER_ROLES.includes(user.role) : false;

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
        name="index"
        options={{
          title: 'Дашборд',
          headerShown: true,
          headerRight: () => <NotificationBell />,
          headerStyle: { backgroundColor: '#ffffff' },
          headerShadowVisible: false,
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="home-outline" size={size} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="stock/index"
        options={{
          title: 'Залишки',
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="layers-outline" size={size} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="scan"
        options={{
          title: 'Скан',
          tabBarIcon: ({ color }) => (
            <View className="bg-primary-600 w-14 h-14 rounded-full -mt-6 items-center justify-center shadow-lg">
              <Ionicons name="scan-outline" size={28} color="white" />
            </View>
          ),
          tabBarLabel: () => null,
        }}
      />
      <Tabs.Screen
        name="pos"
        options={{
          title: 'Каса',
          href: canAccessPos ? '/(app)/pos' : null,
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="cash-outline" size={size} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="receipt/index"
        options={{
          title: 'Прийомка',
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="receipt-outline" size={size} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="more/index"
        options={{
          title: 'Ще',
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="grid-outline" size={size} color={color} />
          ),
        }}
      />
      {/* Hidden screens (no tab) */}
      <Tabs.Screen name="profile/index" options={{ href: null }} />
      <Tabs.Screen name="notifications" options={{ href: null }} />
      <Tabs.Screen name="stock/[id]" options={{ href: null }} />
      <Tabs.Screen name="receipt/[id]" options={{ href: null }} />
      <Tabs.Screen name="inventory/[zoneId]" options={{ href: null }} />
      <Tabs.Screen name="write-offs/index" options={{ href: null }} />
      <Tabs.Screen name="write-offs/[id]" options={{ href: null }} />
      <Tabs.Screen name="write-offs/create" options={{ href: null }} />
      <Tabs.Screen name="transfers/index" options={{ href: null }} />
      <Tabs.Screen name="transfers/[id]" options={{ href: null }} />
      <Tabs.Screen name="transfers/create" options={{ href: null }} />
      {/* Auto Service — stack routes (no tab) */}
      <Tabs.Screen name="auto-service/index" options={{ href: null }} />
      <Tabs.Screen name="auto-service/[id]" options={{ href: null }} />
      <Tabs.Screen name="auto-service/customers" options={{ href: null }} />
      {/* Customers — hidden routes (no tab) */}
      <Tabs.Screen name="customers/index" options={{ href: null }} />
      <Tabs.Screen name="customers/[id]" options={{ href: null }} />
      {/* Service Desk — hidden routes (no tab) */}
      <Tabs.Screen name="service-desk/index" options={{ href: null }} />
      <Tabs.Screen name="service-desk/[id]" options={{ href: null }} />
      {/* Schedules — hidden routes (no tab) */}
      <Tabs.Screen name="schedules/index" options={{ href: null }} />
      <Tabs.Screen name="schedules/[id]" options={{ href: null }} />
      {/* Marketplace — hidden routes (no tab) */}
      <Tabs.Screen name="marketplace/index" options={{ href: null }} />
      <Tabs.Screen name="marketplace/[id]" options={{ href: null }} />
      {/* Production — hidden routes (no tab) */}
      <Tabs.Screen name="production/index" options={{ href: null }} />
      <Tabs.Screen name="production/[id]" options={{ href: null }} />
      <Tabs.Screen name="production/recipes/index" options={{ href: null }} />
      {/* AI Assistant — hidden route (no tab) */}
      <Tabs.Screen name="ai-assistant" options={{ href: null }} />
    </Tabs>
  );
}
