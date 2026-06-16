import { api } from "@/lib/api";
import type { CatalogProductDto } from "../types";

export const catalogApi = {
  getAll: (params?: { category_id?: string; management_type?: string }) => {
    const qs = new URLSearchParams();
    if (params?.category_id) qs.set("category_id", params.category_id);
    if (params?.management_type) qs.set("management_type", params.management_type);
    const q = qs.toString();
    return api.get<CatalogProductDto[]>(`/api/items${q ? `?${q}` : ""}`);
  },

  getById: (id: string) => api.get<CatalogProductDto>(`/api/items/${id}`),

  getByBarcode: (code: string) => api.get<CatalogProductDto>(`/api/items/by-barcode/${code}`),
};
