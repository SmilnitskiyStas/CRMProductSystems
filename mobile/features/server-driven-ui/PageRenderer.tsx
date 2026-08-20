import { View } from 'react-native';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import type { MobilePageConfig, RetailFeatureConfig } from '@/features/mobile-config/types';
import { featureRequirementMet } from '@/features/feature-flags/policy';
import { BlockRenderer } from './BlockRenderer';
import { componentRegistry } from './coreRegistry';
import type { ComponentRegistry } from './registry';
import { defaultRendererLogger } from './logger';
import type { RendererLogger } from './types';
import {
  useConsumerBanners,
  useConsumerCatalog,
  useConsumerCatalogByIds,
  useConsumerPromotions,
  useSelectedConsumerContext,
} from '@/features/consumer-content/hooks';
import type { ConsumerCatalogItem } from '@/features/consumer-content/types';
import { useAvailableNetworks } from '@/features/loyalty/hooks/useLoyalty';
import { resolvePage } from './resolveBlocks';
import { useAuthStore } from '@/features/auth/store';

interface PageBlockListProps {
  page: MobilePageConfig;
  registry?: ComponentRegistry;
  logger?: RendererLogger;
  features?: RetailFeatureConfig;
}

export function PageBlockList({
  page,
  registry = componentRegistry,
  logger = defaultRendererLogger,
  features,
}: PageBlockListProps) {
  const blocks = [...page.blocks].sort(
    (left, right) => (left.order ?? Number.MAX_SAFE_INTEGER) - (right.order ?? Number.MAX_SAFE_INTEGER)
  );
  return (
    <View>
      {blocks.filter((block) => !features || featureRequirementMet(features, block.feature)).map((block) => (
        <BlockRenderer key={block.id} block={block} registry={registry} logger={logger} />
      ))}
    </View>
  );
}

// TASK-570/573 (ADR-032): a curated productGrid/productCarousel selection can name any
// product id, not just one that falls inside PageRenderer's default page=1/pageSize=30
// catalog fetch below. This scans the page's own blocks for those curated ids so they can
// be resolved via a second, targeted "fetch exactly these ids" query — see catalogByIds.
function getCuratedProductIds(page: MobilePageConfig | undefined): string[] {
  const ids = new Set<string>();
  for (const block of page?.blocks ?? []) {
    if (block.type !== 'productGrid' && block.type !== 'productCarousel') continue;
    const props = block.props && typeof block.props === 'object' ? (block.props as Record<string, unknown>) : {};
    if (!Array.isArray(props.productIds)) continue;
    for (const id of props.productIds) {
      if (typeof id === 'string') ids.add(id);
    }
  }
  return [...ids];
}

export function PageRenderer({ pageKey }: { pageKey: string }) {
  const { config } = useMobileConfig();
  const hasPersonalAccess = useAuthStore((state) => state.personalAccessToken !== null);
  const { context, membership } = useSelectedConsumerContext(hasPersonalAccess);
  const page = config.pages[pageKey];
  const banners = useConsumerBanners(context);
  const promotions = useConsumerPromotions(context);
  const catalog = useConsumerCatalog(context, { page: 1, pageSize: 30 });
  const curatedIds = getCuratedProductIds(page);
  const catalogByIds = useConsumerCatalogByIds(context, curatedIds);
  const networks = useAvailableNetworks(hasPersonalAccess);
  if (!page) return null;
  const network = networks.data?.find((item) => item.tenantId === membership?.tenantId) ?? null;
  const catalogById = new Map<string, ConsumerCatalogItem>();
  for (const item of catalog.data?.items ?? []) catalogById.set(item.id, item);
  for (const item of catalogByIds.data ?? []) catalogById.set(item.id, item);
  const resolved = resolvePage(page, {
    banners: banners.data ?? [], promotions: promotions.data ?? [],
    catalog: catalog.data?.items ?? [], catalogById, membership: membership ?? null, network,
  });
  return <PageBlockList page={resolved} features={config.features} />;
}
