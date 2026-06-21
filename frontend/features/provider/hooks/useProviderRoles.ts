import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import * as rolesApi from "../api/providerRolesApi";
import type { CreateProviderRoleRequest, UpdateProviderRoleRequest } from "../api/providerRolesApi";

const KEY = ["provider-roles"] as const;

export function useProviderRoles() {
  return useQuery({ queryKey: KEY, queryFn: rolesApi.getRoles });
}

export function useCreateRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateProviderRoleRequest) => rolesApi.createRole(req),
    onSuccess: () => qc.invalidateQueries({ queryKey: KEY }),
  });
}

export function useUpdateRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateProviderRoleRequest }) =>
      rolesApi.updateRole(id, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: KEY }),
  });
}

export function useDeleteRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => rolesApi.deleteRole(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: KEY }),
  });
}
