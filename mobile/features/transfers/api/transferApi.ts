import { apiClient } from '@/lib/api-client';
import type { Transfer, CreateTransferPayload, LocationOption } from '../types';

export async function getTransfers(locationId?: string, status?: string): Promise<Transfer[]> {
  // Backend query param is still `store_id` (TransfersController never got the
  // v4 Store→Location rename) — `location_id` is silently ignored, unfiltered.
  const { data } = await apiClient.get<Transfer[]>('/transfers', {
    params: { store_id: locationId, status },
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

export async function getLocations(): Promise<LocationOption[]> {
  const { data } = await apiClient.get<LocationOption[]>('/locations');
  return data;
}
