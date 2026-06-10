import { api } from "@/lib/api";
import type {
  TenantSummaryDto,
  TenantDetailDto,
  ProviderHealthDto,
  ProviderLogDto,
  ImpersonateResponse,
} from "../types";

export const providerApi = {
  /** GET /api/provider/tenants */
  getTenants: (): Promise<TenantSummaryDto[]> =>
    api.get<TenantSummaryDto[]>("/api/provider/tenants"),

  /** GET /api/provider/tenants/:id */
  getTenant: (id: string): Promise<TenantDetailDto> =>
    api.get<TenantDetailDto>(`/api/provider/tenants/${id}`),

  /** PUT /api/provider/tenants/:id/plan */
  updatePlan: (id: string, plan: string): Promise<void> =>
    api.put<void>(`/api/provider/tenants/${id}/plan`, { plan }),

  /** PUT /api/provider/tenants/:id/modules */
  updateModules: (id: string, modules: string[]): Promise<void> =>
    api.put<void>(`/api/provider/tenants/${id}/modules`, { modules }),

  /** POST /api/provider/tenants/:id/impersonate */
  impersonate: (id: string): Promise<ImpersonateResponse> =>
    api.post<ImpersonateResponse>(`/api/provider/tenants/${id}/impersonate`),

  /** DELETE /api/provider/tenants/:id/impersonate */
  endImpersonate: (id: string): Promise<void> =>
    api.delete<void>(`/api/provider/tenants/${id}/impersonate`),

  /** GET /api/provider/health */
  getHealth: (): Promise<ProviderHealthDto> =>
    api.get<ProviderHealthDto>("/api/provider/health"),

  /** GET /api/provider/logs?limit=N */
  getLogs: (limit = 100): Promise<ProviderLogDto[]> =>
    api.get<ProviderLogDto[]>(`/api/provider/logs?limit=${limit}`),
};
