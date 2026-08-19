import { personalApiClient, resolveApiAssetUrl } from '@/lib/api-client';
import type { ConsumerNewsItem, NewsPromotionProduct } from '@/features/loyalty/news';
import type {
  ConsumerBannerDto,
  ConsumerCatalogPage,
  ConsumerContentContext,
  ConsumerPromotionDto,
} from './types';

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

export async function recordBannerView(tenantId: string, id: string): Promise<void> {
  await personalApiClient.post(`/consumer/${tenantId}/banners/${id}/view`);
}

export async function recordBannerClick(tenantId: string, id: string): Promise<void> {
  await personalApiClient.post(`/consumer/${tenantId}/banners/${id}/click`);
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
