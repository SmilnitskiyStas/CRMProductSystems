import { deriveOfflineReadUx, formatOfflineReadTimestamp } from '../ux';

describe('limited offline-read UX', () => {
  const syncedAt = Date.UTC(2026, 7, 1, 12, 30);

  it('formats the last successful update in Ukrainian local form', () => {
    expect(formatOfflineReadTimestamp(syncedAt)).toMatch(/01\.08.*\d{2}:\d{2}/);
  });

  it.each([
    [false, true, false, false, 'offline-cached', 'Офлайн-дані.'],
    [false, true, false, true, 'stale', 'можуть бути застарілими'],
    [true, true, true, false, 'refreshing', 'Оновлюємо дані з сервера'],
    [true, true, false, false, 'hidden', null],
    [false, false, false, false, 'no-data', 'термін їх зберігання минув'],
  ])('derives online=%s data=%s fetching=%s stale=%s as %s', (
    online, hasData, isFetching, isStale, kind, text,
  ) => {
    const state = deriveOfflineReadUx({
      online: online as boolean,
      hasData: hasData as boolean,
      isFetching: isFetching as boolean,
      isStale: isStale as boolean,
      lastSyncedAt: hasData ? syncedAt : undefined,
    });
    expect(state.kind).toBe(kind);
    if (text) expect(state.message).toContain(text as string);
    else expect(state.message).toBeNull();
  });

  it('offers retry only for online stale or failed current reads', () => {
    expect(deriveOfflineReadUx({ online: true, hasData: true, isFetching: false, isStale: true, lastSyncedAt: syncedAt }).canRetry).toBe(true);
    expect(deriveOfflineReadUx({ online: false, hasData: true, isFetching: false, isStale: true, lastSyncedAt: syncedAt }).canRetry).toBe(false);
  });

  it('keeps cached data visibly marked after an online refresh failure', () => {
    const state = deriveOfflineReadUx({ online: true, hasData: true, isFetching: false, isError: true, isStale: false, lastSyncedAt: syncedAt });
    expect(state).toMatchObject({ kind: 'offline-cached', canRetry: true });
    expect(state.message).toContain('Показано збережені дані');
  });
});
