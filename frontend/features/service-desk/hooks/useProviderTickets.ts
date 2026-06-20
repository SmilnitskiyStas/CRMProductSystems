import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { providerTicketsApi } from "../api/providerTickets";
import type { CreateProviderTicketPayload, ProviderTicketFilters } from "../types";

const PROVIDER_TICKETS_KEY = ["provider-tickets"] as const;

export function useProviderTickets(filters: ProviderTicketFilters = {}) {
  return useQuery({
    queryKey: [...PROVIDER_TICKETS_KEY, filters],
    queryFn: () => providerTicketsApi.getAll(filters),
    placeholderData: (prev) => prev,
  });
}

export function useCreateProviderTicket() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateProviderTicketPayload) => providerTicketsApi.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: PROVIDER_TICKETS_KEY });
    },
  });
}
