"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { usersApi } from "../api/users";
import type { UserLocationsDto } from "../types";

const userLocationsKey = (id: string) => ["users", id, "locations"] as const;

/**
 * Current store-scoped location assignment list for a user (TASK-392c, Feature 2 Stage 1).
 * GET /api/users/:id/locations is AtLeastEnterpriseAdmin-only on the backend — pass
 * `enabled: false` for viewers who aren't enterprise_admin+ so this doesn't fire a request
 * that's guaranteed to 403 (mirrors useTenantRoles's own `enabled` convention).
 */
export function useUserLocations(id: string, enabled = true) {
  return useQuery({
    queryKey: userLocationsKey(id),
    queryFn: () => usersApi.getLocations(id),
    staleTime: 30_000,
    enabled: Boolean(id) && enabled,
  });
}

/**
 * Full-replace mutation for a user's store-scoped location assignments.
 * Deliberately unbound to a single target id (mirrors useAssignTenantRole) — InviteUserModal
 * calls this right after a fresh invite, before any id is known ahead of time; a manager
 * editing an existing network_manager's territory (UserLocationsEditor) calls it with that
 * user's id instead.
 */
export function useSetUserLocations() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, locationIds }: { userId: string; locationIds: string[] }) =>
      usersApi.setLocations(userId, { locationIds }),
    onSuccess: (result, { userId }) => {
      qc.setQueryData<UserLocationsDto>(userLocationsKey(userId), result);
    },
  });
}
