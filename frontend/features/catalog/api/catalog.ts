import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { CatalogProductDto, CategorySearchResult } from "../types";

export const catalogApi = {
  getAll: (params?: { category_id?: string; management_type?: string; search?: string; ids?: string[] }) => {
    const qs = new URLSearchParams();
    if (params?.category_id) qs.set("category_id", params.category_id);
    if (params?.management_type) qs.set("management_type", params.management_type);
    if (params?.search) qs.set("search", params.search);
    if (params?.ids?.length) for (const id of params.ids) qs.append("ids", id);
    const q = qs.toString();
    return api
      .get<PagedResult<CatalogProductDto>>(`/api/items${q ? `?${q}` : ""}`)
      .then((r) => r.items);
  },

  getById: (id: string) => api.get<CatalogProductDto>(`/api/items/${id}`),

  getByBarcode: (code: string) => api.get<CatalogProductDto>(`/api/items/by-barcode/${code}`),

  /** GET /api/categories/search — case-insensitive name match over all active platform
   *  categories, for the category typeahead (Phase 6e). `limit` caps at 50 server-side. */
  searchCategories: (q: string, limit = 20) => {
    const qs = new URLSearchParams({ q, limit: String(limit) });
    return api.get<CategorySearchResult[]>(`/api/categories/search?${qs.toString()}`);
  },
};
