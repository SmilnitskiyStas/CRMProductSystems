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
}

export interface MovementPageDto {
  items: MovementDto[];
  total: number;
  page: number;
  pageSize: number;
}

export const movementsApi = {
  getByProduct: (
    productId: string,
    params?: { from?: string; to?: string; page_size?: number },
  ) => {
    const qs = new URLSearchParams({ product_id: productId });
    if (params?.from) qs.set("from", params.from);
    if (params?.to) qs.set("to", params.to);
    if (params?.page_size) qs.set("page_size", String(params.page_size));
    return api.get<MovementPageDto>(`/api/movements?${qs}`);
  },
};
