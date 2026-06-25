import { api } from "@/lib/api";
import type { CreateProductPayload, Product, UpdateProductPayload } from "../types";

interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const productsApi = {
  getAll: () =>
    api.get<PagedResult<Product>>("/api/items").then((r) => r.items),
  getById: (id: string) => api.get<Product>(`/api/items/${id}`),
  create: (payload: CreateProductPayload) => api.post<Product>("/api/items", payload),
  update: (id: string, payload: UpdateProductPayload) => api.put<Product>(`/api/items/${id}`, payload),
  delete: (id: string) => api.delete<void>(`/api/items/${id}`),
};
