import { api } from "@/lib/api";
import type { PlatformCategoryDto, CreateCategoryBody, UpdateCategoryBody } from "../types";

// Provider (super admin) CRUD over the global `platform_categories` tree (B2/B3).
// No tenant scoping — one curated tree for every tenant.
export const providerCategoriesApi = {
  list:   () => api.get<PlatformCategoryDto[]>("/api/provider/categories"),
  create: (b: CreateCategoryBody) => api.post<PlatformCategoryDto>("/api/provider/categories", b),
  update: (id: string, b: UpdateCategoryBody) =>
    api.put<PlatformCategoryDto>(`/api/provider/categories/${id}`, b),
  remove: (id: string) => api.delete<void>(`/api/provider/categories/${id}`),
};
