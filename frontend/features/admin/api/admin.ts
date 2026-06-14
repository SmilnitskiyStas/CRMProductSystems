import { api } from "@/lib/api";
import type { TenantDto, CreateTenantRequest } from "../types";

export const adminApi = {
  /** GET /api/admin/tenants */
  fetchTenants: (): Promise<TenantDto[]> =>
    api.get<TenantDto[]>("/api/admin/tenants"),

  /** POST /api/admin/tenants */
  createTenant: (req: CreateTenantRequest): Promise<TenantDto> =>
    api.post<TenantDto>("/api/admin/tenants", req),

  /** GET /api/admin/tenants/:id */
  fetchTenant: (id: string): Promise<TenantDto> =>
    api.get<TenantDto>(`/api/admin/tenants/${id}`),

  /** PATCH /api/admin/tenants/:id/plan */
  updatePlan: (id: string, plan: string): Promise<void> =>
    api.put<void>(`/api/admin/tenants/${id}/plan`, { plan }),

  /** PATCH /api/admin/tenants/:id/modules */
  updateModules: (id: string, modules: string[]): Promise<void> =>
    api.put<void>(`/api/admin/tenants/${id}/modules`, { modules }),

  /** POST /api/admin/tenants/:id/activate */
  activateTenant: (id: string): Promise<void> =>
    api.post<void>(`/api/admin/tenants/${id}/activate`),

  /** POST /api/admin/tenants/:id/deactivate */
  deactivateTenant: (id: string): Promise<void> =>
    api.post<void>(`/api/admin/tenants/${id}/deactivate`),
};
