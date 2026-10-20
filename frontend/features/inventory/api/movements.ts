import { api } from "@/lib/api";

export interface MovementDto {
  id: string;
  movementType: "receipt" | "transfer" | "write_off" | "adjustment";
  productId: string;
  productName: string;
  fromStoreId: string | null;
  fromStoreName: string | null;
  toStoreId: string | null;
  toStoreName: string | null;
  quantity: number;
  quantityBefore: number | null;
  quantityAfter: number | null;
  unitPrice: number | null;
  totalAmount: number | null;
  referenceId: string | null;
  referenceType: string | null;
  notes: string | null;
  createdAt: string;
  performedById: string | null;
  performedByName: string | null;
  batchNumber: string | null;
  batchExpiryDate: string | null;
}

/** Supply on its way to a store (not in stock yet): internal order or open marketplace order. */
export interface IncomingSupplyDto {
  source: "receipt" | "marketplace";
  id: string;
  number: string | null;
  status: string;
  quantity: number;
  unit: string | null;
  orderedAt: string;
  expectedAt: string | null;
  shippedAt: string | null;
  supplierName: string | null;
  destinationStoreId: string | null;
  destinationStoreName: string | null;
}

export interface MovementPageDto {
  items: MovementDto[];
  total: number;
  page: number;
  pageSize: number;
}

export const movementsApi = {
  getIncoming: (productId: string, storeId?: string) => {
    const qs = new URLSearchParams({ product_id: productId });
    if (storeId) qs.set("store_id", storeId);
    return api.get<IncomingSupplyDto[]>(`/api/movements/incoming?${qs}`);
  },

  getByProduct: (
    productId: string,
    params?: { from?: string; to?: string; page_size?: number; store_id?: string },
  ) => {
    const qs = new URLSearchParams({ product_id: productId });
    if (params?.from) qs.set("from", params.from);
    if (params?.to) qs.set("to", params.to);
    if (params?.page_size) qs.set("page_size", String(params.page_size));
    if (params?.store_id) qs.set("store_id", params.store_id);
    return api.get<MovementPageDto>(`/api/movements?${qs}`);
  },
};
