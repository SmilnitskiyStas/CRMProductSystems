import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { customerSupportTicketsApi } from "../api/tickets";
import type { TicketFilters } from "../types";

const TICKETS_KEY = ["customer-support", "tickets"] as const;

export function useCustomerSupportTickets(filters: TicketFilters = {}) {
  return useQuery({
    queryKey: [...TICKETS_KEY, "list", filters],
    queryFn: () => customerSupportTicketsApi.getTickets(filters),
    placeholderData: (prev) => prev,
  });
}

export function useCustomerSupportTicket(id: string) {
  return useQuery({
    queryKey: [...TICKETS_KEY, id],
    queryFn: () => customerSupportTicketsApi.getTicket(id),
    enabled: !!id,
  });
}

export function useReplyToTicket() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: string }) => customerSupportTicketsApi.reply(id, body),
    onSuccess: (_result, { id }) => {
      queryClient.invalidateQueries({ queryKey: TICKETS_KEY });
      queryClient.invalidateQueries({ queryKey: [...TICKETS_KEY, id] });
    },
  });
}

export function useUpdateTicketStatus() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: string }) =>
      customerSupportTicketsApi.updateStatus(id, status),
    onSuccess: (_result, { id }) => {
      queryClient.invalidateQueries({ queryKey: TICKETS_KEY });
      queryClient.invalidateQueries({ queryKey: [...TICKETS_KEY, id] });
    },
  });
}
