import { api } from "@/lib/api";

export interface ProviderRoleDto {
  id: string;
  displayName: string;
  baseRole: string;
  permissions: string[];
  isSystem: boolean;
}

export interface CreateProviderRoleRequest {
  displayName: string;
  baseRole: string;
  permissions: string[];
}

export interface UpdateProviderRoleRequest {
  displayName: string;
  baseRole: string;
  permissions: string[];
}

export function getRoles(): Promise<ProviderRoleDto[]> {
  return api.get<ProviderRoleDto[]>("/api/provider/roles");
}

export function createRole(req: CreateProviderRoleRequest): Promise<ProviderRoleDto> {
  return api.post<ProviderRoleDto>("/api/provider/roles", req);
}

export function updateRole(id: string, req: UpdateProviderRoleRequest): Promise<ProviderRoleDto> {
  return api.put<ProviderRoleDto>(`/api/provider/roles/${id}`, req);
}

export function deleteRole(id: string): Promise<void> {
  return api.delete<void>(`/api/provider/roles/${id}`);
}
