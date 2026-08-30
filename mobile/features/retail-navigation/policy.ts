import type { ComponentProps } from 'react';
import type { Ionicons } from '@expo/vector-icons';
import type {
  RetailFeatureConfig,
  RetailFeatureKey,
  RetailNavigationIcon,
  RetailNavigationItem,
  RetailNavigationRoute,
} from '@/features/mobile-config/types';

type IoniconName = ComponentProps<typeof Ionicons>['name'];

export const navigationIconNames: Record<RetailNavigationIcon, IoniconName> = {
  home: 'home-outline',
  tag: 'pricetag-outline',
  grid: 'grid-outline',
  qr: 'qr-code-outline',
  ticket: 'ticket-outline',
  map: 'map-outline',
  news: 'newspaper-outline',
  user: 'person-outline',
};

interface RoutePolicy {
  screen: 'index' | 'promotions' | 'catalog' | 'wallet' | 'coupons' | 'retailers' | 'news' | 'account';
  href: `/(personal)${string}`;
  feature?: RetailFeatureKey;
  requiresPersonalAccess?: boolean;
}

export const retailRoutePolicies: Record<RetailNavigationRoute, RoutePolicy> = {
  home: { screen: 'index', href: '/(personal)' },
  promotions: { screen: 'promotions', href: '/(personal)/promotions', feature: 'promotions' },
  catalog: { screen: 'catalog', href: '/(personal)/catalog', feature: 'catalog' },
  loyalty: {
    screen: 'wallet',
    href: '/(personal)/wallet',
    feature: 'loyalty',
    requiresPersonalAccess: true,
  },
  coupons: { screen: 'coupons', href: '/(personal)/coupons', feature: 'coupons' },
  stores: { screen: 'retailers', href: '/(personal)/retailers' },
  news: { screen: 'news', href: '/(personal)/news', feature: 'news' },
  profile: { screen: 'account', href: '/(personal)/account' },
};

export interface ResolvedNavigationItem extends RetailNavigationItem {
  screen: RoutePolicy['screen'];
  href: RoutePolicy['href'];
  iconName: IoniconName;
}

export function resolveRetailNavigation(
  items: readonly RetailNavigationItem[],
  _features: RetailFeatureConfig,
  hasPersonalAccess: boolean
): ResolvedNavigationItem[] {
  return items.flatMap((item) => {
    const policy = retailRoutePolicies[item.type];
    if (!policy) return [];
    // Published navigation is an explicit retailer choice. Do not silently remove an item a
    // retailer placed in Navigation Builder because the separate feature map still has its
    // seed/default value. Feature flags continue to protect direct routes not present in menu.
    if (policy.requiresPersonalAccess && !hasPersonalAccess) return [];
    return [{ ...item, screen: policy.screen, href: policy.href, iconName: navigationIconNames[item.icon] }];
  });
}

const protectedPersonalScreens: Readonly<
  Record<string, { feature?: RetailFeatureKey; requiresPersonalAccess?: boolean; parentRoute?: RetailNavigationRoute }>
> = {
  promotions: { feature: 'promotions' },
  catalog: { feature: 'catalog' },
  wallet: { feature: 'loyalty', requiresPersonalAccess: true },
  // History is opened from the Transactions button inside the loyalty wallet; it cannot be
  // authored as a standalone navigation item. Treat the configured loyalty route as its parent
  // so an explicit Wallet tab remains authoritative even when the legacy feature seed is false.
  history: { feature: 'loyalty', requiresPersonalAccess: true, parentRoute: 'loyalty' },
  coupons: { feature: 'coupons' },
  news: { feature: 'news' },
};

export function personalRouteAllowed(
  screen: string | undefined,
  features: RetailFeatureConfig,
  hasPersonalAccess: boolean,
  navigation: readonly RetailNavigationItem[] = []
): boolean {
  if (!screen) return true;
  const requirement = protectedPersonalScreens[screen];
  if (!requirement) return true;
  const configuredScreen = navigation.some((item) =>
    retailRoutePolicies[item.type]?.screen === screen || item.type === requirement.parentRoute
  );
  if (!configuredScreen && requirement.feature && !features[requirement.feature]) return false;
  return !requirement.requiresPersonalAccess || hasPersonalAccess;
}
