"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { adminApi } from "../api/admin";
import type { CreateTenantRequest, TenantDto } from "../types";

// ── Query keys ────────────────────────────────────────────────────────────────

export const ADMIN_TENANTS_KEY = ["admin", "tenants"] as const;
const adminTenantKey = (id: string) => ["admin", "tenants", id] as const;

// ── Queries ───────────────────────────────────────────────────────────────────

export function useTenants() {
  return useQuery({
    queryKey: ADMIN_TENANTS_KEY,
    queryFn: async (): Promise<TenantDto[]> => {
      try {
        return await adminApi.fetchTenants();
      } catch {
        return [];
      }
    },
    staleTime: 60_000,
    retry: false,
  });
}

export function useTenant(id: string, enabled = true) {
  return useQuery({
    queryKey: adminTenantKey(id),
    queryFn: async (): Promise<TenantDto | null> => {
      try {
        return await adminApi.fetchTenant(id);
      } catch {
        return null;
      }
    },
    staleTime: 30_000,
    enabled: Boolean(id) && enabled,
    retry: false,
  });
}

// ── Mutations ─────────────────────────────────────────────────────────────────

export function useCreateTenant() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateTenantRequest) => adminApi.createTenant(req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ADMIN_TENANTS_KEY });
    },
  });
}

export function useUpdatePlan(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (plan: string) => adminApi.updatePlan(tenantId, plan),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: adminTenantKey(tenantId) });
      qc.invalidateQueries({ queryKey: ADMIN_TENANTS_KEY });
    },
  });
}

export function useUpdateModules(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (modules: string[]) => adminApi.updateModules(tenantId, modules),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: adminTenantKey(tenantId) });
      qc.invalidateQueries({ queryKey: ADMIN_TENANTS_KEY });
    },
  });
}

export function useActivateTenant() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminApi.activateTenant(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ADMIN_TENANTS_KEY });
    },
  });
}

export function useDeactivateTenant() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => adminApi.deactivateTenant(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ADMIN_TENANTS_KEY });
    },
  });
}
