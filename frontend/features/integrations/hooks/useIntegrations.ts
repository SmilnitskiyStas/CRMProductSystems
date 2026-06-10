"use client";

import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  fetchIntegrations,
  fetchIntegration,
  upsertIntegration,
  deleteIntegration,
} from "../api/integrations";
import type { IntegrationService, UpsertIntegrationRequest } from "../types";

export const INTEGRATIONS_KEY = ["integrations"] as const;

export function useIntegrations() {
  return useQuery({
    queryKey: INTEGRATIONS_KEY,
    queryFn: fetchIntegrations,
    staleTime: 30_000,
  });
}

export function useIntegration(service: IntegrationService, enabled: boolean) {
  return useQuery({
    queryKey: [...INTEGRATIONS_KEY, service],
    queryFn: () => fetchIntegration(service),
    enabled,
    staleTime: 30_000,
    retry: false,
  });
}

export function useUpsertIntegration(service: IntegrationService) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: UpsertIntegrationRequest) => upsertIntegration(service, body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: INTEGRATIONS_KEY });
    },
  });
}

export function useDeleteIntegration(service: IntegrationService) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => deleteIntegration(service),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: INTEGRATIONS_KEY });
      // Clear cached detail too
      qc.removeQueries({ queryKey: [...INTEGRATIONS_KEY, service] });
    },
  });
}
