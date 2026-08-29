import type { RetailTenant } from '@/features/tenant/types';

export type RetailFeatureKey =
  | 'loyalty'
  | 'promotions'
  | 'catalog'
  | 'coupons'
  | 'news'
  | 'receipts'
  | 'delivery'
  | 'personalOffers';

export type RetailFeatureConfig = Record<RetailFeatureKey, boolean>;

export type RetailNavigationRoute =
  | 'home'
  | 'promotions'
  | 'catalog'
  | 'loyalty'
  | 'coupons'
  | 'stores'
  | 'news'
  | 'profile';

export type RetailNavigationIcon =
  | 'home'
  | 'tag'
  | 'grid'
  | 'qr'
  | 'ticket'
  | 'map'
  | 'news'
  | 'user';

export interface RetailNavigationItem {
  type: RetailNavigationRoute;
  label: string;
  icon: RetailNavigationIcon;
  isPrimary?: boolean;
  primaryColor?: string;
  primaryBarColor?: string;
  primarySize?: 'large' | 'xlarge';
  primaryStyle?: 'floating' | 'raisedContour';
  primaryRaised?: boolean;
  primaryGlow?: boolean;
  primaryGlowAnimated?: boolean;
  primaryGlowSpeed?: 'slow' | 'normal' | 'fast';
}

export interface RetailThemeConfig {
  logoUrl?: string | null;
  colors: {
    primary: string;
    secondary: string;
    background: string;
    surface: string;
    textPrimary: string;
    textSecondary: string;
  };
  buttons: { radius: number };
  cards: { radius: number };
  spacing: 'compact' | 'comfortable';
}

export interface MobileBlockConfig<TProps = unknown> {
  id: string;
  type: string;
  order?: number;
  feature?: RetailFeatureKey;
  props: TProps;
}

export interface MobilePageConfig {
  blocks: readonly MobileBlockConfig[];
}

export interface MobileConfig {
  schemaVersion: number;
  configVersion: number;
  tenant: RetailTenant;
  theme: RetailThemeConfig;
  features: RetailFeatureConfig;
  navigation: RetailNavigationItem[];
  pages: Record<string, MobilePageConfig>;
}
