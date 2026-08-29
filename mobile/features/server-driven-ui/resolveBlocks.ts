import type { ConsumerCatalogItem } from '@/features/consumer-content/types';
import type { ConsumerNewsItem, NewsPromotionProduct } from '@/features/loyalty/news';
import type { LoyaltyMembershipSummary, LoyaltyNetworkSummary } from '@/features/loyalty/types';
import type { MobileBlockConfig, MobilePageConfig } from '@/features/mobile-config/types';

export interface BlockDataSources {
  banners: readonly ConsumerNewsItem[];
  promotionCampaigns?: readonly ConsumerNewsItem[];
  promotions: readonly NewsPromotionProduct[];
  catalog: readonly ConsumerCatalogItem[];
  catalogById: ReadonlyMap<string, ConsumerCatalogItem>;
  membership: LoyaltyMembershipSummary | null;
  network: LoyaltyNetworkSummary | null;
}

function positiveInt(value: unknown, fallback: number, maximum: number) {
  return typeof value === 'number' && Number.isInteger(value)
    ? Math.min(maximum, Math.max(1, value))
    : fallback;
}

function columns(value: unknown): 2 | 3 | 4 {
  return value === 3 ? 3 : value === 4 ? 4 : 2;
}

const actionLabels: Record<string, string> = {
  home: 'Головна', promotions: 'Акції', catalog: 'Каталог', loyalty: 'Картка',
  coupons: 'Купони', stores: 'Магазини', news: 'Новини', profile: 'Профіль',
};

export function resolveBlock(block: MobileBlockConfig, data: BlockDataSources): MobileBlockConfig {
  const props = block.props && typeof block.props === 'object' ? block.props as Record<string, unknown> : {};
  switch (block.type) {
    case 'bannerCarousel': {
      const limit = positiveInt(props.limit, 5, 10);
      return { ...block, props: {
        items: data.banners.slice(0, limit).map((item) => ({
          id: item.id, title: item.title, subtitle: item.description, imageUrl: item.imageUrl ?? undefined,
        })),
        cardWidthPx: typeof props.cardWidthPx === 'number' ? props.cardWidthPx : undefined,
      } };
    }
    case 'loyaltyCard':
      return { ...block, props: {
        title: 'Картка покупця', balance: data.membership?.balance ?? 0,
        cardNumber: data.membership ? `•••• ${data.membership.membershipId.slice(-4)}` : undefined,
      } };
    case 'loyaltyBalance':
      return { ...block, props: {
        label: props.showPointsLabel === false ? '' : 'Бонусний баланс',
        balance: data.membership?.balance ?? 0, unit: 'бонусів',
      } };
    case 'promotionCarousel':
    case 'promotionGrid': {
      const limit = positiveInt(props.limit, block.type === 'promotionGrid' ? 12 : 10, 30);
      return { ...block, props: { title: props.title, showViewAll: props.showViewAll,
        columns: columns(props.columns),
        cardWidthPx: typeof props.cardWidthPx === 'number' ? props.cardWidthPx : undefined,
        items: data.promotions.slice(0, limit).map((item) => ({
        id: item.id, title: item.name,
        subtitle: item.appPrice === null ? item.unit : `${item.appPrice.toFixed(2)} ₴`,
        imageUrl: item.imageUrl ?? undefined,
        badge: item.discountPercent === null ? undefined : `−${item.discountPercent}%`,
      })) } };
    }
    case 'productCarousel':
    case 'productGrid': {
      const limit = positiveInt(props.limit, block.type === 'productGrid' ? 12 : 10, 30);
      // TASK-570/573 (ADR-032): an admin-curated `productIds` selection overrides the
      // alphabetical fallback, resolved in the admin's chosen order via catalogById (which
      // covers ids outside the default catalog page window). A stale/deleted id, or one
      // whose item has no retail price, is silently skipped — never rendered as "missing".
      // Empty/absent productIds falls back to today's unchanged alphabetical-by-limit behavior.
      const productIds = Array.isArray(props.productIds)
        ? props.productIds.filter((value): value is string => typeof value === 'string')
        : [];
      const sourceItems = productIds.length > 0
        ? productIds
            .map((id) => data.catalogById.get(id))
            .filter((item): item is ConsumerCatalogItem => !!item && item.priceRetail !== null)
        : data.catalog.filter((item) => item.priceRetail !== null);
      return { ...block, props: { title: props.title, showViewAll: props.showViewAll,
        columns: columns(props.columns),
        cardWidthPx: typeof props.cardWidthPx === 'number' ? props.cardWidthPx : undefined,
        items: sourceItems
        .slice(0, limit)
        .map((item) => ({ id: item.id, name: item.name, price: item.priceRetail as number,
          unit: item.unit, imageUrl: item.imageUrl ?? undefined })) } };
    }
    case 'quickActions': {
      const actions = Array.isArray(props.actions) ? props.actions.filter((item): item is string => typeof item === 'string') : [];
      return { ...block, props: { items: actions.map((action) => ({ id: action, label: actionLabels[action] ?? action, icon: action })) } };
    }
    case 'newsList': {
      const limit = positiveInt(props.limit, 5, 20);
      return { ...block, props: { items: (data.promotionCampaigns ?? []).slice(0, limit).map((item) => ({
        id: item.id, title: item.title, summary: item.description,
        publishedAt: item.validUntil ?? undefined, imageUrl: item.imageUrl ?? undefined,
      })) } };
    }
    case 'storeList': {
      const limit = positiveInt(props.limit, 10, 30);
      return { ...block, props: { title: props.title, showDistance: props.showDistance, limit,
        items: (data.network?.stores ?? []).slice(0, limit).map((store) => ({
        id: store.storeId, name: store.storeName, address: store.address ?? undefined,
      })) } };
    }
    default:
      return block;
  }
}

export function resolvePage(page: MobilePageConfig, data: BlockDataSources): MobilePageConfig {
  return { blocks: page.blocks.map((block) => {
    const resolved = resolveBlock(block, data);
    const authored = block.props && typeof block.props === 'object' ? block.props as Record<string, unknown> : {};
    return authored._visualEffect && typeof authored._visualEffect === 'object'
      ? { ...resolved, props: { ...(resolved.props as Record<string, unknown>), _visualEffect: authored._visualEffect } }
      : resolved;
  }) };
}
