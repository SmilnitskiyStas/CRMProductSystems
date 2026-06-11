import { api } from "@/lib/api";
import type { CsvImportResult, DailySale, SalesFilters, UpsertDailySalePayload } from "../types";

function query(filters: SalesFilters): string {
  const params = new URLSearchParams();
  if (filters.storeId) params.set("store_id", filters.storeId);
  if (filters.productId) params.set("product_id", filters.productId);
  if (filters.from) params.set("from", filters.from);
  if (filters.to) params.set("to", filters.to);
  const q = params.toString();
  return q ? `?${q}` : "";
}

export const salesApi = {
  getAll: (filters: SalesFilters) =>
    api.get<DailySale[]>(`/api/daily-sales${query(filters)}`),

  upsert: (payload: UpsertDailySalePayload) =>
    api.post<DailySale>("/api/daily-sales", payload),

  importCsv: (storeId: string, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return api.postForm<CsvImportResult>(
      `/api/daily-sales/import?store_id=${storeId}`,
      form,
    );
  },

  markAnomaly: (id: string, isAnomaly: boolean) =>
    api.put<DailySale>(`/api/daily-sales/${id}/mark-anomaly`, { isAnomaly }),
};
