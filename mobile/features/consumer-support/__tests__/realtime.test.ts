import { appendSupportMessage, resolveConsumerSupportHubUrl } from '../realtime';
import type { ConsumerSupportMessage, ConsumerSupportTicket } from '../types';

const message: ConsumerSupportMessage = {
  id: 'message-a',
  ticketId: 'ticket-a',
  senderConsumerAccountId: 'consumer-a',
  senderUserId: null,
  body: 'Добрий день',
  isRead: false,
  createdAt: '2026-08-25T12:00:00Z',
};

const ticket: ConsumerSupportTicket = {
  id: 'ticket-a',
  tenantId: 'tenant-a',
  subject: 'Запитання',
  status: 'open',
  createdAt: '2026-08-25T11:00:00Z',
  updatedAt: '2026-08-25T11:00:00Z',
  messages: [],
};

describe('consumer support realtime contract', () => {
  it('builds the Hub URL next to the configured API route', () => {
    expect(resolveConsumerSupportHubUrl('https://example.test/api')).toBe(
      'https://example.test/api/hubs/consumer-support',
    );
  });

  it('appends a pushed message once and ignores the REST/SignalR echo duplicate', () => {
    const updated = appendSupportMessage(ticket, message);
    expect(updated?.messages).toEqual([message]);
    expect(appendSupportMessage(updated, message)).toBe(updated);
  });
});
