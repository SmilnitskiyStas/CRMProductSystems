import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getMyTickets,
  getTickets,
  getTicket,
  createTicket,
  updateTicket,
  addComment,
} from '../api';
import type { CreateTicketPayload, UpdateTicketPayload, AddCommentPayload } from '../types';

export function useMyTickets() {
  return useQuery({
    queryKey: ['tickets', 'my'],
    queryFn: () => getMyTickets(),
  });
}

export function useTickets(status?: string) {
  return useQuery({
    queryKey: ['tickets', status],
    queryFn: () => getTickets(1, status),
  });
}

export function useTicket(id: string) {
  return useQuery({
    queryKey: ['ticket', id],
    queryFn: () => getTicket(id),
    enabled: !!id,
  });
}

export function useCreateTicket() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateTicketPayload) => createTicket(payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['tickets', 'my'] }),
  });
}

export function useUpdateTicket() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, payload }: { id: string; payload: UpdateTicketPayload }) =>
      updateTicket(id, payload),
    onSuccess: (_data, { id }) => {
      void qc.invalidateQueries({ queryKey: ['tickets'] });
      void qc.invalidateQueries({ queryKey: ['ticket', id] });
    },
  });
}

export function useAddComment(ticketId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: AddCommentPayload) => addComment(ticketId, payload),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['ticket', ticketId] }),
  });
}
