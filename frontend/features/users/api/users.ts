import { api } from "@/lib/api";
import type {
  UserDto,
  InviteUserRequest,
  UpdateUserRequest,
  UpdatePermissionsRequest,
  ActivityLogDto,
  PermissionGrantDto,
  GrantTemporaryPermissionRequest,
  UserLocationsDto,
  UpdateUserLocationsRequest,
} from "../types";

export const usersApi = {
  /** GET /api/users — all users in tenant, optionally filtered by store (repeated ?storeIds=,
   * omitted entirely when empty — matches priceSegmentsApi's serialization style). */
  getAll(storeIds?: string[]): Promise<UserDto[]> {
    const qs = new URLSearchParams();
    for (const id of storeIds ?? []) qs.append("storeIds", id);
    const s = qs.toString();
    return api.get<UserDto[]>(`/api/users${s ? `?${s}` : ""}`);
  },

  /** GET /api/users/:id */
  getById(id: string): Promise<UserDto> {
    return api.get<UserDto>(`/api/users/${id}`);
  },

  /** POST /api/users/invite */
  invite(data: InviteUserRequest): Promise<UserDto> {
    return api.post<UserDto>("/api/users/invite", data);
  },

  /** PUT /api/users/:id */
  update(id: string, data: UpdateUserRequest): Promise<UserDto> {
    return api.put<UserDto>(`/api/users/${id}`, data);
  },

  /** DELETE /api/users/:id — soft delete (deactivate) */
  deactivate(id: string): Promise<void> {
    return api.delete<void>(`/api/users/${id}`);
  },

  /** GET /api/users/:id/activity */
  getActivity(id: string, limit = 50): Promise<ActivityLogDto[]> {
    return api.get<ActivityLogDto[]>(`/api/users/${id}/activity?limit=${limit}`);
  },

  /** PUT /api/users/:id/permissions */
  updatePermissions(id: string, data: UpdatePermissionsRequest): Promise<UserDto> {
    return api.put<UserDto>(`/api/users/${id}/permissions`, data);
  },

  /** POST /api/users/:id/permission-grants — ADR-019 temporary access grant */
  grantTemporaryPermission(id: string, data: GrantTemporaryPermissionRequest): Promise<PermissionGrantDto> {
    return api.post<PermissionGrantDto>(`/api/users/${id}/permission-grants`, data);
  },

  /** GET /api/users/:id/permission-grants — active (non-revoked, non-expired) grants */
  getActivePermissionGrants(id: string): Promise<PermissionGrantDto[]> {
    return api.get<PermissionGrantDto[]>(`/api/users/${id}/permission-grants`);
  },

  /** DELETE /api/users/:id/permission-grants/:grantId — early revoke */
  revokeTemporaryPermission(id: string, grantId: string): Promise<void> {
    return api.delete<void>(`/api/users/${id}/permission-grants/${grantId}`);
  },

  /** POST /api/users/:id/tenant-role — assign (or clear, when null) a TenantRole template (ADR-020). */
  assignTenantRole(id: string, tenantRoleId: string | null): Promise<void> {
    return api.post<void>(`/api/users/${id}/tenant-role`, { tenantRoleId });
  },

  /** GET /api/users/:id/locations — store-scoped assignment list (TASK-392c, AtLeastEnterpriseAdmin-only). */
  getLocations(id: string): Promise<UserLocationsDto> {
    return api.get<UserLocationsDto>(`/api/users/${id}/locations`);
  },

  /** PUT /api/users/:id/locations — full-replace (TASK-392c, AtLeastEnterpriseAdmin-only). */
  setLocations(id: string, data: UpdateUserLocationsRequest): Promise<UserLocationsDto> {
    return api.put<UserLocationsDto>(`/api/users/${id}/locations`, data);
  },
};
