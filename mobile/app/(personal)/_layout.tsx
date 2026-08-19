import { Ionicons } from '@expo/vector-icons';
import { Redirect, Tabs, useSegments } from 'expo-router';
import { ActivityIndicator, View, type ColorValue } from 'react-native';
import { useAuthStore } from '@/features/auth/store';
import { AuthBootstrapState } from '@/features/auth/components/AuthBootstrapState';
import { RetailShellProviders } from '@/features/mobile-config/RetailShellProviders';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import { personalRouteAllowed, resolveRetailNavigation } from '@/features/retail-navigation/policy';
import { useRetailTheme } from '@/features/theme/RetailThemeProvider';
import { MobileConfigOfflineBanner } from '@/features/mobile-config/MobileConfigOfflineBanner';

const CONFIGURABLE_SCREENS = [
  'index',
  'promotions',
  'catalog',
  'wallet',
  'coupons',
  'retailers',
  'news',
  'account',
] as const;

function PersonalTabs({ hasPersonalAccess }: { hasPersonalAccess: boolean }) {
  const theme = useRetailTheme();
  const { config } = useMobileConfig();
  const segments = useSegments();
  const navigation = resolveRetailNavigation(config.navigation, config.features, hasPersonalAccess);

  function optionsFor(item: (typeof navigation)[number]) {
    return {
      href: item.href,
      title: item.label,
      tabBarIcon: ({ color, size }: { color: ColorValue; size: number }) => (
        <Ionicons name={item.iconName} color={color} size={size} />
      ),
    };
  }

  const selectedScreens = new Set(navigation.map((item) => item.screen));
  const activeScreen = (segments as readonly string[])[1];

  if (!personalRouteAllowed(activeScreen, config.features, hasPersonalAccess, config.navigation)) {
    return <Redirect href="/(personal)" />;
  }

  // Bonuses/history are loyalty-wallet screens backed by the consumer JWT — gate on
  // personalAccessToken being present, never on staff-vs-consumer identity, so both a
  // plain consumer and a linked staff member see them (see TASK-497 product framing).
  return (
    <View className="flex-1">
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: theme.colors.primary,
        tabBarInactiveTintColor: theme.colors.textSecondary,
        tabBarStyle: { borderTopColor: theme.colors.surface, height: 60, paddingTop: 4, paddingBottom: 4 },
      }}
    >
      {navigation.map((item) => (
        <Tabs.Screen key={item.type} name={item.screen} options={optionsFor(item)} />
      ))}
      {CONFIGURABLE_SCREENS.filter((screen) => !selectedScreens.has(screen)).map((screen) => (
        <Tabs.Screen key={screen} name={screen} options={{ href: null }} />
      ))}
      <Tabs.Screen name="history" options={{ href: null }} />
      <Tabs.Screen name="news/[id]" options={{ href: null }} />
      <Tabs.Screen name="product/[id]" options={{ href: null }} />
      <Tabs.Screen name="scan" options={{ href: null }} />
      <Tabs.Screen name="retailer-onboarding" options={{ href: null }} />
    </Tabs>
    <MobileConfigOfflineBanner />
    </View>
  );
}

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

  return (
    <RetailShellProviders>
      <PersonalTabs hasPersonalAccess={personalAccessToken !== null} />
    </RetailShellProviders>
  );
}
