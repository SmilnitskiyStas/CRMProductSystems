import { api } from "@/lib/api";
import type { LoyaltyTierDefinitionDto, UpsertTierRequest } from "../types";

/**
 * GET /api/settings/loyalty/tiers — the tenant's tier ladder, ordered by `sortOrder` ascending.
 * Empty array (never null) when the tenant has no ladder configured yet.
 */
export async function fetchLoyaltyTiers(): Promise<LoyaltyTierDefinitionDto[]> {
  return api.get<LoyaltyTierDefinitionDto[]>("/api/settings/loyalty/tiers");
}

/**
 * PUT /api/settings/loyalty/tiers — bulk replace of the whole ladder, keyed by `sortOrder` (see
 * UpsertTierRequest's doc comment in ../types.ts). Returns the updated ladder.
 */
export async function updateLoyaltyTiers(
  body: UpsertTierRequest[],
): Promise<LoyaltyTierDefinitionDto[]> {
  return api.put<LoyaltyTierDefinitionDto[]>("/api/settings/loyalty/tiers", body);
}

export async function uploadLoyaltyTierImage(id: string, file: File): Promise<string> {
  const form = new FormData();
  form.append("file", file);
  const result = await api.postForm<{ imageUrl: string }>(`/api/settings/loyalty/tiers/${id}/image`, form);
  return result.imageUrl;
}
