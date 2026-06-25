import { api } from "@/lib/api";
import type { CatalogProductDto } from "../types";

interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const catalogApi = {
  getAll: (params?: { category_id?: string; management_type?: string }) => {
    const qs = new URLSearchParams();
    if (params?.category_id) qs.set("category_id", params.category_id);
    if (params?.management_type) qs.set("management_type", params.management_type);
    const q = qs.toString();
    return api
      .get<PagedResult<CatalogProductDto>>(`/api/items${q ? `?${q}` : ""}`)
      .then((r) => r.items);
  },

  getById: (id: string) => api.get<CatalogProductDto>(`/api/items/${id}`),

  getByBarcode: (code: string) => api.get<CatalogProductDto>(`/api/items/by-barcode/${code}`),
};
