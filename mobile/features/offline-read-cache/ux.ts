import type { QueryKey } from '@tanstack/react-query';
import { useNetInfo } from '@react-native-community/netinfo';
import { getOfflineReadMetadata } from './lifecycle';

export type OfflineReadUxKind =
  | 'hidden'
  | 'refreshing'
  | 'offline-cached'
  | 'stale'
  | 'no-data';

export interface OfflineReadUxState {
  kind: OfflineReadUxKind;
  message: string | null;
  canRetry: boolean;
}

export function formatOfflineReadTimestamp(timestamp: number, locale = 'uk-UA'): string {
  return new Intl.DateTimeFormat(locale, {
    day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit',
  }).format(new Date(timestamp));
}

export function deriveOfflineReadUx({
  online,
  hasData,
  isFetching,
  isError = false,
  lastSyncedAt,
  isStale,
}: {
  online: boolean;
  hasData: boolean;
  isFetching: boolean;
  isError?: boolean;
  lastSyncedAt?: number;
  isStale?: boolean;
}): OfflineReadUxState {
  if (!hasData || !lastSyncedAt) {
    if (!online) return {
      kind: 'no-data',
      message: 'Офлайн-даних немає або термін їх зберігання минув. Підключіться до інтернету та спробуйте знову.',
      canRetry: false,
    };
    return isFetching
      ? { kind: 'refreshing', message: 'Оновлюємо дані…', canRetry: false }
      : { kind: 'hidden', message: null, canRetry: true };
  }

  const updated = formatOfflineReadTimestamp(lastSyncedAt);
  if (!online) return {
    kind: isStale ? 'stale' : 'offline-cached',
    message: isStale
      ? `Офлайн-дані можуть бути застарілими. Оновлено ${updated}.`
      : `Офлайн-дані. Оновлено ${updated}.`,
    canRetry: false,
  };
  if (isError) return {
    kind: isStale ? 'stale' : 'offline-cached',
    message: isStale
      ? `Не вдалося оновити. Дані можуть бути застарілими. Оновлено ${updated}.`
      : `Не вдалося оновити. Показано збережені дані від ${updated}.`,
    canRetry: true,
  };
  if (isFetching) return {
    kind: 'refreshing',
    message: `Оновлюємо дані з сервера. Останнє оновлення ${updated}.`,
    canRetry: false,
  };
  if (isStale) return {
    kind: 'stale',
    message: `Дані можуть бути застарілими. Оновлено ${updated}.`,
    canRetry: true,
  };
  return { kind: 'hidden', message: null, canRetry: false };
}

export function getOfflineReadUx(
  queryKey: QueryKey,
  options: { online: boolean; hasData: boolean; isFetching: boolean; isError?: boolean; now?: number },
): OfflineReadUxState {
  const metadata = getOfflineReadMetadata(queryKey, options.now);
  return deriveOfflineReadUx({
    ...options,
    lastSyncedAt: metadata?.lastSyncedAt,
    isStale: metadata?.isStale,
  });
}

export function useOfflineReadUx(
  queryKey: QueryKey,
  options: { hasData: boolean; isFetching: boolean; isError?: boolean },
): OfflineReadUxState {
  const network = useNetInfo();
  // Unknown native reachability is not enough to announce an outage; the query remains authoritative.
  const online = network.isConnected !== false && network.isInternetReachable !== false;
  return getOfflineReadUx(queryKey, { ...options, online });
}
