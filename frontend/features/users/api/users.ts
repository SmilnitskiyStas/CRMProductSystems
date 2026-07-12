import { api } from "@/lib/api";
import type {
  UserDto,
  InviteUserRequest,
  UpdateUserRequest,
  UpdatePermissionsRequest,
  ActivityLogDto,
  PermissionGrantDto,
  GrantTemporaryPermissionRequest,
} from "../types";

export const usersApi = {
  /** GET /api/users — all users in tenant */
  getAll(): Promise<UserDto[]> {
    return api.get<UserDto[]>("/api/users");
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
};
