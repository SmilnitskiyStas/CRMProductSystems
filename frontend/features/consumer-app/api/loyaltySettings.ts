import { api } from "@/lib/api";
import type { LoyaltyProgramSettings, UpdateLoyaltyProgramSettingsRequest } from "../types";

export async function fetchLoyaltySettings(): Promise<LoyaltyProgramSettings> {
  return api.get<LoyaltyProgramSettings>("/api/settings/loyalty");
}

export async function updateLoyaltySettings(
  body: UpdateLoyaltyProgramSettingsRequest,
): Promise<LoyaltyProgramSettings> {
  return api.put<LoyaltyProgramSettings>("/api/settings/loyalty", body);
}

export async function resetAllBonusBalances(): Promise<{ affectedMemberships: number }> {
  return api.post<{ affectedMemberships: number }>("/api/settings/loyalty/reset-balances", {});
}
