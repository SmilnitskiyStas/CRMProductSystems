import { apiClient } from '@/lib/api-client';
import type { WriteOff, CreateWriteOffPayload } from '../types';

type WriteOffWire = Omit<WriteOff, 'locationId' | 'locationName'> & {
  storeId: string;
  storeName: string;
};

const mapWriteOff = ({ storeId, storeName, ...writeOff }: WriteOffWire): WriteOff => ({
  ...writeOff,
  locationId: storeId,
  locationName: storeName,
});

export async function getWriteOffs(locationId?: string, status?: string): Promise<WriteOff[]> {
  // Backend query param is still `store_id` (WriteOffsController never got the
  // v4 Store→Location rename) — `location_id` is silently ignored, unfiltered.
  const { data } = await apiClient.get<WriteOffWire[]>('/write-offs', {
    params: { store_id: locationId, status },
  });
  return data.map(mapWriteOff);
}

export async function getWriteOff(id: string): Promise<WriteOff> {
  const { data } = await apiClient.get<WriteOffWire>(`/write-offs/${id}`);
  return mapWriteOff(data);
}

export async function createWriteOff(payload: CreateWriteOffPayload, idempotencyKey?: string): Promise<WriteOff> {
  const { locationId, ...body } = payload;
  const { data } = await apiClient.post<WriteOffWire>(
    '/write-offs',
    { ...body, storeId: locationId },
    { headers: idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : undefined },
  );
  return mapWriteOff(data);
}

export async function approveWriteOff(id: string): Promise<WriteOff> {
  const { data } = await apiClient.put<WriteOffWire>(`/write-offs/${id}/approve`);
  return mapWriteOff(data);
}

export async function rejectWriteOff(id: string): Promise<WriteOff> {
  const { data } = await apiClient.put<WriteOffWire>(`/write-offs/${id}/reject`);
  return mapWriteOff(data);
}
