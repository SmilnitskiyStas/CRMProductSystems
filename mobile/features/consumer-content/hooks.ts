import { useQuery } from '@tanstack/react-query';
import { useMemberships } from '@/features/loyalty/hooks/useLoyalty';
import { useLoyaltyUiStore } from '@/features/loyalty/store';
import { selectMembershipForTenant } from '@/features/loyalty/selection';
import { getConsumerBanners, getConsumerCatalog, getConsumerCatalogByIds, getConsumerPromotions } from './api';
import type { ConsumerContentContext } from './types';

export function useSelectedConsumerContext(enabled = true) {
  const selectedTenantId = useLoyaltyUiStore((state) => state.selectedTenantId);
  const membershipsQuery = useMemberships(enabled);
  const membership = selectMembershipForTenant(membershipsQuery.data, selectedTenantId);
  const context = membership?.preferredStoreId
    ? { tenantId: membership.tenantId, storeId: membership.preferredStoreId }
    : null;
  return { context, membership, membershipsQuery };
}

export function useConsumerBanners(context: ConsumerContentContext | null) {
  return useQuery({
    queryKey: ['consumer-content', 'banners', context?.tenantId, context?.storeId],
    queryFn: () => getConsumerBanners(context as ConsumerContentContext),
    enabled: Boolean(context),
    staleTime: 60_000,
  });
}

export function useConsumerPromotions(context: ConsumerContentContext | null) {
  return useQuery({
    queryKey: ['consumer-content', 'promotions', context?.tenantId, context?.storeId],
    queryFn: () => getConsumerPromotions(context as ConsumerContentContext),
    enabled: Boolean(context),
    staleTime: 60_000,
  });
}

export function useConsumerCatalog(
  context: ConsumerContentContext | null,
  params: { search?: string; categoryId?: string; page?: number; pageSize?: number }
) {
  return useQuery({
    queryKey: ['consumer-content', 'catalog', context?.tenantId, context?.storeId, params],
    queryFn: () => getConsumerCatalog(context as ConsumerContentContext, params),
    enabled: Boolean(context),
    placeholderData: (previous) => previous,
  });
}

export function useConsumerCatalogByIds(context: ConsumerContentContext | null, ids: string[]) {
  return useQuery({
    queryKey: ['consumer-content', 'catalog-by-ids', context?.tenantId, context?.storeId, [...ids].sort()],
    queryFn: () => getConsumerCatalogByIds(context as ConsumerContentContext, ids),
    enabled: Boolean(context) && ids.length > 0,
    staleTime: 60_000,
  });
}
