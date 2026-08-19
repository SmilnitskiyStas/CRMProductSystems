import type { NewsPromotionProduct, ConsumerNewsItem } from '@/features/loyalty/news';

export interface ConsumerBannerProductDto {
  id: string;
  name: string;
  imageUrl: string | null;
  unit: string;
  priceRetail: number | null;
}

export interface ConsumerBannerDto {
  id: string;
  title: string;
  eyebrow: string | null;
  description: string;
  body: string[];
  terms: string[];
  imageUrl: string | null;
  icon: string | null;
  backgroundColor: string | null;
  accentColor: string | null;
  detailMode: 'internal' | 'external' | string;
  externalUrl: string | null;
  validUntil: string | null;
  sortOrder: number;
  products: ConsumerBannerProductDto[];
}

export interface ConsumerPromotionDto {
  id: string;
  productId: string;
  productName: string;
  imageUrl: string | null;
  unit: string;
  discountPercent: number;
  priceOriginal: number;
  priceDiscounted: number;
  validUntil: string | null;
}

export interface ConsumerCatalogItem {
  id: string;
  name: string;
  imageUrl: string | null;
  unit: string;
  priceRetail: number | null;
  categoryId: string | null;
  categoryName: string | null;
  isAvailableAtStore: boolean;
}

export interface ConsumerCatalogPage {
  items: ConsumerCatalogItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ConsumerContentContext {
  tenantId: string;
  storeId: string;
}

export type MappedConsumerBanner = ConsumerNewsItem;
export type MappedConsumerPromotion = NewsPromotionProduct;
