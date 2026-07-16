import { apiClient } from '@/lib/api-client';
import type { WriteOff, CreateWriteOffPayload } from '../types';

export async function getWriteOffs(locationId?: string, status?: string): Promise<WriteOff[]> {
  // Backend query param is still `store_id` (WriteOffsController never got the
  // v4 Store→Location rename) — `location_id` is silently ignored, unfiltered.
  const { data } = await apiClient.get<WriteOff[]>('/write-offs', {
    params: { store_id: locationId, status },
  });
  return data;
}

export async function getWriteOff(id: string): Promise<WriteOff> {
  const { data } = await apiClient.get<WriteOff>(`/write-offs/${id}`);
  return data;
}

export async function createWriteOff(payload: CreateWriteOffPayload): Promise<WriteOff> {
  const { data } = await apiClient.post<WriteOff>('/write-offs', payload);
  return data;
}

export async function approveWriteOff(id: string): Promise<WriteOff> {
  const { data } = await apiClient.put<WriteOff>(`/write-offs/${id}/approve`);
  return data;
}

export async function rejectWriteOff(id: string): Promise<WriteOff> {
  const { data } = await apiClient.put<WriteOff>(`/write-offs/${id}/reject`);
  return data;
}
