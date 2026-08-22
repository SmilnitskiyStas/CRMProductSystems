import { apiClient } from '@/lib/api-client';
import type {
  CatalogProduct,
  MarketplaceOrder,
  MarketplaceOrderReceipt,
  UpdateReceiptItemRequest,
} from '../types';

const base = '/marketplace/orders';

export async function getAwaitingReceiptOrders(): Promise<MarketplaceOrder[]> {
  const { data } = await apiClient.get<MarketplaceOrder[]>(`${base}/awaiting-receipt`);
  return data;
}

export async function startMarketplaceReceipt(orderId: string): Promise<MarketplaceOrderReceipt> {
  const { data } = await apiClient.post<MarketplaceOrderReceipt>(`${base}/${orderId}/receipt`);
  return data;
}

export async function updateMarketplaceReceiptItem(
  orderId: string,
  itemId: string,
  body: UpdateReceiptItemRequest,
): Promise<MarketplaceOrderReceipt> {
  const { data } = await apiClient.put<MarketplaceOrderReceipt>(
    `${base}/${orderId}/receipt/items/${itemId}`,
    body,
  );
  return data;
}

export async function finalizeMarketplaceReceipt(orderId: string): Promise<MarketplaceOrderReceipt> {
  const { data } = await apiClient.post<MarketplaceOrderReceipt>(`${base}/${orderId}/receipt/finalize`);
  return data;
}

export async function searchCatalogProducts(search: string): Promise<CatalogProduct[]> {
  const { data } = await apiClient.get<CatalogProduct[]>('/items', { params: { search } });
  return Array.isArray(data) ? data : [];
}
