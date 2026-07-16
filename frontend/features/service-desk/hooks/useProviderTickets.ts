import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { providerTicketsApi } from "../api/providerTickets";
import type {
  CreateProviderTicketPayload,
  ProviderTicketFilters,
  AddCommentPayload,
} from "../types";

const PROVIDER_TICKETS_KEY = ["provider-tickets"] as const;

export function useProviderTickets(filters: ProviderTicketFilters = {}) {
  return useQuery({
    queryKey: [...PROVIDER_TICKETS_KEY, filters],
    queryFn: () => providerTicketsApi.getAll(filters),
    placeholderData: (prev) => prev,
  });
}

export function useProviderTicket(id: string | null) {
  return useQuery({
    queryKey: [...PROVIDER_TICKETS_KEY, id],
    queryFn: () => providerTicketsApi.getTicket(id as string),
    enabled: !!id,
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

export function useAddProviderComment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: AddCommentPayload }) =>
      providerTicketsApi.addComment(id, data),
    onSuccess: (_result, { id }) => {
      queryClient.invalidateQueries({ queryKey: PROVIDER_TICKETS_KEY });
      queryClient.invalidateQueries({ queryKey: [...PROVIDER_TICKETS_KEY, id] });
    },
  });
}
