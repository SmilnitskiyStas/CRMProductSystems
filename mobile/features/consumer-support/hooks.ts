import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { addTicketMessage, createTicket, getMyTickets, getTicket } from './api';
import type { ConsumerSupportTicket } from './types';
import { appendSupportMessage, consumerSupportTicketKey } from './realtime';

export const useConsumerTickets = (tenantId: string | null) => useQuery({ queryKey: ['consumer-support', tenantId], queryFn: () => getMyTickets(tenantId!), enabled: !!tenantId });
export const useConsumerTicket = (id: string) => useQuery({
  queryKey: consumerSupportTicketKey(id),
  queryFn: () => getTicket(id),
  enabled: !!id,
});
export function useCreateConsumerTicket() { const qc=useQueryClient(); return useMutation({ mutationFn: createTicket, onSuccess: (t) => void qc.invalidateQueries({ queryKey: ['consumer-support', t.tenantId] }) }); }
export function useAddConsumerMessage(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: string) => addTicketMessage(id, body),
    onSuccess: (message) => {
      qc.setQueryData<ConsumerSupportTicket>(consumerSupportTicketKey(id), (ticket) =>
        appendSupportMessage(ticket, message),
      );
      void qc.invalidateQueries({ queryKey: consumerSupportTicketKey(id) });
    },
  });
}
