import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { BarcodeProductLookup, CreateProductPayload, Product, UpdateProductPayload } from "../types";

export const productsApi = {
  getAll: (params?: {
    search?: string;
    ids?: string[];
    category_id?: string;
    uncategorized?: boolean;
    min_price?: number;
    max_price?: number;
    page?: number;
    pageSize?: number;
    sortBy?: string;
    sortDescending?: boolean;
  }) => {
    const qs = new URLSearchParams();
    if (params?.search) qs.set("search", params.search);
    if (params?.ids?.length) for (const id of params.ids) qs.append("ids", id);
    if (params?.category_id) qs.set("category_id", params.category_id);
    if (params?.uncategorized) qs.set("uncategorized", "true");
    // `!= null` (never a truthy check) — 0 is a valid bound and must not be treated as unset.
    if (params?.min_price != null) qs.set("min_price", String(params.min_price));
    if (params?.max_price != null) qs.set("max_price", String(params.max_price));
    if (params?.page) qs.set("page", String(params.page));
    if (params?.pageSize) qs.set("pageSize", String(params.pageSize));
    if (params?.sortBy) qs.set("sortBy", params.sortBy);
    if (params?.sortDescending !== undefined) qs.set("sortDescending", String(params.sortDescending));
    const q = qs.toString();
    // Was: `.then((r) => r.items)`, silently dropping totalCount/page/pageSize and never sending
    // page/pageSize itself — the Catalog page was capped at the backend's default pageSize=50
    // with no indication more products existed. Now returns the full envelope; callers that only
    // ever wanted the flat list use `useProducts()`'s `select`, not this function directly.
    return api.get<PagedResult<Product>>(`/api/items${q ? `?${q}` : ""}`);
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
