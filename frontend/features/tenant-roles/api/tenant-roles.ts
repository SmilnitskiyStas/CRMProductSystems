import { api } from "@/lib/api";
import type {
  TenantRoleDto,
  CreateTenantRoleRequest,
  UpdateTenantRoleRequest,
  TenantRoleCapabilityGroup,
} from "../types";

// CRUD for TenantRolesController (ADR-020) — all AtLeastEnterpriseAdmin-only on the backend.
// Assignment (POST /api/users/:id/tenant-role) lives on UsersController and is exposed via
// usersApi.assignTenantRole in frontend/features/users/api/users.ts, reused by
// hooks/useTenantRoles.ts's useAssignTenantRole — kept there since it mutates the User resource.
export const tenantRolesApi = {
  /** GET /api/tenant-roles — templates for the current tenant. */
  getAll(includeInactive = false): Promise<TenantRoleDto[]> {
    return api.get<TenantRoleDto[]>(`/api/tenant-roles?includeInactive=${includeInactive}`);
  },

  /** GET /api/tenant-roles/:id */
  getById(id: string): Promise<TenantRoleDto> {
    return api.get<TenantRoleDto>(`/api/tenant-roles/${id}`);
  },

  /** GET /api/tenant-roles/capabilities — backend-grouped capability catalog. */
  getCapabilities(): Promise<TenantRoleCapabilityGroup[]> {
    return api.get<TenantRoleCapabilityGroup[]>("/api/tenant-roles/capabilities");
  },

  /** POST /api/tenant-roles */
  create(data: CreateTenantRoleRequest): Promise<TenantRoleDto> {
    return api.post<TenantRoleDto>("/api/tenant-roles", data);
  },

  /** PUT /api/tenant-roles/:id */
  update(id: string, data: UpdateTenantRoleRequest): Promise<TenantRoleDto> {
    return api.put<TenantRoleDto>(`/api/tenant-roles/${id}`, data);
  },

  /** DELETE /api/tenant-roles/:id — archives (soft delete), never a hard delete. */
  archive(id: string): Promise<void> {
    return api.delete<void>(`/api/tenant-roles/${id}`);
  },
};
