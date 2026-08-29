import { apiClient } from '@/lib/api-client';
import type { Receipt } from '../types';

export async function getReceipts(locationId?: string): Promise<Receipt[]> {
  const { data } = await apiClient.get<{ items: Receipt[] }>('/receipts', {
    params: { store_id: locationId },
  });
  return Array.isArray(data.items) ? data.items : [];
}

export async function getReceipt(id: string): Promise<Receipt> {
  const { data } = await apiClient.get<Receipt>(`/receipts/${id}`);
  return data;
}

export async function confirmReceipt(id: string, idempotencyKey?: string): Promise<Receipt> {
  const { data } = await apiClient.put<Receipt>(`/receipts/${id}/receive`, undefined, {
    headers: idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : undefined,
  });
  return data;
}

/** Quick-accept a line: received = ordered. */
export async function processItem(
  receiptId: string,
  itemId: string,
  quantityReceived: number,
  idempotencyKey?: string,
): Promise<Receipt> {
  const { data } = await apiClient.put<Receipt>(`/receipts/${receiptId}/items`, {
    items: [{ itemId, quantityReceived }],
  }, { headers: idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : undefined });
  return data;
}
