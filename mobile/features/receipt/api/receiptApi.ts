import { apiClient } from '@/lib/api-client';
import type { Receipt } from '../types';

export async function getReceipts(): Promise<Receipt[]> {
  const { data } = await apiClient.get<Receipt[]>('/receipts');
  return data;
}

export async function getReceipt(id: string): Promise<Receipt> {
  const { data } = await apiClient.get<Receipt>(`/receipts/${id}`);
  return data;
}

export async function confirmReceipt(id: string): Promise<Receipt> {
  const { data } = await apiClient.put<Receipt>(`/receipts/${id}/receive`);
  return data;
}
