import { resolveBlock } from '../resolveBlocks';
import type { BlockDataSources } from '../resolveBlocks';

const data: BlockDataSources = {
  banners: [{
    id: 'banner-1', title: 'Літня акція', eyebrow: null, description: 'Опис', body: [], terms: [],
    imageUrl: null, icon: 'gift-outline', background: '#ffffff', accent: '#000000', detailMode: 'internal',
    externalUrl: null, validUntil: 'до завершення', promotionProducts: [],
  }],
  promotions: [{
    id: 'promo-1', barcode: null, name: 'Молоко', imageUrl: null, unit: 'шт',
    discountPercent: 20, regularPrice: 50, appPrice: 40, icon: 'pricetag-outline',
    background: '#ffffff', manufacturer: null, countryOfOrigin: null,
  }],
  catalog: [{
    id: 'product-1', name: 'Хліб', imageUrl: null, unit: 'шт', priceRetail: 30,
    categoryId: null, categoryName: null, isAvailableAtStore: true,
  }],
  membership: {
    membershipId: 'membership-1234', tenantId: 'tenant-1', tenantName: 'Мережа', balance: 125,
    status: 'active', joinedAt: '2026-08-01', preferredStoreId: null,
    preferredStoreName: null, preferredStoreAddress: null,
  },
  network: {
    tenantId: 'tenant-1', tenantName: 'Мережа', slug: 'merezha',
    stores: [{ storeId: 'store-1', storeName: 'Центр', address: 'Київ' }],
  },
};

describe('App Builder block data resolution', () => {
  test.each([
    ['bannerCarousel', { limit: 3 }, 'Літня акція'],
    ['loyaltyCard', { showQrCode: true }, 125],
    ['loyaltyBalance', { showPointsLabel: true }, 125],
    ['promotionCarousel', { limit: 5 }, 'Молоко'],
    ['promotionGrid', { limit: 5, columns: 2 }, 'Молоко'],
    ['productCarousel', { limit: 5 }, 'Хліб'],
    ['productGrid', { limit: 5, columns: 2 }, 'Хліб'],
    ['quickActions', { actions: ['catalog'] }, 'Каталог'],
    ['newsList', { limit: 5 }, 'Літня акція'],
    ['storeList', { limit: 5 }, 'Центр'],
  ])('resolves authored %s props into renderable data', (type, props, expected) => {
    const resolved = resolveBlock({ id: type, type, props }, data);
    expect(JSON.stringify(resolved.props)).toContain(String(expected));
  });

  test.each([
    ['promotionGrid', { limit: 5, columns: 4 }],
    ['productGrid', { limit: 5, columns: 4 }],
  ])('forwards columns: 4 through resolveBlock unchanged for %s (TASK-569)', (type, props) => {
    const resolved = resolveBlock({ id: type, type, props }, data);
    expect((resolved.props as { columns?: number }).columns).toBe(4);
  });

  it('preserves static blocks authored directly in the builder', () => {
    const block = { id: 'hero', type: 'heroBanner', props: { title: 'Вітаємо' } };
    expect(resolveBlock(block, data)).toBe(block);
  });

  it('preserves heightPx on heroBanner blocks (default: return block passthrough)', () => {
    const block = { id: 'hero', type: 'heroBanner', props: { title: 'Вітаємо', heightPx: 220 } };
    const resolved = resolveBlock(block, data);
    expect((resolved.props as { heightPx?: number }).heightPx).toBe(220);
  });

  test.each([
    ['bannerCarousel', { limit: 3, cardWidthPx: 320 }],
    ['promotionCarousel', { limit: 5, cardWidthPx: 240 }],
    ['productCarousel', { limit: 5, cardWidthPx: 190 }],
  ])('forwards authored cardWidthPx through resolveBlock for %s', (type, props) => {
    const resolved = resolveBlock({ id: type, type, props }, data);
    expect((resolved.props as { cardWidthPx?: number }).cardWidthPx).toBe((props as { cardWidthPx: number }).cardWidthPx);
  });

  test.each([
    ['bannerCarousel', { limit: 3 }],
    ['promotionCarousel', { limit: 5 }],
    ['productCarousel', { limit: 5 }],
  ])('resolves cardWidthPx to undefined when not authored, so the default renders (%s)', (type, props) => {
    const resolved = resolveBlock({ id: type, type, props }, data);
    expect((resolved.props as { cardWidthPx?: number }).cardWidthPx).toBeUndefined();
  });
});
