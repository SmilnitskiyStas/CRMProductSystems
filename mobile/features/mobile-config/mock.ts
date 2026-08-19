import type { MobileConfig } from './types';

export const SAFE_DEFAULT_TENANT_ID = 'retail-preview';

export function createMockMobileConfig(tenantId: string): MobileConfig {
  return {
    schemaVersion: 0,
    configVersion: 1,
    tenant: {
      id: tenantId,
      slug: tenantId === SAFE_DEFAULT_TENANT_ID ? 'retail-preview' : tenantId,
      name: tenantId === SAFE_DEFAULT_TENANT_ID ? 'Мій магазин' : 'Retailer',
      logoUrl: null,
    },
    theme: {
      colors: {
        primary: '#16a34a',
        secondary: '#14532d',
        background: '#ffffff',
        surface: '#f3f4f6',
        textPrimary: '#111827',
        textSecondary: '#6b7280',
      },
      buttons: { radius: 14 },
      cards: { radius: 18 },
      spacing: 'comfortable',
    },
    features: {
      loyalty: true,
      promotions: true,
      catalog: true,
      coupons: false,
      news: true,
      receipts: false,
      delivery: false,
      personalOffers: false,
    },
    navigation: [
      { type: 'home', label: 'Головна', icon: 'home' },
      { type: 'catalog', label: 'Каталог', icon: 'grid' },
      { type: 'loyalty', label: 'Бонуси', icon: 'qr' },
      { type: 'profile', label: 'Профіль', icon: 'user' },
    ],
    pages: { home: { blocks: [] } },
  };
}
