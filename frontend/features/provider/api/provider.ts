import { api } from "@/lib/api";
import type {
  TenantSummaryDto,
  TenantDetailDto,
  CreateTenantRequest,
  ProviderHealthDto,
  ProviderLogsPageDto,
  ProviderLogsFilter,
  ImpersonateResponse,
  TenantUserDto,
  CreateTenantUserRequest,
} from "../types";

export const providerApi = {
  /** GET /api/provider/tenants */
  getTenants: (): Promise<TenantSummaryDto[]> =>
    api.get<TenantSummaryDto[]>("/api/provider/tenants"),

  /** GET /api/provider/tenants/:id */
  getTenant: (id: string): Promise<TenantDetailDto> =>
    api.get<TenantDetailDto>(`/api/provider/tenants/${id}`),

  /** POST /api/provider/tenants */
  createTenant: (req: CreateTenantRequest): Promise<TenantDetailDto> =>
    api.post<TenantDetailDto>("/api/provider/tenants", req),

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

  /** GET /api/provider/tenants/:id/users */
  getTenantUsers: (tenantId: string): Promise<TenantUserDto[]> =>
    api.get<TenantUserDto[]>(`/api/provider/tenants/${tenantId}/users`),

  /** POST /api/provider/tenants/:id/users */
  createTenantUser: (tenantId: string, req: CreateTenantUserRequest): Promise<TenantUserDto> =>
    api.post<TenantUserDto>(`/api/provider/tenants/${tenantId}/users`, req),

  /** GET /api/provider/health */
  getHealth: (): Promise<ProviderHealthDto> =>
    api.get<ProviderHealthDto>("/api/provider/health"),

  /** GET /api/provider/logs?... (filtered + paginated) */
  getLogs: (filter: ProviderLogsFilter): Promise<ProviderLogsPageDto> => {
    const params = new URLSearchParams();
    params.set("page",     String(filter.page));
    params.set("pageSize", String(filter.pageSize));
    if (filter.tenantId) params.set("tenantId", filter.tenantId);
    if (filter.action)   params.set("action",   filter.action);
    if (filter.dateFrom) params.set("dateFrom", filter.dateFrom);
    if (filter.dateTo)   params.set("dateTo",   filter.dateTo);
    return api.get<ProviderLogsPageDto>(`/api/provider/logs?${params.toString()}`);
  },
};
