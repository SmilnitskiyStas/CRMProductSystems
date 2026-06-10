import { api } from "@/lib/api";
import type { IntegrationSummary, IntegrationConfig, UpsertIntegrationRequest } from "../types";

export async function fetchIntegrations(): Promise<IntegrationSummary[]> {
  return api.get<IntegrationSummary[]>("/api/integrations");
}

export async function fetchIntegration(service: string): Promise<IntegrationConfig> {
  return api.get<IntegrationConfig>(`/api/integrations/${service}`);
}

export async function upsertIntegration(
  service: string,
  body: UpsertIntegrationRequest,
): Promise<void> {
  await api.put(`/api/integrations/${service}`, body);
}

export async function deleteIntegration(service: string): Promise<void> {
  await api.delete(`/api/integrations/${service}`);
}
