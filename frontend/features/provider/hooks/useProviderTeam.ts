import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as api from "../api/providerTeamApi";
import type { InviteProviderMemberRequest } from "../api/providerTeamApi";

const TEAM_KEY = ["provider", "team"];

export function useProviderTeam() {
  return useQuery({
    queryKey: TEAM_KEY,
    queryFn:  api.getTeam,
  });
}

export function useInviteProviderMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: InviteProviderMemberRequest) => api.inviteMember(req),
    onSuccess:  () => qc.invalidateQueries({ queryKey: TEAM_KEY }),
  });
}

export function useDeactivateMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (memberId: string) => api.deactivateMember(memberId),
    onSuccess:  () => qc.invalidateQueries({ queryKey: TEAM_KEY }),
  });
}
