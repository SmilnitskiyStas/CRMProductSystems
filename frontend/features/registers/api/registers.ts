import { api } from "@/lib/api";
import type {
  ActivationCode,
  CreateRegisterPayload,
  PagedResult,
  PosEmployee,
  Register,
  RegisterCreated,
  RegisterOperator,
  RegisterSession,
  RegisterShift,
  RegisterStats,
  SetPinResult,
  UpdateRegisterPayload,
} from "../types";

const BASE = "/api/pos/registers";

function qs(params: Record<string, string | number | undefined | null>): string {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== "") p.set(k, String(v));
  }
  const s = p.toString();
  return s ? `?${s}` : "";
}

export const registersApi = {
  list: (storeId?: string) => api.get<Register[]>(`${BASE}${qs({ storeId })}`),
  operators: (storeId?: string) => api.get<RegisterOperator[]>(`${BASE}/operators${qs({ storeId })}`),
  create: (data: CreateRegisterPayload) => api.post<RegisterCreated>(BASE, data),
  update: (id: string, data: UpdateRegisterPayload) => api.put<Register>(`${BASE}/${id}`, data),
  issueActivationCode: (id: string) => api.post<ActivationCode>(`${BASE}/${id}/activation-code`),
  revoke: (id: string) => api.post<void>(`${BASE}/${id}/revoke`),
  unlock: (id: string) => api.post<void>(`${BASE}/${id}/unlock`),

  employees: (storeId: string) => api.get<PosEmployee[]>(`${BASE}/employees${qs({ storeId })}`),
  generatePin: (userId: string) => api.post<SetPinResult>(`${BASE}/pins/${userId}`),
  clearPin: (userId: string) => api.delete<void>(`${BASE}/pins/${userId}`),

  stats: (fromIso: string, toIso: string, storeId?: string) =>
    api.get<RegisterStats[]>(`${BASE}/stats${qs({ from: fromIso, to: toIso, storeId })}`),
  sessions: (id: string, page: number, pageSize: number) =>
    api.get<PagedResult<RegisterSession>>(`${BASE}/${id}/sessions${qs({ page, pageSize })}`),
  shifts: (id: string, page: number, pageSize: number) =>
    api.get<PagedResult<RegisterShift>>(`${BASE}/${id}/shifts${qs({ page, pageSize })}`),
};
