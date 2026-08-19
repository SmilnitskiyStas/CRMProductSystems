import { View } from 'react-native';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import type { MobilePageConfig, RetailFeatureConfig } from '@/features/mobile-config/types';
import { featureRequirementMet } from '@/features/feature-flags/policy';
import { BlockRenderer } from './BlockRenderer';
import { componentRegistry } from './coreRegistry';
import type { ComponentRegistry } from './registry';
import { defaultRendererLogger } from './logger';
import type { RendererLogger } from './types';

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
  const page = config.pages[pageKey];
  if (!page) return null;
  return <PageBlockList page={page} features={config.features} />;
}
