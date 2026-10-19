import { describe, expect, it, vi } from 'vitest';
import { CATALOG_SCHEMA_VERSION, effectiveCursor, MemoryCatalogPersistence, MemoryCatalogStore } from './catalogStore';
import { MemoryPromotionsPersistence, PromotionsStore, type SyncPromotionsWire } from './promotions';

const wire = (etag: string, names: string[], notModified = false): SyncPromotionsWire => ({
  etag,
  notModified,
  serverTime: 't',
  rules: notModified
    ? []
    : names.map((n, i) => ({
        id: `00000000-0000-0000-0000-00000000010${i}`,
        versionId: `00000000-0000-0000-0000-00000000020${i}`,
        name: n,
        createdAt: '2026-01-01T00:00:00Z',
        definition: { type: 'percent_off', targets: { itemIds: ['a'] }, params: { percent: 10 } },
      })),
});

describe('PromotionsStore', () => {
  it('stores rules + etag, sends the etag next time and keeps the rules on notModified', async () => {
    const persistence = new MemoryPromotionsPersistence();
    const calls: (string | null)[] = [];
    const responses = [wire('e1', ['-10%']), wire('e1', [], true)];
    const store = new PromotionsStore(persistence, async (etag) => {
      calls.push(etag);
      return responses.shift()!;
    });
    await store.sync();
    expect(store.state().rules.map((r) => r.name)).toEqual(['-10%']);
    await store.sync();
    expect(calls).toEqual([null, 'e1']);
    expect(store.state().rules).toHaveLength(1);
    expect(store.state().etag).toBe('e1');
    expect(persistence.stored?.rules).toHaveLength(1);
  });

  it('replaces the rule set when the etag changes', async () => {
    const responses = [wire('e1', ['a', 'b']), wire('e2', ['c'])];
    const store = new PromotionsStore(new MemoryPromotionsPersistence(), async () => responses.shift()!);
    await store.sync();
    await store.sync();
    expect(store.state().rules.map((r) => r.name)).toEqual(['c']);
    expect(store.state().etag).toBe('e2');
  });

  it('survives a restart and an offline failure: cached rules keep working, the error is reported', async () => {
    const persistence = new MemoryPromotionsPersistence();
    await new PromotionsStore(persistence, async () => wire('e1', ['-10%'])).sync();

    const offline = new PromotionsStore(persistence, async () => {
      throw new Error('offline');
    });
    offline.load();
    expect(offline.state().rules).toHaveLength(1);
    const st = await offline.sync();
    expect(st.error).toBe('offline');
    expect(st.rules).toHaveLength(1);
    expect(st.etag).toBe('e1');
  });

  it('is single-flight and notifies listeners', async () => {
    const fetchRules = vi.fn(async () => wire('e1', ['x']));
    const seen: number[] = [];
    const store = new PromotionsStore(new MemoryPromotionsPersistence(), fetchRules, (s) => seen.push(s.rules.length));
    await Promise.all([store.sync(), store.sync()]);
    expect(fetchRules).toHaveBeenCalledTimes(1);
    expect(seen).toEqual([1]);
  });

  it('clear() wipes rules and etag (re-activation on another register)', async () => {
    const p = new MemoryPromotionsPersistence();
    const store = new PromotionsStore(p, async () => wire('e1', ['x']));
    await store.sync();
    store.clear();
    expect(store.state()).toMatchObject({ rules: [], etag: null });
    expect(p.stored).toBeNull();
  });
});

describe('catalog schema upgrade (categoryId)', () => {
  it('drops the saved cursor below schema v2 so one full resync runs', () => {
    expect(effectiveCursor(0, '2026-10-18T10:00:00Z')).toBeNull();
    expect(effectiveCursor(1, '2026-10-18T10:00:00Z')).toBeNull();
    expect(effectiveCursor(CATALOG_SCHEMA_VERSION, '2026-10-18T10:00:00Z')).toBe('2026-10-18T10:00:00Z');
  });

  it('a store loaded with items but no cursor refetches everything (since=null) and picks up categoryId', async () => {
    const persistence = new MemoryCatalogPersistence();
    persistence.applyDelta([{ id: '1', name: 'Old row', barcodes: ['111'], price: 10, vatRate: 20, unit: 'pcs' }], [], null, null);
    let seen: unknown;
    const store = new MemoryCatalogStore(persistence, async (c) => {
      seen = c;
      return {
        items: [
          { id: '1', name: 'Old row', barcodes: ['111'], priceRetail: 10, vatRate: 20, unit: 'pcs', isActive: true, updatedAt: 't', categoryId: 'cat-1' },
        ],
        hasMore: false,
        nextSince: '2026-10-19T00:00:00Z',
        nextAfterId: '1',
        serverTime: 't',
      };
    });
    await store.load();
    expect(store.findByBarcode('111')?.categoryId).toBeUndefined(); // usable offline, category unknown yet
    await store.sync();
    expect(seen).toEqual({ since: null, afterId: null });
    expect(store.findByBarcode('111')?.categoryId).toBe('cat-1');
  });
});
