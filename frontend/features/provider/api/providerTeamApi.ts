import { api } from "@/lib/api";

export interface ProviderTeamMemberDto {
  id: string;
  email: string;
  fullName: string;
  role: string;
  isActive: boolean;
  createdAt: string;
}

export interface InviteProviderMemberRequest {
  email: string;
  fullName: string;
  role: string;
}

export function getTeam(): Promise<ProviderTeamMemberDto[]> {
  return api.get<ProviderTeamMemberDto[]>("/api/provider/team");
}

export function inviteMember(req: InviteProviderMemberRequest): Promise<ProviderTeamMemberDto> {
  return api.post<ProviderTeamMemberDto>("/api/provider/team/invite", req);
}

export interface UpdateProviderMemberRequest {
  fullName: string;
  role: string;
}

export function updateMember(memberId: string, req: UpdateProviderMemberRequest): Promise<ProviderTeamMemberDto> {
  return api.put<ProviderTeamMemberDto>(`/api/provider/team/${memberId}`, req);
}

export function deactivateMember(memberId: string): Promise<void> {
  return api.delete<void>(`/api/provider/team/${memberId}`);
}

export function reactivateMember(memberId: string): Promise<void> {
  return api.post<void>(`/api/provider/team/${memberId}/reactivate`, {});
}
