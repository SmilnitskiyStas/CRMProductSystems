import type { MobilePageConfig } from '@/features/mobile-config/types';

/**
 * A page entry is created as soon as an administrator opens it in App Builder.
 * Do not let an empty promotions page replace the functional, API-backed screen.
 */
export function hasConfiguredPromotionsContent(page: MobilePageConfig | undefined): boolean {
  return Boolean(page?.blocks.some(
    (block) => block.type === 'promotionGrid' || block.type === 'promotionCarousel'
  ));
}
