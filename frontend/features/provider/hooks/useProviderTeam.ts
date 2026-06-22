import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as api from "../api/providerTeamApi";
import type { InviteProviderMemberRequest, UpdateProviderMemberRequest } from "../api/providerTeamApi";
import { ME_KEY } from "@/features/auth/hooks/useAuth";

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

export function useUpdateMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ memberId, req }: { memberId: string; req: UpdateProviderMemberRequest }) =>
      api.updateMember(memberId, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: TEAM_KEY });
      qc.invalidateQueries({ queryKey: ME_KEY });
    },
  });
}

export function useDeactivateMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (memberId: string) => api.deactivateMember(memberId),
    onSuccess:  () => qc.invalidateQueries({ queryKey: TEAM_KEY }),
  });
}

export function useReactivateMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (memberId: string) => api.reactivateMember(memberId),
    onSuccess:  () => qc.invalidateQueries({ queryKey: TEAM_KEY }),
  });
}
