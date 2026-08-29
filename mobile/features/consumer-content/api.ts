import { personalApiClient, resolveApiAssetUrl } from '@/lib/api-client';
import type { ConsumerNewsItem, NewsPromotionProduct } from '@/features/loyalty/news';
import type {
  ConsumerBannerDto,
  ConsumerCatalogItem,
  ConsumerCatalogPage,
  ConsumerContentContext,
  ConsumerPromotionDto,
  ConsumerPromotionCampaignDto,
} from './types';

const catalogAnalyticsSessionId = `mobile-${Date.now()}-${Math.random().toString(36).slice(2)}`;

export async function recordConsumerCatalogEvent(context: ConsumerContentContext, event: { catalogId: string; eventType: 'catalog_view' | 'product_view' | 'product_scan'; productId?: string | null }): Promise<void> {
  await personalApiClient.post(`/consumer/${context.tenantId}/catalog-events`, { catalogId: event.catalogId, storeId: context.storeId, productId: event.productId ?? null, eventType: event.eventType, sessionId: catalogAnalyticsSessionId });
}

function formatValidUntil(value: string | null): string {
  if (!value) return 'Постійна пропозиція';
  return `До ${new Date(value).toLocaleDateString('uk-UA', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  })}`;
}

function mapBannerProduct(
  product: ConsumerBannerDto['products'][number]
): NewsPromotionProduct {
  return {
    id: product.id,
    barcode: null,
    name: product.name,
    unit: product.unit,
    regularPrice: product.priceRetail,
    appPrice: null,
    discountPercent: null,
    imageUrl: resolveApiAssetUrl(product.imageUrl),
    icon: 'cube-outline',
    background: '#f3f4f6',
    manufacturer: null,
    countryOfOrigin: null,
  };
}

function mapBanner(dto: ConsumerBannerDto): ConsumerNewsItem {
  return {
    id: dto.id,
    contentType: 'banner',
    icon: (dto.icon || 'newspaper-outline') as ConsumerNewsItem['icon'],
    eyebrow: dto.eyebrow,
    title: dto.title,
    description: dto.description,
    body: dto.body,
    validUntil: formatValidUntil(dto.validUntil),
    terms: dto.terms,
    promotionProducts: dto.products.map(mapBannerProduct),
    background: dto.backgroundColor || '#14532d',
    accent: dto.accentColor || '#86efac',
    imageUrl: resolveApiAssetUrl(dto.imageUrl),
    detailMode: dto.detailMode,
    externalUrl: dto.externalUrl,
  };
}

function mapPromotion(dto: ConsumerPromotionDto): NewsPromotionProduct {
  return {
    id: dto.productId,
    barcode: null,
    name: dto.productName,
    unit: dto.unit,
    regularPrice: dto.priceOriginal,
    appPrice: dto.priceDiscounted,
    discountPercent: dto.discountPercent,
    imageUrl: resolveApiAssetUrl(dto.imageUrl),
    icon: 'pricetag-outline',
    background: '#f0fdf4',
    manufacturer: null,
    countryOfOrigin: null,
  };
}

function mapPromotionCampaign(dto: ConsumerPromotionCampaignDto): ConsumerNewsItem {
  return { id:dto.id, contentType:'promotion_campaign', icon:'pricetag-outline', eyebrow:dto.eyebrow, title:dto.title, description:dto.description,
    body:dto.body, terms:dto.terms, validUntil:formatValidUntil(dto.endsAt), imageUrl:resolveApiAssetUrl(dto.imageUrl),
    background:dto.backgroundColor, accent:dto.accentColor, detailMode:'internal', externalUrl:null,
    promotionProducts:dto.products.map(mapPromotion) };
}

export async function getConsumerBanners({
  tenantId,
  storeId,
}: ConsumerContentContext): Promise<ConsumerNewsItem[]> {
  const { data } = await personalApiClient.get<ConsumerBannerDto[]>(
    `/consumer/${tenantId}/banners`,
    { params: { storeId } }
  );
  return data.map(mapBanner);
}

export async function recordBannerView(context: ConsumerContentContext, id: string): Promise<void> {
  await personalApiClient.post(`/consumer/${context.tenantId}/banners/${id}/view`, null, { params: { storeId: context.storeId } });
}

export async function recordBannerClick(context: ConsumerContentContext, id: string): Promise<void> {
  await personalApiClient.post(`/consumer/${context.tenantId}/banners/${id}/click`, null, { params: { storeId: context.storeId } });
}

export async function recordPromotionCampaignEvent(context: ConsumerContentContext, id: string, eventType: 'impression' | 'open'): Promise<void> {
  await personalApiClient.post(`/consumer/${context.tenantId}/promotion-campaigns/${id}/${eventType}`, null, { params: { storeId: context.storeId } });
}

export async function getConsumerPromotions({
  tenantId,
  storeId,
}: ConsumerContentContext): Promise<NewsPromotionProduct[]> {
  const { data } = await personalApiClient.get<ConsumerPromotionDto[]>(
    `/consumer/${tenantId}/promotions`,
    { params: { storeId } }
  );
  return data.map(mapPromotion);
}

export async function getConsumerPromotionCampaigns({ tenantId, storeId }: ConsumerContentContext): Promise<ConsumerNewsItem[]> {
  const { data } = await personalApiClient.get<ConsumerPromotionCampaignDto[]>(`/consumer/${tenantId}/promotion-campaigns`, { params: { storeId } });
  return data.map(mapPromotionCampaign);
}

export async function getConsumerCatalog(
  context: ConsumerContentContext,
  params: { search?: string; categoryId?: string; page?: number; pageSize?: number }
): Promise<ConsumerCatalogPage> {
  const { data } = await personalApiClient.get<ConsumerCatalogPage>(
    `/consumer/${context.tenantId}/catalog`,
    { params: { storeId: context.storeId, ...params } }
  );
  return {
    ...data,
    items: data.items.map((item) => ({
      ...item,
      imageUrl: resolveApiAssetUrl(item.imageUrl),
    })),
  };
}

export async function getConsumerCatalogByIds(
  context: ConsumerContentContext,
  ids: string[]
): Promise<ConsumerCatalogItem[]> {
  if (ids.length === 0) return [];
  // ASP.NET Core's `[FromQuery(Name = "ids")] Guid[]` model binder expects repeated
  // `ids=<guid1>&ids=<guid2>` pairs. Axios has no built-in multi-value-param precedent
  // elsewhere in this codebase, and its default paramsSerializer (verified against
  // node_modules/axios's toFormData, default `indexes: false`) emits `ids[]=<guid>`
  // instead, which the binder would not populate — so this one call builds its query
  // string by hand rather than passing `ids` through axios's `params` option.
  const idsQuery = ids.map((id) => `ids=${encodeURIComponent(id)}`).join('&');
  const { data } = await personalApiClient.get<ConsumerCatalogItem[]>(
    `/consumer/${context.tenantId}/catalog/by-ids?storeId=${encodeURIComponent(context.storeId)}&${idsQuery}`
  );
  return data.map((item) => ({ ...item, imageUrl: resolveApiAssetUrl(item.imageUrl) }));
}
