import { ComponentRegistry } from './registry';
import {
  BannerCarouselBlock,
  HeroBannerBlock,
  LoyaltyBalanceBlock,
  LoyaltyCardBlock,
  NewsListBlock,
  ProductCarouselBlock,
  ProductGridBlock,
  PromotionCarouselBlock,
  PromotionGridBlock,
  QuickActionsBlock,
  SectionHeaderBlock,
  StoreListBlock,
} from './blocks/CoreBlocks';
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
} from './blocks/validators';

export function registerCoreBlocks(registry: ComponentRegistry): ComponentRegistry {
  return registry
    .register('heroBanner', HeroBannerBlock, isHeroBannerProps)
    .register('bannerCarousel', BannerCarouselBlock, isBannerCarouselProps)
    .register('loyaltyCard', LoyaltyCardBlock, isLoyaltyCardProps)
    .register('loyaltyBalance', LoyaltyBalanceBlock, isLoyaltyBalanceProps)
    .register('promotionCarousel', PromotionCarouselBlock, isPromotionCollectionProps)
    .register('promotionGrid', PromotionGridBlock, isPromotionCollectionProps)
    .register('productCarousel', ProductCarouselBlock, isProductCollectionProps)
    .register('productGrid', ProductGridBlock, isProductCollectionProps)
    .register('sectionHeader', SectionHeaderBlock, isSectionHeaderProps)
    .register('quickActions', QuickActionsBlock, isQuickActionsProps)
    .register('newsList', NewsListBlock, isNewsListProps)
    .register('storeList', StoreListBlock, isStoreListProps);
}

export const componentRegistry = registerCoreBlocks(new ComponentRegistry());
