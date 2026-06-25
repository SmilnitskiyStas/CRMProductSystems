import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { CreateProductPayload, Product, UpdateProductPayload } from "../types";

export const productsApi = {
  getAll: () =>
    api.get<PagedResult<Product>>("/api/items").then((r) => r.items),
  getById: (id: string) => api.get<Product>(`/api/items/${id}`),
  create: (payload: CreateProductPayload) => api.post<Product>("/api/items", payload),
  update: (id: string, payload: UpdateProductPayload) => api.put<Product>(`/api/items/${id}`, payload),
  delete: (id: string) => api.delete<void>(`/api/items/${id}`),
};
