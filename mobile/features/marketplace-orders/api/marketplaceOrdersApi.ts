import { apiClient } from '@/lib/api-client';
import type {
  CatalogProduct,
  MarketplaceOrder,
  MarketplaceOrderReceipt,
  UpdateReceiptItemRequest,
} from '../types';

const base = '/marketplace/orders';

interface CatalogProductWire {
  id: string;
  name: string;
  barcodes: string[];
}

interface CatalogPageWire {
  items: CatalogProductWire[];
}

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
  idempotencyKey?: string,
): Promise<MarketplaceOrderReceipt> {
  const { data } = await apiClient.put<MarketplaceOrderReceipt>(
    `${base}/${orderId}/receipt/items/${itemId}`,
    body,
    { headers: idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : undefined },
  );
  return data;
}

export async function finalizeMarketplaceReceipt(orderId: string, idempotencyKey?: string): Promise<MarketplaceOrderReceipt> {
  const { data } = await apiClient.post<MarketplaceOrderReceipt>(`${base}/${orderId}/receipt/finalize`, undefined, {
    headers: idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : undefined,
  });
  return data;
}

export async function searchCatalogProducts(search: string): Promise<CatalogProduct[]> {
  const { data } = await apiClient.get<CatalogPageWire>('/items', {
    params: { search: search.trim(), page: 1, pageSize: 50 },
  });
  return Array.isArray(data.items)
    ? data.items.map((item) => ({
        id: item.id,
        name: item.name,
        barcode: item.barcodes[0] ?? null,
      }))
    : [];
}
