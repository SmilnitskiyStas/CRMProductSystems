import { apiClient } from '@/lib/api-client';
import type { StockBatch, CreateStockBatchRequest } from '../types';

export async function getStock(params?: {
  status?: string;
  locationId?: string;
  zone_id?: string;
}): Promise<StockBatch[]> {
  const { data } = await apiClient.get<StockBatch[]>('/stock', { params });
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
  const { data } = await apiClient.get(`/products/by-barcode/${barcode}`);
  return data;
}
