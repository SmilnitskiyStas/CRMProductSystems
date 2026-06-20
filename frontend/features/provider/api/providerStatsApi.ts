import { api } from "@/lib/api";

export interface ProviderMemberStatsDto {
  memberId: string;
  fullName: string;
  role: string;
  isActive: boolean;
  ticketsAssigned: number;
  ticketsResolved: number;
  ticketsCreatedByProvider: number;
  commentsWritten: number;
  avgResolutionHours: number | null;
}

export function getTeamStats(): Promise<ProviderMemberStatsDto[]> {
  return api.get<ProviderMemberStatsDto[]>("/api/provider/team/stats");
}
