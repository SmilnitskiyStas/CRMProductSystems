import { api } from "@/lib/api";
import type { LandingLeadDto, LandingLeadListDto, LeadStatusFilter, UpdateLeadBody } from "../types";

// Provider-team view over public landing leads (TASK-721).
export const providerLeadsApi = {
  list: (p: { status: LeadStatusFilter; search: string; page: number; pageSize: number }) => {
    const qs = new URLSearchParams({
      status: p.status,
      page: String(p.page),
      pageSize: String(p.pageSize),
    });
    if (p.search.trim()) qs.set("search", p.search.trim());
    return api.get<LandingLeadListDto>(`/api/provider/leads?${qs.toString()}`);
  },
  count: () => api.get<{ unprocessed: number }>("/api/provider/leads/count"),
  update: (id: string, body: UpdateLeadBody) =>
    api.patch<LandingLeadDto>(`/api/provider/leads/${id}`, body),
};
