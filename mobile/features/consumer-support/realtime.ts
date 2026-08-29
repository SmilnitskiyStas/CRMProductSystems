import { useEffect } from 'react';
import * as signalR from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useAuthStore } from '@/features/auth/store';
import type { ConsumerSupportMessage, ConsumerSupportTicket } from './types';

const HUB_PATH = '/api/hubs/consumer-support';
const MESSAGE_EVENT = 'SupportMessageCreated';
const STATUS_EVENT = 'SupportTicketStatusChanged';

type MessageCreatedPayload = { ticketId: string; message: ConsumerSupportMessage };
type StatusChangedPayload = { ticketId: string; status: string; updatedAt: string };

export const consumerSupportTicketKey = (ticketId: string) =>
  ['consumer-support', 'ticket', ticketId] as const;

export function resolveConsumerSupportHubUrl(apiUrl = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5000/api'): string {
  const apiBase = apiUrl.replace(/\/+$/, '');
  const origin = apiBase.replace(/\/api$/i, '');
  return `${origin}${HUB_PATH}`;
}

export function appendSupportMessage(
  ticket: ConsumerSupportTicket | undefined,
  message: ConsumerSupportMessage,
): ConsumerSupportTicket | undefined {
  if (!ticket || ticket.messages?.some((item) => item.id === message.id)) return ticket;
  return { ...ticket, updatedAt: message.createdAt, messages: [...(ticket.messages ?? []), message] };
}

/** Keeps a realtime subscription alive only while a concrete ticket screen is mounted. */
export function useConsumerSupportRealtime(ticketId: string): void {
  const queryClient = useQueryClient();
  const personalAccessToken = useAuthStore((state) => state.personalAccessToken);

  useEffect(() => {
    if (!ticketId || !personalAccessToken) return;

    let disposed = false;
    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(resolveConsumerSupportHubUrl(), {
        accessTokenFactory: () => useAuthStore.getState().personalAccessToken ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(__DEV__ ? signalR.LogLevel.Warning : signalR.LogLevel.Error)
      .build();

    const refreshTicket = () => queryClient.invalidateQueries({ queryKey: consumerSupportTicketKey(ticketId) });
    const joinAndCatchUp = async () => {
      await connection.invoke('JoinTicket', ticketId);
      await refreshTicket();
    };
    const scheduleStart = () => {
      if (disposed || retryTimer) return;
      retryTimer = setTimeout(() => {
        retryTimer = undefined;
        void start();
      }, 5_000);
    };
    const start = async () => {
      if (disposed || connection.state !== signalR.HubConnectionState.Disconnected) return;
      try {
        await connection.start();
        if (disposed) {
          await connection.stop();
          return;
        }
        await joinAndCatchUp();
      } catch {
        if (connection.state !== signalR.HubConnectionState.Disconnected) await connection.stop();
        scheduleStart();
      }
    };

    connection.on(MESSAGE_EVENT, (payload: MessageCreatedPayload) => {
      if (payload.ticketId !== ticketId) return;
      queryClient.setQueryData<ConsumerSupportTicket>(
        consumerSupportTicketKey(ticketId),
        (ticket) => appendSupportMessage(ticket, payload.message),
      );
    });
    connection.on(STATUS_EVENT, (payload: StatusChangedPayload) => {
      if (payload.ticketId !== ticketId) return;
      queryClient.setQueryData<ConsumerSupportTicket>(consumerSupportTicketKey(ticketId), (ticket) =>
        ticket ? { ...ticket, status: payload.status, updatedAt: payload.updatedAt } : ticket,
      );
    });
    connection.onreconnected(async () => {
      try {
        await joinAndCatchUp();
      } catch {
        await connection.stop();
        scheduleStart();
      }
    });
    connection.onclose(scheduleStart);
    void start();

    return () => {
      disposed = true;
      if (retryTimer) clearTimeout(retryTimer);
      connection.off(MESSAGE_EVENT);
      connection.off(STATUS_EVENT);
      if (connection.state === signalR.HubConnectionState.Connected) {
        void connection.invoke('LeaveTicket', ticketId).catch(() => undefined).finally(() => connection.stop());
      } else {
        void connection.stop();
      }
    };
  }, [personalAccessToken, queryClient, ticketId]);
}
