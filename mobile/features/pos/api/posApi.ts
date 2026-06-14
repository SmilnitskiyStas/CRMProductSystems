import { apiClient } from '@/lib/api-client';
import type {
  PosShift,
  SaleRequest,
  SaleResponse,
  SaleListItem,
} from '../types';

export async function getCurrentShift(): Promise<PosShift | null> {
  try {
    const { data } = await apiClient.get<PosShift>('/pos/shifts/current');
    return data;
  } catch (err: unknown) {
    // 404 means no open shift
    const axiosErr = err as { response?: { status?: number } };
    if (axiosErr?.response?.status === 404) return null;
    throw err;
  }
}

export interface StoreOption {
  id: string;
  name: string;
}

export async function getStores(): Promise<StoreOption[]> {
  const { data } = await apiClient.get<StoreOption[]>('/stores');
  return data;
}

export async function openShift(storeId: string): Promise<PosShift> {
  const { data } = await apiClient.post<PosShift>('/pos/shifts/open', { storeId });
  return data;
}

export async function closeShift(): Promise<PosShift> {
  const { data } = await apiClient.post<PosShift>('/pos/shifts/close');
  return data;
}

export async function createSale(body: SaleRequest): Promise<SaleResponse> {
  const { data } = await apiClient.post<SaleResponse>('/pos/sales', body);
  return data;
}

export async function getSales(shiftId: string): Promise<SaleListItem[]> {
  const { data } = await apiClient.get<SaleListItem[]>('/pos/sales', {
    params: { shiftId },
  });
  return data;
}
