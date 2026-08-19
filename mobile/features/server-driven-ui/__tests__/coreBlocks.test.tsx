import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render } from '@testing-library/react-native';
import { RetailShellProviders } from '@/features/mobile-config/RetailShellProviders';
import type { MobilePageConfig } from '@/features/mobile-config/types';
import { componentRegistry } from '../coreRegistry';
import { PageBlockList } from '../PageRenderer';
import {
  isBannerCarouselProps,
  isHeroBannerProps,
  isLoyaltyBalanceProps,
  isLoyaltyCardProps,
  isNewsListProps,
  isProductCollectionProps,
  isPromotionCollectionProps,
  isQuickActionsProps,
  isSectionHeaderProps,
  isStoreListProps,
} from '../blocks/validators';

const coreTypes = [
  'heroBanner',
  'bannerCarousel',
  'loyaltyCard',
  'loyaltyBalance',
  'promotionCarousel',
  'promotionGrid',
  'productCarousel',
  'productGrid',
  'sectionHeader',
  'quickActions',
  'newsList',
  'storeList',
] as const;

describe('Core Blocks V1', () => {
  test('registers every required V1 block type', () => {
    expect(coreTypes.every((type) => componentRegistry.has(type))).toBe(true);
  });

  test('validates every block props family and rejects malformed data', () => {
    expect(isHeroBannerProps({ title: 'Hero' })).toBe(true);
    expect(isBannerCarouselProps({ items: [{ id: 'b', title: 'Banner' }] })).toBe(true);
    expect(isLoyaltyCardProps({ balance: 10 })).toBe(true);
    expect(isLoyaltyBalanceProps({ balance: 10 })).toBe(true);
    expect(isPromotionCollectionProps({ items: [{ id: 'p', title: 'Promo' }] })).toBe(true);
    expect(isProductCollectionProps({ items: [{ id: 'p', name: 'Milk', price: 20 }] })).toBe(true);
    expect(isSectionHeaderProps({ title: 'Section' })).toBe(true);
    expect(isQuickActionsProps({ items: [{ id: 'q', label: 'Scan' }] })).toBe(true);
    expect(isNewsListProps({ items: [{ id: 'n', title: 'News' }] })).toBe(true);
    expect(isStoreListProps({ items: [{ id: 's', name: 'Store' }] })).toBe(true);
    expect(isProductCollectionProps({ items: [{ id: 'bad', name: 'Bad', price: 'free' }] })).toBe(false);
    expect(isQuickActionsProps({ items: Array.from({ length: 9 }, () => ({ id: 'q', label: 'Too many' })) })).toBe(false);
  });

  test('accepts columns: 4 for promotion and product grids (TASK-569)', () => {
    expect(isPromotionCollectionProps({ items: [{ id: 'p', title: 'Promo' }], columns: 4 })).toBe(true);
    expect(isProductCollectionProps({ items: [{ id: 'p', name: 'Milk', price: 20 }], columns: 4 })).toBe(true);
  });

  test('accepts the new optional heightPx/cardWidthPx size props and rejects non-numeric values', () => {
    expect(isHeroBannerProps({ title: 'Hero', heightPx: 220 })).toBe(true);
    expect(isHeroBannerProps({ title: 'Hero', heightPx: '220' })).toBe(false);
    expect(isBannerCarouselProps({ items: [{ id: 'b', title: 'Banner' }], cardWidthPx: 320 })).toBe(true);
    expect(isBannerCarouselProps({ items: [{ id: 'b', title: 'Banner' }], cardWidthPx: '320' })).toBe(false);
    expect(isPromotionCollectionProps({ items: [{ id: 'p', title: 'Promo' }], cardWidthPx: 240 })).toBe(true);
    expect(isProductCollectionProps({ items: [{ id: 'p', name: 'Milk', price: 20 }], cardWidthPx: 190 })).toBe(true);
  });

  test('renders all core block families through the default registry', async () => {
    const page: MobilePageConfig = {
      blocks: [
        { id: 'hero', type: 'heroBanner', props: { title: 'Hero title' } },
        { id: 'banners', type: 'bannerCarousel', props: { items: [{ id: 'b', title: 'Banner title' }] } },
        { id: 'card', type: 'loyaltyCard', props: { balance: 100, title: 'Loyalty card' } },
        { id: 'balance', type: 'loyaltyBalance', props: { balance: 50, label: 'Balance label' } },
        { id: 'promo-carousel', type: 'promotionCarousel', props: { items: [{ id: 'pc', title: 'Promo carousel' }] } },
        { id: 'promo-grid', type: 'promotionGrid', props: { items: [{ id: 'pg', title: 'Promo grid' }] } },
        { id: 'product-carousel', type: 'productCarousel', props: { items: [{ id: 'xc', name: 'Product carousel', price: 1 }] } },
        { id: 'product-grid', type: 'productGrid', props: { items: [{ id: 'xg', name: 'Product grid', price: 2 }] } },
        { id: 'section', type: 'sectionHeader', props: { title: 'Section title' } },
        { id: 'quick', type: 'quickActions', props: { items: [{ id: 'q', label: 'Quick action' }] } },
        { id: 'news', type: 'newsList', props: { items: [{ id: 'n', title: 'News title' }] } },
        { id: 'stores', type: 'storeList', props: { items: [{ id: 's', name: 'Store name' }] } },
      ],
    };
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const screen = await render(
      <QueryClientProvider client={client}>
        <RetailShellProviders>
          <PageBlockList page={page} />
        </RetailShellProviders>
      </QueryClientProvider>
    );
    await act(async () => fireEvent.press(screen.getByLabelText('Відкрити список магазинів')));
    for (const expected of [
      'Hero title',
      'Banner title',
      'Loyalty card',
      'Balance label',
      'Promo carousel',
      'Promo grid',
      'Product carousel',
      'Product grid',
      'Section title',
      'Quick action',
      'News title',
      'Store name',
    ]) {
      expect(screen.getByText(expected)).toBeOnTheScreen();
    }
    await screen.unmount();
    client.clear();
  });

  test('renders heightPx/cardWidthPx overrides when authored, and today\'s defaults when absent', async () => {
    const page: MobilePageConfig = {
      blocks: [
        { id: 'hero-custom', type: 'heroBanner', props: { title: 'Hero custom', heightPx: 240 } },
        { id: 'hero-default', type: 'heroBanner', props: { title: 'Hero default' } },
        { id: 'banners', type: 'bannerCarousel', props: { items: [
          { id: 'b-custom', title: 'Banner custom' },
          { id: 'b-default', title: 'Banner default' },
        ], cardWidthPx: 320 } },
        { id: 'promo-carousel', type: 'promotionCarousel', props: { items: [{ id: 'pc', title: 'Promo carousel' }], cardWidthPx: 240 } },
        { id: 'product-carousel', type: 'productCarousel', props: { items: [{ id: 'xc', name: 'Product carousel', price: 1 }], cardWidthPx: 190 } },
      ],
    };
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const screen = await render(
      <QueryClientProvider client={client}>
        <RetailShellProviders>
          <PageBlockList page={page} />
        </RetailShellProviders>
      </QueryClientProvider>
    );
    expect(screen.getByTestId('block-hero-custom').props.style.minHeight).toBe(240);
    expect(screen.getByTestId('block-hero-default').props.style.minHeight).toBe(190);
    expect(screen.getByTestId('banner-card-b-custom').props.style.width).toBe(320);
    expect(screen.getByTestId('promotion-card-pc').props.style.width).toBe(240);
    expect(screen.getByTestId('product-card-xc').props.style.width).toBe(190);
    await screen.unmount();
    client.clear();
  });

  test('falls back to today\'s exact default pixel values when heightPx/cardWidthPx are absent (regression guard)', async () => {
    const page: MobilePageConfig = {
      blocks: [
        { id: 'hero', type: 'heroBanner', props: { title: 'Hero' } },
        { id: 'banners', type: 'bannerCarousel', props: { items: [{ id: 'b', title: 'Banner' }] } },
        { id: 'promo-carousel', type: 'promotionCarousel', props: { items: [{ id: 'pc', title: 'Promo' }] } },
        { id: 'product-carousel', type: 'productCarousel', props: { items: [{ id: 'xc', name: 'Product', price: 1 }] } },
      ],
    };
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const screen = await render(
      <QueryClientProvider client={client}>
        <RetailShellProviders>
          <PageBlockList page={page} />
        </RetailShellProviders>
      </QueryClientProvider>
    );
    expect(screen.getByTestId('block-hero').props.style.minHeight).toBe(190);
    expect(screen.getByTestId('banner-card-b').props.style.width).toBe(280);
    expect(screen.getByTestId('promotion-card-pc').props.style.width).toBe(210);
    expect(screen.getByTestId('product-card-xc').props.style.width).toBe(170);
    await screen.unmount();
    client.clear();
  });

  test('renders promotionGrid/productGrid cards at 23% width for columns: 4 (TASK-569)', async () => {
    const page: MobilePageConfig = {
      blocks: [
        { id: 'promo-grid-4', type: 'promotionGrid', props: { items: [{ id: 'pg4', title: 'Promo grid 4' }], columns: 4 } },
        { id: 'product-grid-4', type: 'productGrid', props: { items: [{ id: 'xg4', name: 'Product grid 4', price: 3 }], columns: 4 } },
      ],
    };
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const screen = await render(
      <QueryClientProvider client={client}>
        <RetailShellProviders>
          <PageBlockList page={page} />
        </RetailShellProviders>
      </QueryClientProvider>
    );
    expect(screen.getByTestId('promotion-card-pg4').props.style.width).toBe('23%');
    expect(screen.getByTestId('product-card-xg4').props.style.width).toBe('23%');
    await screen.unmount();
    client.clear();
  });

  test('falls back to a placeholder when a valid remote image fails to load', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const screen = await render(
      <QueryClientProvider client={client}>
        <RetailShellProviders>
          <PageBlockList page={{ blocks: [{
            id: 'banner', type: 'bannerCarousel',
            props: { items: [{ id: 'image', title: 'Broken image', imageUrl: 'https://cdn.example/missing.png' }] },
          }] }} />
        </RetailShellProviders>
      </QueryClientProvider>
    );
    await act(async () => screen.getByTestId('remote-image').props.onError());
    expect(screen.getByTestId('image-placeholder')).toBeOnTheScreen();
    await screen.unmount();
    client.clear();
  });
});
