"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { providerLeadsApi } from "../api/providerLeads";
import type { LeadStatusFilter, UpdateLeadBody } from "../types";

export const PROVIDER_LEADS_KEY = ["provider", "leads"] as const;
export const PROVIDER_LEADS_COUNT_KEY = ["provider", "leads", "count"] as const;

export function useProviderLeads(params: {
  status: LeadStatusFilter;
  search: string;
  page: number;
  pageSize: number;
}) {
  return useQuery({
    queryKey: [...PROVIDER_LEADS_KEY, "list", params] as const,
    queryFn: () => providerLeadsApi.list(params),
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** Unprocessed leads count for the sidebar badge. Polled every 60s. */
export function useUnprocessedLeadsCount(enabled = true) {
  return useQuery({
    queryKey: PROVIDER_LEADS_COUNT_KEY,
    queryFn: providerLeadsApi.count,
    enabled,
    refetchInterval: 60_000,
    retry: false,
  });
}

export function useUpdateLead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateLeadBody }) =>
      providerLeadsApi.update(id, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: PROVIDER_LEADS_KEY }),
  });
}
