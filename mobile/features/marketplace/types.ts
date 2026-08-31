import type { DeliveryCoverage } from '@/features/geo/types';

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

/** Measured average delivery time to one destination region (nightly worker job). */
export interface RegionDeliveryStat {
  regionCode: string;
  avgDeliveryDays: number;
  sampleSize: number;
}

export interface SupplierMetrics {
  rating: number | null;
  avgDeliveryDays: number | null;
  // NOTE: orderAccuracy / qualityScore arrive as 0–1 fractions from the backend
  // (`decimal?`), not percentages — multiply by 100 before rendering with "%".
  orderAccuracy: number | null;
  qualityScore: number | null;
  cancellationRate: number | null;
  responseTimeHours: number | null;
  updatedAt: string;
  // TASK-660: worker-computed delivery/response aggregates. All optional/nullable —
  // the nightly job may not have run yet, or a given metric may have no data.
  deliveryByRegion?: RegionDeliveryStat[] | null;
  deliverySampleSize?: number | null;
  responseSampleSize?: number | null;
  aggregatesComputedAt?: string | null;
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
  // TASK-660: supplier-declared delivery coverage. NOT premium-gated — populated for
  // every caller. Read-only on mobile.
  deliveryCoverage?: DeliveryCoverage | null;
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
