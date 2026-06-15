import { useQuery } from '@tanstack/react-query';
import { getNotificationHistory } from '../api/notificationApi';

export function useNotificationHistory() {
  return useQuery({
    queryKey: ['notifications'],
    queryFn: getNotificationHistory,
    refetchInterval: 60_000,
  });
}
