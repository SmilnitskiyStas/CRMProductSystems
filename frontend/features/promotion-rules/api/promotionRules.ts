import { api } from "@/lib/api";
import type {
  LookupItem,
  RuleDetail,
  RuleListItem,
  RuleStatus,
  SaveRulePayload,
  SimulatePayload,
  SimulateResult,
  ValidationResult,
} from "../types";

const BASE = "/api/promotion-rules";

function qs(params: Record<string, string | number | undefined | null>): string {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== "") p.set(k, String(v));
  }
  const s = p.toString();
  return s ? `?${s}` : "";
}

export const promotionRulesApi = {
  list: (status?: RuleStatus, storeId?: string) => api.get<RuleListItem[]>(`${BASE}${qs({ status, storeId })}`),
  get: (id: string) => api.get<RuleDetail>(`${BASE}/${id}`),
  create: (data: Omit<SaveRulePayload, "changeComment">) => api.post<RuleDetail>(BASE, data),
  update: (id: string, data: SaveRulePayload) => api.put<RuleDetail>(`${BASE}/${id}`, data),
  validate: (id: string) => api.post<ValidationResult>(`${BASE}/${id}/validate`),
  publish: (id: string) => api.post<RuleDetail>(`${BASE}/${id}/publish`),
  pause: (id: string) => api.post<RuleDetail>(`${BASE}/${id}/pause`),
  resume: (id: string) => api.post<RuleDetail>(`${BASE}/${id}/resume`),
  archive: (id: string) => api.post<RuleDetail>(`${BASE}/${id}/archive`),
  simulate: (data: SimulatePayload) => api.post<SimulateResult>(`${BASE}/simulate`, data),
  lookupItems: (search: string, categoryId?: string, limit = 20) =>
    api.get<LookupItem[]>(`${BASE}/lookups/items${qs({ search, categoryId, limit })}`),
  /** Contract extension: `?ids=a,b,c` (max 50) resolves items by id; `search` is ignored then. */
  lookupItemsByIds: (ids: string[]) =>
    api.get<LookupItem[]>(`${BASE}/lookups/items${qs({ ids: ids.slice(0, 50).join(",") })}`),
};
