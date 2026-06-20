import { apiClient } from '@/lib/api-client';
import type {
  PagedResult,
  SupplierItem,
  SupplierListItem,
  SupplierProfile,
  SupplierReview,
} from './types';

export async function getSuppliers(params: {
  region?: string;
  category?: string;
  plan?: string;
  page?: number;
  pageSize?: number;
}): Promise<PagedResult<SupplierListItem>> {
  const { data } = await apiClient.get('/marketplace/suppliers', { params });
  return data;
}

export async function searchSuppliers(
  itemName: string,
  region?: string,
): Promise<SupplierListItem[]> {
  const { data } = await apiClient.post('/marketplace/search', { itemName, region });
  return data;
}

export async function getSupplierById(id: string): Promise<SupplierProfile> {
  const { data } = await apiClient.get(`/marketplace/suppliers/${id}`);
  return data;
}

export async function getSupplierItems(id: string): Promise<SupplierItem[]> {
  const { data } = await apiClient.get(`/marketplace/suppliers/${id}/items`);
  return data;
}

export async function createReview(
  supplierId: string,
  rating: number,
  comment: string | null,
): Promise<SupplierReview> {
  const { data } = await apiClient.post(`/marketplace/suppliers/${supplierId}/reviews`, {
    rating,
    comment: comment || null,
  });
  return data;
}
