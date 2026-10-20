import { describe, expect, it } from 'vitest';
import { MemoryPromotionsPersistence, PromotionsStore, type SyncPromotionsWire } from './promotions';

const wire = (etag: string, zone: string | null | undefined, notModified = false): SyncPromotionsWire => ({
  etag,
  notModified,
  serverTime: 't',
  ...(zone === undefined ? {} : { storeTimeZone: zone }),
  rules: notModified
    ? []
    : [
        {
          id: '00000000-0000-0000-0000-000000000101',
          versionId: '00000000-0000-0000-0000-000000000201',
          name: 'r',
          createdAt: '2026-01-01T00:00:00Z',
          definition: { type: 'percent_off', targets: { itemIds: ['a'] }, params: { percent: 10 } },
        },
      ],
});

describe('PromotionsStore storeTimeZone', () => {
  it('stores and returns the zone, persists it for offline restarts', async () => {
    const p = new MemoryPromotionsPersistence();
    await new PromotionsStore(p, async () => wire('e1', 'Europe/Kyiv')).sync();
    expect(p.stored?.storeTimeZone).toBe('Europe/Kyiv');

    const restarted = new PromotionsStore(p, async () => {
      throw new Error('offline');
    });
    restarted.load();
    expect(restarted.state().storeTimeZone).toBe('Europe/Kyiv');
    expect((await restarted.sync()).storeTimeZone).toBe('Europe/Kyiv'); // survives a failed refresh
  });

  it('refetches when the etag changes (zone changed on the server) and takes the new zone', async () => {
    const calls: (string | null)[] = [];
    const responses = [wire('e1', 'Europe/Kyiv'), wire('e2', 'Europe/Warsaw')];
    const store = new PromotionsStore(new MemoryPromotionsPersistence(), async (etag) => {
      calls.push(etag);
      return responses.shift()!;
    });
    await store.sync();
    await store.sync();
    expect(calls).toEqual([null, 'e1']);
    expect(store.state().storeTimeZone).toBe('Europe/Warsaw');
    expect(store.state().etag).toBe('e2');
  });

  it('an older server without the field leaves the zone null; notModified keeps the stored zone', async () => {
    const responses = [wire('e1', undefined), wire('e1', undefined, true)];
    const store = new PromotionsStore(new MemoryPromotionsPersistence(), async () => responses.shift()!);
    await store.sync();
    expect(store.state().storeTimeZone).toBeNull();

    const store2 = new PromotionsStore(new MemoryPromotionsPersistence(), async (etag) => (etag ? wire('e1', null, true) : wire('e1', 'Europe/Kyiv')));
    await store2.sync();
    await store2.sync(); // notModified response carries storeTimeZone:null -> that is what the server says now
    expect(store2.state().storeTimeZone).toBeNull();
  });

  it('clear() resets the zone', async () => {
    const store = new PromotionsStore(new MemoryPromotionsPersistence(), async () => wire('e1', 'Europe/Kyiv'));
    await store.sync();
    store.clear();
    expect(store.state().storeTimeZone).toBeNull();
  });
});
