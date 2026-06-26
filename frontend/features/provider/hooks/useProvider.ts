"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { providerApi } from "../api/provider";
import type { TenantSummaryDto, ProviderHealthDto, ProviderLogsFilter, ProviderLogsPageDto, TenantDetailDto, CreateTenantRequest, CreateTenantUserRequest } from "../types";

// ── Query keys ───────────────────────────────────────────────────────────────

export const TENANTS_KEY    = ["provider", "tenants"] as const;
export const HEALTH_KEY     = ["provider", "health"]  as const;
const logsKey = (f: ProviderLogsFilter) => ["provider", "logs", f] as const;
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

export function useProviderLogs(filter: ProviderLogsFilter) {
  return useQuery({
    queryKey: logsKey(filter),
    queryFn: async (): Promise<ProviderLogsPageDto> => {
      try {
        return await providerApi.getLogs(filter);
      } catch {
        return { items: [], total: 0, page: filter.page, pageSize: filter.pageSize };
      }
    },
    staleTime: 15_000,
    retry: false,
  });
}

// ── Mutations ────────────────────────────────────────────────────────────────

export function useCreateTenant() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateTenantRequest) => providerApi.createTenant(req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: TENANTS_KEY });
      qc.invalidateQueries({ queryKey: HEALTH_KEY });
    },
  });
}

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

export function useTenantUsers(tenantId: string) {
  return useQuery({
    queryKey: ["provider", "tenants", tenantId, "users"],
    queryFn: () => providerApi.getTenantUsers(tenantId),
    enabled: !!tenantId,
  });
}

export function useCreateTenantUser(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateTenantUserRequest) => providerApi.createTenantUser(tenantId, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["provider", "tenants", tenantId, "users"] });
      qc.invalidateQueries({ queryKey: ["provider", "tenants", tenantId] });
    },
  });
}

export function useActivateTenant(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => providerApi.activateTenant(tenantId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["provider", "tenants", tenantId] });
      qc.invalidateQueries({ queryKey: ["provider", "tenants"] });
    },
  });
}

export function useDeactivateTenant(tenantId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => providerApi.deactivateTenant(tenantId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["provider", "tenants", tenantId] });
      qc.invalidateQueries({ queryKey: ["provider", "tenants"] });
    },
  });
}
