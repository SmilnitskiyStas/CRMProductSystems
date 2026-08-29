import { apiClient } from '@/lib/api-client';
import type { StockBatch, CreateStockBatchRequest, CatalogProductLookup } from '../types';

interface ProductStockWire {
  id: string;
  productId: string;
  productName: string;
  productBarcode: string | null;
  storeId: string;
  zoneName: string | null;
  shelfNumber: number | null;
  batchNumber: string | null;
  quantity: number;
  expiryDate: string;
  daysLeft: number;
  status: StockBatch['status'];
  lastCheckedAt: string;
  pricePurchase: number | null;
  priceRetail: number | null;
  defaultReimbursementType: 'fixed' | 'percent' | null;
  defaultReimbursementValue: number | null;
}

interface StockPageWire {
  items: ProductStockWire[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

function mapStockBatch(item: ProductStockWire): StockBatch {
  return {
    id: item.id,
    productId: item.productId,
    productName: item.productName,
    barcode: item.productBarcode,
    batchNumber: item.batchNumber,
    quantity: item.quantity,
    expiryDate: item.expiryDate,
    daysLeft: item.daysLeft,
    status: item.status,
    locationId: item.storeId,
    zoneName: item.zoneName,
    shelfNumber: item.shelfNumber,
    lastCheckedAt: item.lastCheckedAt,
    pricePurchase: item.pricePurchase ?? null,
    priceRetail: item.priceRetail ?? null,
    defaultReimbursementType: item.defaultReimbursementType ?? null,
    defaultReimbursementValue: item.defaultReimbursementValue ?? null,
  };
}

export async function getStock(params?: {
  status?: string;
  locationId?: string;
  zone_id?: string;
}): Promise<StockBatch[]> {
  // Backend query param is still `store_id` (StockController never got the v4
  // Store→Location rename) — sending `locationId` was silently ignored,
  // returning unfiltered stock across every location.
  const { data } = await apiClient.get<StockPageWire>('/stock', {
    params: {
      status: params?.status,
      store_id: params?.locationId,
      zone_id: params?.zone_id,
    },
  });
  return Array.isArray(data.items) ? data.items.map(mapStockBatch) : [];
}

export async function getStockBatch(id: string): Promise<StockBatch> {
  const { data } = await apiClient.get<ProductStockWire>(`/stock/${id}`);
  return mapStockBatch(data);
}

export async function createStockBatch(body: CreateStockBatchRequest): Promise<StockBatch> {
  const { locationId, ...rest } = body;
  const { data } = await apiClient.post<ProductStockWire>('/stock', {
    ...rest,
    storeId: locationId,
  });
  return mapStockBatch(data);
}

export async function verifyBatch(id: string): Promise<void> {
  await apiClient.post(`/stock/${id}/verify`);
}

export async function getProductByBarcode(barcode: string): Promise<CatalogProductLookup> {
  // Camera/QR payloads occasionally include surrounding whitespace or formatted EAN dashes.
  // Backend lookup is an exact JSONB string match, so normalize presentation characters here.
  const normalized = barcode.trim().replace(/[\s-]+/g, '');
  if (!normalized) throw new Error('EMPTY_BARCODE');
  const { data } = await apiClient.get<CatalogProductLookup>(`/items/by-barcode/${encodeURIComponent(normalized)}`);
  return {
    ...data,
    barcodes: data.barcodes ?? [],
    barcode: data.barcode ?? data.barcodes?.[0] ?? normalized,
    price: data.price ?? data.priceRetail ?? undefined,
    pricePurchase: data.pricePurchase ?? null,
    priceRetail: data.priceRetail ?? null,
    defaultReimbursementType: data.defaultReimbursementType ?? null,
    defaultReimbursementValue: data.defaultReimbursementValue ?? null,
  };
}
