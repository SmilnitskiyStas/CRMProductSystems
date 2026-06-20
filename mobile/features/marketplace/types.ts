export interface SupplierListItem {
  id: string;
  name: string;
  region: string | null;
  plan: 'free' | 'premium';
  categories: string[] | null;
  rating: number | null;
  avgDeliveryDays: number | null;
  isPublic: boolean;
}

export interface SupplierMetrics {
  rating: number | null;
  avgDeliveryDays: number | null;
  orderAccuracy: number | null;
  qualityScore: number | null;
  cancellationRate: number | null;
  responseTimeHours: number | null;
  updatedAt: string;
}

export interface SupplierProfile {
  supplierId: string;
  supplierName: string;
  region: string | null;
  categories: string[] | null;
  website: string | null;
  deliveryRegions: string[] | null;
  workingHours: string | null;
  paymentTerms: string | null;
  isPublic: boolean;
  plan: 'free' | 'premium';
  metrics: SupplierMetrics | null;
}

export interface SupplierItem {
  id: string;
  itemId: string | null;
  customName: string | null;
  itemName: string | null;
  price: number | null;
  minQty: number | null;
  unit: string | null;
  isAvailable: boolean;
}

export interface SupplierReview {
  id: string;
  rating: number;
  comment: string | null;
  createdAt: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
