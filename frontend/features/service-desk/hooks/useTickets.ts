import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ticketsApi } from "../api/tickets";
import type {
  TicketFilters,
  CreateTicketPayload,
  UpdateTicketPayload,
  AddCommentPayload,
} from "../types";

const TICKETS_KEY = ["tickets"] as const;
const MY_TICKETS_KEY = ["tickets", "my"] as const;

export function useTickets(filters: TicketFilters = {}) {
  return useQuery({
    queryKey: [...TICKETS_KEY, "list", filters],
    queryFn: () => ticketsApi.getTickets(filters),
    placeholderData: (prev) => prev,
  });
}

export function useMyTickets() {
  return useQuery({
    queryKey: MY_TICKETS_KEY,
    queryFn: () => ticketsApi.getMyTickets(),
  });
}

export function useTicket(id: string) {
  return useQuery({
    queryKey: [...TICKETS_KEY, id],
    queryFn: () => ticketsApi.getTicket(id),
    enabled: !!id,
  });
}

export function useCreateTicket() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateTicketPayload) => ticketsApi.createTicket(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TICKETS_KEY });
      queryClient.invalidateQueries({ queryKey: MY_TICKETS_KEY });
    },
  });
}

export function useUpdateTicket() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateTicketPayload }) =>
      ticketsApi.updateTicket(id, data),
    onSuccess: (_result, { id }) => {
      queryClient.invalidateQueries({ queryKey: TICKETS_KEY });
      queryClient.invalidateQueries({ queryKey: MY_TICKETS_KEY });
      queryClient.invalidateQueries({ queryKey: [...TICKETS_KEY, id] });
    },
  });
}

export function useAddComment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: AddCommentPayload }) =>
      ticketsApi.addComment(id, data),
    onSuccess: (_result, { id }) => {
      queryClient.invalidateQueries({ queryKey: [...TICKETS_KEY, id] });
    },
  });
}
