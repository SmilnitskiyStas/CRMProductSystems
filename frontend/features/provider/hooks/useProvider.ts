"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { providerApi } from "../api/provider";
import type { TenantSummaryDto, ProviderHealthDto, ProviderLogDto, TenantDetailDto } from "../types";

// ── Query keys ───────────────────────────────────────────────────────────────

export const TENANTS_KEY    = ["provider", "tenants"] as const;
export const HEALTH_KEY     = ["provider", "health"]  as const;
export const LOGS_KEY       = ["provider", "logs"]    as const;
const tenantKey = (id: string) => ["provider", "tenants", id] as const;

// ── Queries ──────────────────────────────────────────────────────────────────

export function useTenants() {
  return useQuery({
    queryKey: TENANTS_KEY,
    queryFn: async (): Promise<TenantSummaryDto[]> => {
      try {
        return await providerApi.getTenants();
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
    queryKey: tenantKey(id),
    queryFn: async (): Promise<TenantDetailDto | null> => {
      try {
        return await providerApi.getTenant(id);
      } catch {
        return null;
      }
    },
    staleTime: 30_000,
    enabled: Boolean(id) && enabled,
    retry: false,
  });
}

export function useProviderHealth() {
  return useQuery({
    queryKey: HEALTH_KEY,
    queryFn: async (): Promise<ProviderHealthDto | null> => {
      try {
        return await providerApi.getHealth();
      } catch {
        return null;
      }
    },
    staleTime: 30_000,
    retry: false,
  });
}

export function useProviderLogs(limit = 100) {
  return useQuery({
    queryKey: LOGS_KEY,
    queryFn: async (): Promise<ProviderLogDto[]> => {
      try {
        return await providerApi.getLogs(limit);
      } catch {
        return [];
      }
    },
    staleTime: 30_000,
    retry: false,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────────

export function useUpdatePlan(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (plan: string) => providerApi.updatePlan(tenantId, plan),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: tenantKey(tenantId) });
      qc.invalidateQueries({ queryKey: TENANTS_KEY });
    },
  });
}

export function useUpdateModules(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (modules: string[]) => providerApi.updateModules(tenantId, modules),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: tenantKey(tenantId) });
      qc.invalidateQueries({ queryKey: TENANTS_KEY });
    },
  });
}

export function useImpersonate() {
  return useMutation({
    mutationFn: (tenantId: string) => providerApi.impersonate(tenantId),
  });
}
