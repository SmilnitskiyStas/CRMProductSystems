import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as api from "../api/supportApi";

const TICKETS_KEY   = ["support", "tickets"];
const ticketKey     = (id: string) => ["support", "ticket", id];
const PROV_KEY      = ["provider", "support", "tickets"];
const provTicketKey = (id: string) => ["provider", "support", "ticket", id];

// ── Client hooks ─────────────────────────────────────────────────────────────

export function useMyTickets(status?: string) {
  return useQuery({
    queryKey: [...TICKETS_KEY, status],
    queryFn:  () => api.getMyTickets(status),
  });
}

export function useMyTicket(id: string | null) {
  return useQuery({
    queryKey: ticketKey(id ?? ""),
    queryFn:  () => api.getMyTicket(id!),
    enabled:  !!id,
    refetchInterval: 5000,
  });
}

export function useCreateTicket() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: api.createTicket,
    onSuccess:  () => qc.invalidateQueries({ queryKey: TICKETS_KEY }),
  });
}

export function useAddClientMessage(ticketId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: string) => api.addClientMessage(ticketId, body),
    onSuccess:  () => qc.invalidateQueries({ queryKey: ticketKey(ticketId) }),
  });
}

export function useMarkTicketRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ticketId: string) => api.markTicketRead(ticketId),
    onSuccess:  (_, ticketId) => qc.invalidateQueries({ queryKey: TICKETS_KEY }),
  });
}

// ── Provider hooks ────────────────────────────────────────────────────────────

export function useAllTickets(params: { status?: string; assignedTo?: string }) {
  return useQuery({
    queryKey: [...PROV_KEY, params],
    queryFn:  () => api.getAllTickets(params),
  });
}

export function useProviderTicket(id: string | null) {
  return useQuery({
    queryKey: provTicketKey(id ?? ""),
    queryFn:  () => api.getProviderTicket(id!),
    enabled:  !!id,
    refetchInterval: 5000,
  });
}

export function useAssignTicket(ticketId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (agentId: string) => api.assignTicket(ticketId, agentId),
    onSuccess:  () => {
      qc.invalidateQueries({ queryKey: PROV_KEY });
      qc.invalidateQueries({ queryKey: provTicketKey(ticketId) });
    },
  });
}

export function useUpdateTicketStatus(ticketId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (status: string) => api.updateTicketStatus(ticketId, status),
    onSuccess:  () => {
      qc.invalidateQueries({ queryKey: PROV_KEY });
      qc.invalidateQueries({ queryKey: provTicketKey(ticketId) });
    },
  });
}

export function useAddProviderMessage(ticketId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: string) => api.addProviderMessage(ticketId, body),
    onSuccess:  () => qc.invalidateQueries({ queryKey: provTicketKey(ticketId) }),
  });
}

export function useMarkProviderTicketRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ticketId: string) => api.markProviderTicketRead(ticketId),
    onSuccess:  () => qc.invalidateQueries({ queryKey: PROV_KEY }),
  });
}
