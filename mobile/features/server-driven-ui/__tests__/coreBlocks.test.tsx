import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render } from '@testing-library/react-native';
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
