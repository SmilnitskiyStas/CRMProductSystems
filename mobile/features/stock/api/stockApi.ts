import { apiClient } from '@/lib/api-client';
import type { StockBatch, CreateStockBatchRequest } from '../types';

export async function getStock(params?: {
  status?: string;
  locationId?: string;
  zone_id?: string;
}): Promise<StockBatch[]> {
  // Backend query param is still `store_id` (StockController never got the v4
  // Store→Location rename) — sending `locationId` was silently ignored,
  // returning unfiltered stock across every location.
  const { data } = await apiClient.get<StockBatch[]>('/stock', {
    params: {
      status: params?.status,
      store_id: params?.locationId,
      zone_id: params?.zone_id,
    },
  });
  return data;
}

export async function getStockBatch(id: string): Promise<StockBatch> {
  const { data } = await apiClient.get<StockBatch>(`/stock/${id}`);
  return data;
}

export async function createStockBatch(body: CreateStockBatchRequest): Promise<StockBatch> {
  const { data } = await apiClient.post<StockBatch>('/stock', body);
  return data;
}

export async function verifyBatch(id: string): Promise<void> {
  await apiClient.post(`/stock/${id}/verify`);
}

export async function getProductByBarcode(barcode: string) {
  const { data } = await apiClient.get(`/items/by-barcode/${barcode}`);
  return data;
}
