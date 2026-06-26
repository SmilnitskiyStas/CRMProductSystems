import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { BarcodeProductLookup, CreateProductPayload, Product, UpdateProductPayload } from "../types";

export const productsApi = {
  getAll: () =>
    api.get<PagedResult<Product>>("/api/items").then((r) => r.items),
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
