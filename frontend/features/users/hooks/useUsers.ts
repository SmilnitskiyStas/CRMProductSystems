"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { usersApi } from "../api/users";
import type { ActivityLogDto, InviteUserRequest, UpdatePermissionsRequest, UpdateUserRequest, UserDto } from "../types";

export const USERS_KEY = ["users"] as const;
const userKey = (id: string) => ["users", id] as const;
const activityKey = (id: string) => ["users", id, "activity"] as const;

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
