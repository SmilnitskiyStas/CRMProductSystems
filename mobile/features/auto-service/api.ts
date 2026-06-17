import { apiClient } from '@/lib/api-client';
import type {
  AsWorkOrder,
  AsCustomer,
  AsServiceCatalogItem,
  CreateWorkOrderLinePayload,
  CreateCustomerPayload,
  SparePartItem,
} from './types';

// Work Orders
export async function getWorkOrders(status?: string): Promise<AsWorkOrder[]> {
  const { data } = await apiClient.get<AsWorkOrder[]>('/auto-service/work-orders', {
    params: status ? { status } : undefined,
  });
  return data;
}

export async function getWorkOrder(id: string): Promise<AsWorkOrder> {
  const { data } = await apiClient.get<AsWorkOrder>(`/auto-service/work-orders/${id}`);
  return data;
}

export async function completeWorkOrder(id: string): Promise<AsWorkOrder> {
  const { data } = await apiClient.post<AsWorkOrder>(
    `/auto-service/work-orders/${id}/complete`
  );
  return data;
}

export async function addWorkOrderLine(
  workOrderId: string,
  payload: CreateWorkOrderLinePayload
): Promise<AsWorkOrder> {
  const { data } = await apiClient.post<AsWorkOrder>(
    `/auto-service/work-orders/${workOrderId}/lines`,
    payload
  );
  return data;
}

// Customers
export async function getCustomers(): Promise<AsCustomer[]> {
  const { data } = await apiClient.get<AsCustomer[]>('/auto-service/customers');
  return data;
}

export async function createCustomer(payload: CreateCustomerPayload): Promise<AsCustomer> {
  const { data } = await apiClient.post<AsCustomer>('/auto-service/customers', payload);
  return data;
}

// Service catalog
export async function getServiceCatalog(): Promise<AsServiceCatalogItem[]> {
  const { data } = await apiClient.get<AsServiceCatalogItem[]>(
    '/auto-service/service-catalog'
  );
  return data;
}

// Spare parts lookup by barcode
export async function getSparePartByBarcode(barcode: string): Promise<SparePartItem | null> {
  const { data } = await apiClient.get<SparePartItem[]>('/items', {
    params: { barcode, item_type: 'spare_part' },
  });
  return Array.isArray(data) && data.length > 0 ? data[0] : null;
}

// Text search for spare parts
export async function searchSpareParts(query: string): Promise<SparePartItem[]> {
  const { data } = await apiClient.get<SparePartItem[]>('/items', {
    params: { search: query, item_type: 'spare_part' },
  });
  return data;
}
