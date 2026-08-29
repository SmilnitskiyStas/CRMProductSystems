import { Ionicons } from '@expo/vector-icons';
import { useEffect, useRef, useState } from 'react';
import { Redirect, Tabs, useSegments } from 'expo-router';
import { AccessibilityInfo, ActivityIndicator, Animated, View, type ColorValue } from 'react-native';
import Svg, { Path } from 'react-native-svg';
import { useAuthStore } from '@/features/auth/store';
import { AuthBootstrapState } from '@/features/auth/components/AuthBootstrapState';
import { RetailShellProviders } from '@/features/mobile-config/RetailShellProviders';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import { personalRouteAllowed, resolveRetailNavigation, type ResolvedNavigationItem } from '@/features/retail-navigation/policy';
import { useRetailTheme } from '@/features/theme/RetailThemeProvider';
import { MobileConfigOfflineBanner } from '@/features/mobile-config/MobileConfigOfflineBanner';
import { MobileAppLoadingScreen } from '@/features/mobile-config/MobileAppLoadingScreen';
import { useActiveTenant } from '@/features/tenant/ActiveTenantProvider';
import { useMemberships } from '@/features/loyalty/hooks/useLoyalty';

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

function PrimaryNavigationIcon({ item, navigationBackground, fallbackColor }: { item: ResolvedNavigationItem; navigationBackground: string; fallbackColor: string }) {
  const pulse = useRef(new Animated.Value(0)).current;
  const [reduceMotion, setReduceMotion] = useState(false);
  const duration = item.primaryGlowSpeed === 'slow' ? 2600 : item.primaryGlowSpeed === 'fast' ? 900 : 1600;

  useEffect(() => {
    void AccessibilityInfo.isReduceMotionEnabled().then(setReduceMotion);
    const subscription = AccessibilityInfo.addEventListener('reduceMotionChanged', setReduceMotion);
    return () => subscription.remove();
  }, []);

  useEffect(() => {
    pulse.stopAnimation();
    pulse.setValue(0);
    if (!item.primaryGlow || !item.primaryGlowAnimated || reduceMotion) return;
    const animation = Animated.loop(Animated.sequence([
      Animated.timing(pulse, { toValue: 1, duration: duration / 2, useNativeDriver: false }),
      Animated.timing(pulse, { toValue: 0, duration: duration / 2, useNativeDriver: false }),
    ]));
    animation.start();
    return () => animation.stop();
  }, [duration, item.primaryGlow, item.primaryGlowAnimated, pulse, reduceMotion]);

  const buttonSize = item.primarySize === 'xlarge' ? 68 : 58;
  const buttonColor = item.primaryColor ?? fallbackColor;
  return (
    <View style={{ width: buttonSize, height: buttonSize, alignItems: 'center', justifyContent: 'center', transform: [{ translateY: item.primaryRaised === false ? 0 : -10 }] }}>
      <Animated.View style={{
        width: buttonSize,
        height: buttonSize,
        borderRadius: buttonSize / 2,
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: buttonColor,
        borderWidth: 4,
        borderColor: navigationBackground,
        shadowColor: buttonColor,
        shadowOpacity: item.primaryGlow && item.primaryGlowAnimated && !reduceMotion ? pulse.interpolate({ inputRange: [0, 1], outputRange: [0.32, 0.88] }) : item.primaryGlow ? 0.75 : 0.28,
        shadowRadius: item.primaryGlow ? 13 : 6,
        shadowOffset: { width: 0, height: 4 },
        elevation: item.primaryGlow ? 12 : 7,
      }}>
        <Ionicons name={item.iconName} color="#FFFFFF" size={item.primarySize === 'xlarge' ? 29 : 25} />
      </Animated.View>
    </View>
  );
}

function PersonalTabs({ hasPersonalAccess }: { hasPersonalAccess: boolean }) {
  const theme = useRetailTheme();
  const { config, status } = useMobileConfig();
  const activeTenant = useActiveTenant();
  const memberships = useMemberships(hasPersonalAccess);
  const segments = useSegments();
  const navigation = resolveRetailNavigation(config.navigation, config.features, hasPersonalAccess);
  const contourIndex = navigation.findIndex((item) => item.isPrimary && item.primaryStyle === 'raisedContour');
  const contourCenter = contourIndex >= 0 ? ((contourIndex + 0.5) / navigation.length) * 1000 : 500;
  const primaryNavigationItem = navigation.find((item) => item.isPrimary);
  const navigationBackground = primaryNavigationItem?.primaryBarColor ?? theme.colors.surface;
  const contourIsRaised = primaryNavigationItem?.primaryRaised !== false;
  const contourSvgTop = contourIsRaised ? -25 : -12;
  const contourSvgHeight = contourIsRaised ? 27 : 14;

  function optionsFor(item: (typeof navigation)[number]) {
    return {
      href: item.href,
      title: item.label,
      tabBarIcon: ({ color, size }: { color: ColorValue; size: number }) => item.isPrimary ? (
        <PrimaryNavigationIcon item={item} navigationBackground={navigationBackground} fallbackColor={theme.colors.primary} />
      ) : <Ionicons name={item.iconName} color={color} size={size} />,
      ...(item.isPrimary ? {
        tabBarLabelStyle: { color: item.primaryColor ?? theme.colors.primary, fontWeight: '700' as const },
        tabBarIconStyle: { overflow: 'visible' as const },
      } : {}),
    };
  }

  const selectedScreens = new Set(navigation.map((item) => item.screen));
  const activeScreen = (segments as readonly string[])[1];

  if (activeTenant.hydrationStatus !== 'ready' || (hasPersonalAccess && memberships.isLoading) || status === 'loading') {
    return <MobileAppLoadingScreen />;
  }

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
        tabBarStyle: { backgroundColor: navigationBackground, borderTopColor: navigationBackground, borderTopWidth: contourIndex >= 0 ? 0 : 1, height: navigation.some((item) => item.isPrimary) && contourIsRaised ? 72 : 60, paddingTop: 4, paddingBottom: 4, overflow: 'visible' },
        ...(contourIndex >= 0 ? {
          tabBarBackground: () => (
            <View style={{ position: 'absolute', top: 0, right: 0, bottom: 0, left: 0, backgroundColor: navigationBackground }}>
              <Svg width="100%" height={contourSvgHeight} viewBox="0 0 1000 52" preserveAspectRatio="none" style={{ position: 'absolute', top: contourSvgTop, left: 0 }}>
                <Path
                  d={`M 0 50 H ${contourCenter - 92} C ${contourCenter - 58} 50, ${contourCenter - 58} 2, ${contourCenter} 2 C ${contourCenter + 58} 2, ${contourCenter + 58} 50, ${contourCenter + 92} 50 H 1000 V 70 H 0 Z`}
                  fill={navigationBackground}
                  stroke="none"
                />
                <Path
                  d={`M 0 50 H ${contourCenter - 92} C ${contourCenter - 58} 50, ${contourCenter - 58} 2, ${contourCenter} 2 C ${contourCenter + 58} 2, ${contourCenter + 58} 50, ${contourCenter + 92} 50 H 1000`}
                  fill="none"
                  stroke={theme.colors.textSecondary}
                  strokeWidth={2}
                  vectorEffect="non-scaling-stroke"
                />
              </Svg>
            </View>
          ),
        } : {}),
      }}
    >
      {navigation.map((item) => (
        <Tabs.Screen key={item.type} name={item.screen} options={optionsFor(item)} />
      ))}
      {CONFIGURABLE_SCREENS.filter((screen) => !selectedScreens.has(screen)).map((screen) => (
        <Tabs.Screen key={screen} name={screen} options={{ href: null }} />
      ))}
      <Tabs.Screen name="history" options={{ href: null }} />
      <Tabs.Screen name="tier-progress" options={{ href: null }} />
      <Tabs.Screen name="support/index" options={{ href: null }} />
      <Tabs.Screen name="support/[id]" options={{ href: null }} />
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
