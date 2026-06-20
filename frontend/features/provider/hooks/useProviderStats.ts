import { useQuery } from "@tanstack/react-query";
import { getTeamStats } from "../api/providerStatsApi";

export function useProviderStats() {
  return useQuery({
    queryKey: ["provider", "stats"],
    queryFn: getTeamStats,
  });
}
