import { resolveBlock } from '../resolveBlocks';
import type { BlockDataSources } from '../resolveBlocks';

const catalog: BlockDataSources['catalog'] = [{
  id: 'product-1', name: 'Хліб', imageUrl: null, unit: 'шт', priceRetail: 30,
  categoryId: null, categoryName: null, isAvailableAtStore: true,
}];

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
  catalog,
  catalogById: new Map(catalog.map((item) => [item.id, item])),
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

  describe('curated productIds selection (TASK-570/573, ADR-032)', () => {
    const curatedCatalog: BlockDataSources['catalog'] = [
      { id: 'a', name: 'Яблука', imageUrl: null, unit: 'кг', priceRetail: 10, categoryId: null, categoryName: null, isAvailableAtStore: true },
      { id: 'b', name: 'Банани', imageUrl: null, unit: 'кг', priceRetail: 20, categoryId: null, categoryName: null, isAvailableAtStore: true },
      { id: 'c', name: 'Вишні', imageUrl: null, unit: 'кг', priceRetail: null, categoryId: null, categoryName: null, isAvailableAtStore: true },
      { id: 'd', name: 'Груші', imageUrl: null, unit: 'кг', priceRetail: 40, categoryId: null, categoryName: null, isAvailableAtStore: true },
    ];
    const curatedData: BlockDataSources = {
      ...data,
      catalog: curatedCatalog,
      catalogById: new Map(curatedCatalog.map((item) => [item.id, item])),
    };

    test.each(['productCarousel', 'productGrid'])(
      '(a) resolves a curated selection in the admin\'s exact chosen order, not alphabetical (%s)',
      (type) => {
        const resolved = resolveBlock({ id: type, type, props: { limit: 10, productIds: ['d', 'a', 'b'] } }, curatedData);
        const items = (resolved.props as { items: { id: string }[] }).items;
        expect(items.map((item) => item.id)).toEqual(['d', 'a', 'b']);
      }
    );

    test.each(['productCarousel', 'productGrid'])(
      '(b) silently skips a curated id with no match in catalogById, remaining items still resolve (%s)',
      (type) => {
        const resolved = resolveBlock({ id: type, type, props: { limit: 10, productIds: ['a', 'missing-id', 'b'] } }, curatedData);
        const items = (resolved.props as { items: { id: string }[] }).items;
        expect(items.map((item) => item.id)).toEqual(['a', 'b']);
      }
    );

    test.each(['productCarousel', 'productGrid'])(
      '(c) silently skips a curated id whose item has priceRetail === null (%s)',
      (type) => {
        const resolved = resolveBlock({ id: type, type, props: { limit: 10, productIds: ['a', 'c', 'b'] } }, curatedData);
        const items = (resolved.props as { items: { id: string }[] }).items;
        expect(items.map((item) => item.id)).toEqual(['a', 'b']);
      }
    );

    test.each(['productCarousel', 'productGrid'])(
      '(d) caps a curated selection longer than limit (%s)',
      (type) => {
        const resolved = resolveBlock({ id: type, type, props: { limit: 2, productIds: ['d', 'a', 'b'] } }, curatedData);
        const items = (resolved.props as { items: { id: string }[] }).items;
        expect(items.map((item) => item.id)).toEqual(['d', 'a']);
      }
    );

    test.each(['productCarousel', 'productGrid'])(
      "(e) empty productIds resolves identically to a block with no productIds prop at all — regression guard on the unchanged alphabetical fallback (%s)",
      (type) => {
        const withoutProp = resolveBlock({ id: type, type, props: { limit: 10 } }, curatedData);
        const withEmptyArray = resolveBlock({ id: type, type, props: { limit: 10, productIds: [] } }, curatedData);
        expect(withEmptyArray).toEqual(withoutProp);
        // Fallback is still "alphabetical order as authored in the catalog array, priceRetail-filtered" —
        // 'c' (priceRetail: null) excluded, 'd' kept even though it wasn't curated.
        const items = (withoutProp.props as { items: { id: string }[] }).items;
        expect(items.map((item) => item.id)).toEqual(['a', 'b', 'd']);
      }
    );
  });
});
