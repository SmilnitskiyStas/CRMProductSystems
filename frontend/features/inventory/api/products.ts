import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { BarcodeProductLookup, CreateProductPayload, Product, UpdateProductPayload } from "../types";

export const productsApi = {
  getAll: (params?: { search?: string; ids?: string[]; pageSize?: number }) => {
    const qs = new URLSearchParams();
    if (params?.search) qs.set("search", params.search);
    if (params?.ids?.length) for (const id of params.ids) qs.append("ids", id);
    if (params?.pageSize) qs.set("pageSize", String(params.pageSize));
    const q = qs.toString();
    return api.get<PagedResult<Product>>(`/api/items${q ? `?${q}` : ""}`).then((r) => r.items);
  },
  getById: (id: string) => api.get<Product>(`/api/items/${id}`),
  create: (payload: CreateProductPayload) => api.post<Product>("/api/items", payload),
  update: (id: string, payload: UpdateProductPayload) => api.put<Product>(`/api/items/${id}`, payload),
  delete: (id: string) => api.delete<void>(`/api/items/${id}`),
  lookupByBarcode: (barcode: string): Promise<BarcodeProductLookup> =>
    api.get<BarcodeProductLookup>(`/api/items/lookup?barcode=${encodeURIComponent(barcode)}`),
  uploadImage: (id: string, file: File): Promise<{ imageUrl: string }> => {
    const form = new FormData();
    form.append("file", file);
    return api.postForm<{ imageUrl: string }>(`/api/items/${id}/image`, form);
  },
};
