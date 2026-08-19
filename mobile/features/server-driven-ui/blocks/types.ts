export interface HeroBannerProps {
  title: string;
  subtitle?: string;
  imageUrl?: string;
  eyebrow?: string;
}

export interface BannerItem {
  id: string;
  title: string;
  subtitle?: string;
  imageUrl?: string;
}

export interface BannerCarouselProps {
  items: BannerItem[];
}

export interface LoyaltyCardProps {
  title?: string;
  balance: number;
  cardNumber?: string;
  tier?: string;
}

export interface LoyaltyBalanceProps {
  label?: string;
  balance: number;
  unit?: string;
}

export interface PromotionItem {
  id: string;
  title: string;
  subtitle?: string;
  imageUrl?: string;
  badge?: string;
}

export interface PromotionCollectionProps {
  items: PromotionItem[];
  columns?: 2 | 3;
}

export interface ProductItem {
  id: string;
  name: string;
  price: number;
  oldPrice?: number;
  unit?: string;
  imageUrl?: string;
}

export interface ProductCollectionProps {
  items: ProductItem[];
  columns?: 2 | 3;
}

export interface SectionHeaderProps {
  title: string;
  subtitle?: string;
  actionLabel?: string;
}

export interface QuickActionItem {
  id: string;
  label: string;
  icon?: string;
}

export interface QuickActionsProps {
  items: QuickActionItem[];
}

export interface NewsItem {
  id: string;
  title: string;
  summary?: string;
  publishedAt?: string;
  imageUrl?: string;
}

export interface NewsListProps {
  items: NewsItem[];
}

export interface StoreItem {
  id: string;
  name: string;
  address?: string;
  distanceKm?: number;
  openNow?: boolean;
}

export interface StoreListProps {
  items: StoreItem[];
}
