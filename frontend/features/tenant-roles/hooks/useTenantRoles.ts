"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { tenantRolesApi } from "../api/tenant-roles";
import { usersApi } from "@/features/users/api/users";
import { USERS_KEY } from "@/features/users/hooks/useUsers";
import type { UserDto } from "@/features/users/types";
import type { CreateTenantRoleRequest, UpdateTenantRoleRequest } from "../types";

const LIST_KEY = ["tenant-roles", "list"] as const;
const CAPABILITIES_KEY = ["tenant-roles", "capabilities"] as const;
const TABS_KEY = ["tenant-roles", "tabs"] as const;

/**
 * Templates for the current tenant. Pass includeInactive to also list archived ones.
 * GET /api/tenant-roles is AtLeastEnterpriseAdmin-only on the backend (403 for every other
 * role) — pass `enabled: false` for viewers who aren't enterprise_admin+ (e.g. TenantRoleBadge
 * rendered inside a store_manager's view of UsersList) so we don't fire a request that's
 * guaranteed to 403-and-retry.
 */
export function useTenantRoles(includeInactive = false, enabled = true) {
  return useQuery({
    queryKey: [...LIST_KEY, { includeInactive }],
    queryFn: () => tenantRolesApi.getAll(includeInactive),
    staleTime: 60_000,
    enabled,
    retry: false,
  });
}

/** Backend-sourced capability catalog grouped by specialty — rarely changes, cached longer. */
export function useTenantRoleCapabilities() {
  return useQuery({
    queryKey: CAPABILITIES_KEY,
    queryFn: tenantRolesApi.getCapabilities,
    staleTime: 10 * 60_000,
  });
}

/** Backend-sourced sidebar-tab catalog (TASK-391b) — rarely changes, cached longer. */
export function useTenantRoleTabs() {
  return useQuery({
    queryKey: TABS_KEY,
    queryFn: tenantRolesApi.getTabs,
    staleTime: 10 * 60_000,
  });
}

export function useCreateTenantRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateTenantRoleRequest) => tenantRolesApi.create(data),
    onSuccess: () => qc.invalidateQueries({ queryKey: LIST_KEY }),
  });
}

export function useUpdateTenantRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateTenantRoleRequest }) =>
      tenantRolesApi.update(id, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: LIST_KEY }),
  });
}

/** Archives (soft-deletes) a template — never a hard delete, users may still reference it. */
export function useArchiveTenantRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => tenantRolesApi.archive(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: LIST_KEY }),
  });
}

/**
 * Assigns (or clears, tenantRoleId=null) a TenantRole template to a user.
 * POST /api/users/:id/tenant-role — UsersController, not TenantRolesController — so this
 * patches the shared `users` query cache (same keys as useUsers.ts) rather than the
 * tenant-roles list; the endpoint returns 204, so there's no DTO to write back with.
 */
export function useAssignTenantRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, tenantRoleId }: { userId: string; tenantRoleId: string | null }) =>
      usersApi.assignTenantRole(userId, tenantRoleId),
    onSuccess: (_, { userId, tenantRoleId }) => {
      qc.setQueryData<UserDto[]>(USERS_KEY, (prev) =>
        prev?.map((u) => (u.id === userId ? { ...u, tenantRoleId } : u)),
      );
      qc.setQueryData<UserDto | undefined>(["users", userId], (prev) =>
        prev ? { ...prev, tenantRoleId } : prev,
      );
    },
  });
}
