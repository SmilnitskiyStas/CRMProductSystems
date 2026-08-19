import { View } from 'react-native';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import type { MobilePageConfig, RetailFeatureConfig } from '@/features/mobile-config/types';
import { featureRequirementMet } from '@/features/feature-flags/policy';
import { BlockRenderer } from './BlockRenderer';
import { componentRegistry } from './coreRegistry';
import type { ComponentRegistry } from './registry';
import { defaultRendererLogger } from './logger';
import type { RendererLogger } from './types';
import { useConsumerBanners, useConsumerCatalog, useConsumerPromotions, useSelectedConsumerContext } from '@/features/consumer-content/hooks';
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

export function PageRenderer({ pageKey }: { pageKey: string }) {
  const { config } = useMobileConfig();
  const hasPersonalAccess = useAuthStore((state) => state.personalAccessToken !== null);
  const { context, membership } = useSelectedConsumerContext(hasPersonalAccess);
  const banners = useConsumerBanners(context);
  const promotions = useConsumerPromotions(context);
  const catalog = useConsumerCatalog(context, { page: 1, pageSize: 30 });
  const networks = useAvailableNetworks(hasPersonalAccess);
  const page = config.pages[pageKey];
  if (!page) return null;
  const network = networks.data?.find((item) => item.tenantId === membership?.tenantId) ?? null;
  const resolved = resolvePage(page, {
    banners: banners.data ?? [], promotions: promotions.data ?? [],
    catalog: catalog.data?.items ?? [], membership: membership ?? null, network,
  });
  return <PageBlockList page={resolved} features={config.features} />;
}
