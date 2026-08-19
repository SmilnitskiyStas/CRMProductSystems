import { apiClient } from '@/lib/api-client';
import { getNotificationHistory } from '../notificationApi';
import type { PagedResult, Notification } from '../../types';

jest.mock('@/lib/api-client', () => ({
  apiClient: {
    get: jest.fn(),
  },
}));

const mockGet = apiClient.get as jest.MockedFunction<typeof apiClient.get>;

describe('getNotificationHistory', () => {
  test('returns the paged result without treating the response as a raw array', async () => {
    const page: PagedResult<Notification> = {
      items: [
        {
          id: 'notification-1',
          eventType: 'stock_low',
          channel: 'push',
          status: 'sent',
          payload: null,
          createdAt: '2026-07-29T10:00:00Z',
          isRead: false,
          readAt: null,
          title: 'Low stock',
          storeId: 'location-1',
          userId: 'user-1',
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };
    mockGet.mockResolvedValueOnce({ data: page });

    await expect(getNotificationHistory()).resolves.toEqual(page);
    expect(mockGet).toHaveBeenCalledWith('/notifications/history');
  });
});
