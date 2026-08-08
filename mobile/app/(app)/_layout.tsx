import { Tabs, Redirect, useSegments } from 'expo-router';
import { useEffect, useRef } from 'react';
import { useNetInfo } from '@react-native-community/netinfo';
import { ActivityIndicator, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useAuthStore } from '@/features/auth/store';
import { NotificationBell } from '@/features/notifications/components/NotificationBell';
import { useModulesSettings } from '@/features/navigation/hooks';
import { navigationDecision } from '@/features/navigation/policy';
import { GuardState } from '@/features/navigation/components/GuardState';
import { AuthBootstrapState } from '@/features/auth/components/AuthBootstrapState';
import { offlineRouteAllowed, persistOfflineSessionSnapshot } from '@/features/auth/offlineSnapshot';
import { retrySessionBootstrap } from '@/features/auth/bootstrap';

export default function AppLayout() {
  const workspaceAccessToken = useAuthStore((s) => s.workspaceAccessToken);
  const personalAccessToken = useAuthStore((s) => s.personalAccessToken);
  const hydrationStatus = useAuthStore((s) => s.hydrationStatus);
  const user = useAuthStore((s) => s.user);
  const offlineSnapshot = useAuthStore((s) => s.offlineSnapshot);
  const network = useNetInfo();
  const reconnecting = useRef(false);
  const segments = useSegments();
  const tenantSession = Boolean(user?.tenantId);
  const offlineMode = hydrationStatus === 'offline_read_ready';
  const modulesQuery = useModulesSettings(Boolean(workspaceAccessToken) && tenantSession && !offlineMode);
  useEffect(() => {
    if (hydrationStatus === 'ready' && user && modulesQuery.data) {
      void persistOfflineSessionSnapshot(user, modulesQuery.data).catch(() => undefined);
    }
  }, [hydrationStatus, modulesQuery.data, user]);
  useEffect(() => {
    const online = network.isConnected === true && network.isInternetReachable !== false;
    if (!offlineMode || !online || reconnecting.current) return;
    reconnecting.current = true;
    void retrySessionBootstrap().finally(() => { reconnecting.current = false; });
  }, [network.isConnected, network.isInternetReachable, offlineMode]);

  if (hydrationStatus === 'pending') {
    return <View className="flex-1 items-center justify-center"><ActivityIndicator color="#16a34a" /></View>;
  }
  if (hydrationStatus === 'retryable_error') return <AuthBootstrapState />;
  // TASK-497: (app) is the workspace area — only a session holding a workspaceAccessToken
  // belongs here. A plain consumer (personalAccessToken only, no linked staff access) is
  // sent to their personal space instead of a `user`-less staff UI; a session with neither
  // token has never authenticated at all.
  if (!workspaceAccessToken) {
    return <Redirect href={personalAccessToken ? '/(personal)' : '/(auth)/select-role'} />;
  }

  const route = `/(app)${segments.slice(1).length ? `/${segments.slice(1).join('/')}` : ''}`;
  if (offlineMode) {
    const fallback = offlineSnapshot?.allowedRoutes[0];
    if (route === '/(app)' && fallback) return <Redirect href={fallback} />;
    if (!offlineSnapshot || !offlineRouteAllowed(route, offlineSnapshot)) {
      return <GuardState contextUnavailable />;
    }
  }
  const context = { user, settings: modulesQuery.data ?? null };
  const decision = navigationDecision(route, context);

  if (tenantSession && modulesQuery.isLoading) {
    return <View className="flex-1 items-center justify-center"><ActivityIndicator color="#16a34a" /></View>;
  }
  if (tenantSession && (modulesQuery.isError || !modulesQuery.data)) {
    return <GuardState contextUnavailable onRetry={() => void modulesQuery.refetch()} />;
  }
  if (!offlineMode && !decision.allowed) {
    return <GuardState moduleDisabled={decision.reason === 'module_disabled'} />;
  }

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
          href: offlineMode ? null : '/(app)',
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
          href: !offlineMode && navigationDecision('/(app)/stock', context).allowed ? '/(app)/stock' : null,
          title: 'Залишки',
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="layers-outline" size={size} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="scan"
        options={{
          href: !offlineMode && navigationDecision('/(app)/scan', context).allowed ? '/(app)/scan' : null,
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
          href: !offlineMode && navigationDecision('/(app)/pos', context).allowed ? '/(app)/pos' : null,
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="cash-outline" size={size} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="receipt/index"
        options={{
          href: !offlineMode && navigationDecision('/(app)/receipt', context).allowed ? '/(app)/receipt' : null,
          title: 'Прийомка',
          tabBarIcon: ({ color, size }) => (
            <Ionicons name="receipt-outline" size={size} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="more/index"
        options={{
          href: offlineMode ? null : '/(app)/more',
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
