import { api } from "@/lib/api";
import type { CreateProductPayload, Product, UpdateProductPayload } from "../types";

export const productsApi = {
  getAll: () => api.get<Product[]>("/api/products"),
  getById: (id: string) => api.get<Product>(`/api/products/${id}`),
  create: (payload: CreateProductPayload) => api.post<Product>("/api/products", payload),
  update: (id: string, payload: UpdateProductPayload) => api.put<Product>(`/api/products/${id}`, payload),
  delete: (id: string) => api.delete<void>(`/api/products/${id}`),
};
