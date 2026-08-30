import { ActivityIndicator, Pressable, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
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
  useConsumerPromotionCampaigns,
  useConsumerPromotions,
  useSelectedConsumerContext,
} from '@/features/consumer-content/hooks';
import type { ConsumerCatalogItem } from '@/features/consumer-content/types';
import { useAvailableNetworks } from '@/features/loyalty/hooks/useLoyalty';
import { resolvePage } from './resolveBlocks';
import { useAuthStore } from '@/features/auth/store';
import { mergeStoreNetworks } from '@/features/loyalty/selection';

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
  const { context, membership, membershipsQuery } = useSelectedConsumerContext(hasPersonalAccess);
  const page = config.pages[pageKey];
  const banners = useConsumerBanners(context);
  const promotions = useConsumerPromotions(context);
  const promotionCampaigns = useConsumerPromotionCampaigns(context);
  const catalog = useConsumerCatalog(context, { page: 1, pageSize: 30 });
  const curatedIds = getCuratedProductIds(page);
  const catalogByIds = useConsumerCatalogByIds(context, curatedIds);
  const networks = useAvailableNetworks(hasPersonalAccess);
  if (!page) return null;

  // A configured promotions page used to render its block headings immediately while the
  // consumer context/data was still unavailable. With two promotion blocks this produced
  // exactly "Акції / Акції / Акції" and an otherwise blank screen. Keep the authored page,
  // but give this data-driven surface the same loading/error/empty states as the static page.
  if (pageKey === 'promotions') {
    if (membershipsQuery.isLoading || promotions.isLoading || promotionCampaigns.isLoading) {
      return <View className="items-center py-20"><ActivityIndicator size="large" color="#16a34a" /></View>;
    }
    if (!context) {
      return (
        <View className="items-center px-7 py-20">
          <Ionicons name="storefront-outline" size={48} color="#9ca3af" />
          <Text className="mt-4 text-center text-lg font-bold text-gray-900">Магазин не вибрано</Text>
          <Text className="mt-2 text-center text-sm text-gray-500">Оберіть мережу та магазин, щоб побачити актуальні пропозиції.</Text>
        </View>
      );
    }
    if (promotions.isError || promotionCampaigns.isError) {
      return (
        <View className="items-center px-7 py-20">
          <Text className="text-center text-gray-500">Не вдалося завантажити акції</Text>
          <Pressable
            onPress={() => void Promise.all([promotions.refetch(), promotionCampaigns.refetch()])}
            className="mt-4 rounded-xl bg-green-700 px-5 py-3"
          >
            <Text className="font-bold text-white">Спробувати ще раз</Text>
          </Pressable>
        </View>
      );
    }
    const promotionItems = [
      ...(promotions.data ?? []),
      ...(promotionCampaigns.data ?? []).flatMap((item) => item.promotionProducts ?? []),
    ];
    if (promotionItems.length === 0) {
      return <Text className="py-20 text-center text-gray-500">Активних акцій поки немає</Text>;
    }
  }
  // Builder blocks need the same reconciled store source as the static Home. Memberships carry
  // the persisted preferred store while the networks endpoint carries the complete store list.
  const storeNetworks = mergeStoreNetworks(networks.data, membershipsQuery.data);
  const network = storeNetworks.find((item) => item.tenantId === membership?.tenantId) ?? null;
  const catalogById = new Map<string, ConsumerCatalogItem>();
  for (const item of catalog.data?.items ?? []) catalogById.set(item.id, item);
  for (const item of catalogByIds.data ?? []) catalogById.set(item.id, item);
  const resolved = resolvePage(page, {
    banners: banners.data ?? [], promotionCampaigns: promotionCampaigns.data ?? [],
    promotions: [...(promotions.data ?? []), ...(promotionCampaigns.data ?? []).flatMap(item => item.promotionProducts ?? [])],
    catalog: catalog.data?.items ?? [], catalogById, membership: membership ?? null, network,
  });
  return <PageBlockList page={resolved} features={config.features} />;
}
