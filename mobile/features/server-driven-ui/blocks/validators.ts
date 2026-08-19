import type {
  BannerCarouselProps,
  BannerItem,
  HeroBannerProps,
  LoyaltyBalanceProps,
  LoyaltyCardProps,
  NewsItem,
  NewsListProps,
  ProductCollectionProps,
  ProductItem,
  PromotionCollectionProps,
  PromotionItem,
  QuickActionItem,
  QuickActionsProps,
  SectionHeaderProps,
  StoreItem,
  StoreListProps,
} from './types';

type UnknownRecord = Record<string, unknown>;

function record(value: unknown): value is UnknownRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function text(value: unknown, required = false): boolean {
  return value === undefined ? !required : typeof value === 'string' && value.length <= 500;
}

function finiteNumber(value: unknown, required = false): boolean {
  return value === undefined ? !required : typeof value === 'number' && Number.isFinite(value);
}

function imageUrl(value: unknown): boolean {
  return value === undefined || value === '' || (
    typeof value === 'string' &&
    value.length <= 2048 &&
    /^https:\/\/[^\s]+$/i.test(value)
  );
}

function arrayOf<T>(value: unknown, guard: (item: unknown) => item is T, limit = 50): value is T[] {
  return Array.isArray(value) && value.length <= limit && value.every(guard);
}

function banner(value: unknown): value is BannerItem {
  return record(value) && text(value.id, true) && text(value.title, true) && text(value.subtitle) && imageUrl(value.imageUrl);
}

function promotion(value: unknown): value is PromotionItem {
  return record(value) && text(value.id, true) && text(value.title, true) && text(value.subtitle) && imageUrl(value.imageUrl) && text(value.badge);
}

function product(value: unknown): value is ProductItem {
  return record(value) && text(value.id, true) && text(value.name, true) && finiteNumber(value.price, true) && finiteNumber(value.oldPrice) && text(value.unit) && imageUrl(value.imageUrl);
}

function quickAction(value: unknown): value is QuickActionItem {
  return record(value) && text(value.id, true) && text(value.label, true) && text(value.icon);
}

function news(value: unknown): value is NewsItem {
  return record(value) && text(value.id, true) && text(value.title, true) && text(value.summary) && text(value.publishedAt) && imageUrl(value.imageUrl);
}

function store(value: unknown): value is StoreItem {
  return record(value) && text(value.id, true) && text(value.name, true) && text(value.address) && finiteNumber(value.distanceKm) && (value.openNow === undefined || typeof value.openNow === 'boolean');
}

export function isHeroBannerProps(value: unknown): value is HeroBannerProps {
  return record(value) && text(value.title, true) && text(value.subtitle) && imageUrl(value.imageUrl) &&
    text(value.eyebrow) && text(value.ctaLabel) && imageUrl(value.ctaLink) && finiteNumber(value.heightPx);
}

export function isBannerCarouselProps(value: unknown): value is BannerCarouselProps {
  return record(value) && arrayOf(value.items, banner, 20) && finiteNumber(value.cardWidthPx);
}

export function isLoyaltyCardProps(value: unknown): value is LoyaltyCardProps {
  return record(value) && text(value.title) && finiteNumber(value.balance, true) && text(value.cardNumber) && text(value.tier);
}

export function isLoyaltyBalanceProps(value: unknown): value is LoyaltyBalanceProps {
  return record(value) && text(value.label) && finiteNumber(value.balance, true) && text(value.unit);
}

function validColumns(value: unknown): value is 2 | 3 | undefined {
  return value === undefined || value === 2 || value === 3;
}

export function isPromotionCollectionProps(value: unknown): value is PromotionCollectionProps {
  return record(value) && arrayOf(value.items, promotion) && validColumns(value.columns) && text(value.title) &&
    (value.showViewAll === undefined || typeof value.showViewAll === 'boolean') && finiteNumber(value.cardWidthPx);
}

export function isProductCollectionProps(value: unknown): value is ProductCollectionProps {
  return record(value) && arrayOf(value.items, product) && validColumns(value.columns) && text(value.title) &&
    (value.showViewAll === undefined || typeof value.showViewAll === 'boolean') && finiteNumber(value.cardWidthPx);
}

export function isSectionHeaderProps(value: unknown): value is SectionHeaderProps {
  return record(value) && text(value.title, true) && text(value.subtitle) && text(value.actionLabel) &&
    (value.alignment === undefined || value.alignment === 'left' || value.alignment === 'center');
}

export function isQuickActionsProps(value: unknown): value is QuickActionsProps {
  return record(value) && arrayOf(value.items, quickAction, 8);
}

export function isNewsListProps(value: unknown): value is NewsListProps {
  return record(value) && arrayOf(value.items, news, 20);
}

export function isStoreListProps(value: unknown): value is StoreListProps {
  return record(value) && arrayOf(value.items, store, 50) && text(value.title) &&
    (value.showDistance === undefined || typeof value.showDistance === 'boolean');
}
