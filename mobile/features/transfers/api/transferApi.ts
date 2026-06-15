import { apiClient } from '@/lib/api-client';
import type { Transfer, CreateTransferPayload, StoreOption } from '../types';

export async function getTransfers(storeId?: string, status?: string): Promise<Transfer[]> {
  const { data } = await apiClient.get<Transfer[]>('/transfers', {
    params: { store_id: storeId, status },
  });
  return data;
}

export async function getTransfer(id: string): Promise<Transfer> {
  const { data } = await apiClient.get<Transfer>(`/transfers/${id}`);
  return data;
}

export async function createTransfer(payload: CreateTransferPayload): Promise<Transfer> {
  const { data } = await apiClient.post<Transfer>('/transfers', payload);
  return data;
}

export async function confirmTransfer(id: string): Promise<Transfer> {
  const { data } = await apiClient.put<Transfer>(`/transfers/${id}/confirm`);
  return data;
}

export async function cancelTransfer(id: string): Promise<Transfer> {
  const { data } = await apiClient.put<Transfer>(`/transfers/${id}/cancel`);
  return data;
}

export async function getStores(): Promise<StoreOption[]> {
  const { data } = await apiClient.get<StoreOption[]>('/stores');
  return data;
}
