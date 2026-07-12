"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { usersApi } from "../api/users";
import type {
  ActivityLogDto,
  InviteUserRequest,
  UpdatePermissionsRequest,
  UpdateUserRequest,
  UserDto,
  PermissionGrantDto,
  GrantTemporaryPermissionRequest,
} from "../types";

export const USERS_KEY = ["users"] as const;
const userKey = (id: string) => ["users", id] as const;
const activityKey = (id: string) => ["users", id, "activity"] as const;
const grantsKey = (id: string) => ["users", id, "permission-grants"] as const;

/** Fetch all users in the tenant */
export function useUsers() {
  return useQuery({
    queryKey: USERS_KEY,
    queryFn: usersApi.getAll,
    staleTime: 2 * 60_000,
  });
}

/** Fetch a single user */
export function useUser(id: string) {
  return useQuery({
    queryKey: userKey(id),
    queryFn: () => usersApi.getById(id),
    staleTime: 2 * 60_000,
    enabled: Boolean(id),
  });
}

/** Fetch activity log for a user */
export function useUserActivity(id: string, enabled = true) {
  return useQuery({
    queryKey: activityKey(id),
    queryFn: async (): Promise<ActivityLogDto[]> => {
      try {
        return await usersApi.getActivity(id);
      } catch {
        // Backend may not have data yet or endpoint not available — show empty state
        return [];
      }
    },
    staleTime: 60_000,
    enabled: Boolean(id) && enabled,
    retry: false,
  });
}

/** Invite a new user */
export function useInviteUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: InviteUserRequest) => usersApi.invite(data),
    onSuccess: (newUser) => {
      // Prepend to the list
      qc.setQueryData<UserDto[]>(USERS_KEY, (prev) =>
        prev ? [newUser, ...prev] : [newUser],
      );
    },
  });
}

/** Update a user's profile/role */
export function useUpdateUser(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdateUserRequest) => usersApi.update(id, data),
    onSuccess: (updated) => {
      // Update in list
      qc.setQueryData<UserDto[]>(USERS_KEY, (prev) =>
        prev?.map((u) => (u.id === updated.id ? updated : u)),
      );
      qc.setQueryData(userKey(id), updated);
    },
  });
}

/** Update per-user page-access overrides */
export function useUpdatePermissions(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdatePermissionsRequest) => usersApi.updatePermissions(id, data),
    onSuccess: (updated) => {
      qc.setQueryData<UserDto[]>(USERS_KEY, (prev) =>
        prev?.map((u) => (u.id === updated.id ? updated : u)),
      );
      qc.setQueryData(["users", id], updated);
    },
  });
}

// ── Temporary permission grants (ADR-019, TASK-342/344) ───────────────────────

/** Active (non-revoked, non-expired) temporary grants for a user */
export function useActivePermissionGrants(id: string, enabled = true) {
  return useQuery({
    queryKey: grantsKey(id),
    queryFn: () => usersApi.getActivePermissionGrants(id),
    staleTime: 30_000,
    enabled: Boolean(id) && enabled,
  });
}

/** Grant a temporary, self-expiring page-access override */
export function useGrantTemporaryPermission(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: GrantTemporaryPermissionRequest) => usersApi.grantTemporaryPermission(id, data),
    onSuccess: (grant) => {
      qc.setQueryData<PermissionGrantDto[]>(grantsKey(id), (prev) => (prev ? [...prev, grant] : [grant]));
    },
  });
}

/** Early-revoke a temporary permission grant */
export function useRevokeTemporaryPermission(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (grantId: string) => usersApi.revokeTemporaryPermission(id, grantId),
    onSuccess: (_, grantId) => {
      qc.setQueryData<PermissionGrantDto[]>(grantsKey(id), (prev) => prev?.filter((g) => g.id !== grantId));
    },
  });
}

/** Deactivate a user (soft delete) */
export function useDeactivateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => usersApi.deactivate(id),
    onSuccess: (_, id) => {
      // Mark as inactive in list optimistically
      qc.setQueryData<UserDto[]>(USERS_KEY, (prev) =>
        prev?.map((u) => (u.id === id ? { ...u, isActive: false } : u)),
      );
    },
  });
}
