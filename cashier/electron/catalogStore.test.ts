import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CURSOR_OVERLAP_MS, MemoryCatalogStore, type SyncCatalogItemWire, type SyncCatalogPageWire } from './catalogStore';

const wire = (id: string, name: string, barcodes: string[], over: Partial<SyncCatalogItemWire> = {}): SyncCatalogItemWire => ({
  id,
  name,
  barcodes,
  priceRetail: 10,
  vatRate: 20,
  unit: 'pcs',
  isActive: true,
  updatedAt: '2026-10-18T10:00:00Z',
  ...over,
});

let dir: string;
beforeEach(async () => {
  dir = await mkdtemp(join(tmpdir(), 'cashier-cat-'));
});
afterEach(async () => {
  await rm(dir, { recursive: true, force: true });
});

describe('MemoryCatalogStore', () => {
  it('pages through the delta, indexes barcodes, drops inactive items', async () => {
    const pages: SyncCatalogPageWire[] = [
      { items: [wire('1', 'Milk', ['111', '112'])], hasMore: true, nextSince: '2026-10-18T10:00:00Z', nextAfterId: '1', serverTime: 't' },
      { items: [wire('2', 'Bread', ['222']), wire('3', 'Old', ['333'], { isActive: false })], hasMore: false, nextSince: '2026-10-18T10:05:00Z', nextAfterId: '3', serverTime: 't' },
    ];
    const calls: unknown[] = [];
    const store = new MemoryCatalogStore(join(dir, 'c.json'), async (c) => {
      calls.push(c);
      return pages.shift()!;
    });
    const st = await store.sync();
    expect(st.count).toBe(2);
    expect(store.findByBarcode('112')?.name).toBe('Milk');
    expect(store.findByBarcode('333')).toBeNull();
    expect(calls[1]).toEqual({ since: '2026-10-18T10:00:00Z', afterId: '1' });
  });

  it('removes an item that became inactive and re-indexes changed barcodes', async () => {
    const batches: SyncCatalogPageWire[] = [
      { items: [wire('1', 'Milk', ['111'])], hasMore: false, nextSince: '2026-10-18T10:00:00Z', nextAfterId: '1', serverTime: 't' },
      { items: [wire('1', 'Milk', ['999'])], hasMore: false, nextSince: '2026-10-18T10:10:00Z', nextAfterId: '1', serverTime: 't' },
      { items: [wire('1', 'Milk', ['999'], { isActive: false })], hasMore: false, nextSince: '2026-10-18T10:20:00Z', nextAfterId: '1', serverTime: 't' },
    ];
    const store = new MemoryCatalogStore(join(dir, 'c.json'), async () => batches.shift()!);
    await store.sync();
    await store.sync();
    expect(store.findByBarcode('111')).toBeNull();
    expect(store.findByBarcode('999')?.id).toBe('1');
    await store.sync();
    expect(store.findByBarcode('999')).toBeNull();
    expect(store.status().count).toBe(0);
  });

  it('persists, reloads and resumes from the cursor minus the overlap', async () => {
    const path = join(dir, 'c.json');
    const first = new MemoryCatalogStore(path, async () => ({
      items: [wire('1', 'Milk', ['111'])],
      hasMore: false,
      nextSince: '2026-10-18T10:00:00.000Z',
      nextAfterId: '1',
      serverTime: 't',
    }));
    await first.sync();

    let seen: unknown;
    const second = new MemoryCatalogStore(path, async (c) => {
      seen = c;
      return { items: [], hasMore: false, nextSince: null, nextAfterId: null, serverTime: 't' };
    });
    await second.load();
    expect(second.findByBarcode('111')?.name).toBe('Milk');
    await second.sync();
    expect(seen).toEqual({
      since: new Date(new Date('2026-10-18T10:00:00.000Z').getTime() - CURSOR_OVERLAP_MS).toISOString(),
      afterId: null,
    });
  });

  it('keeps cached data and reports an error when the server is unreachable', async () => {
    let fail = false;
    const store = new MemoryCatalogStore(join(dir, 'c.json'), async () => {
      if (fail) throw new Error('offline');
      return { items: [wire('1', 'Milk', ['111'])], hasMore: false, nextSince: '2026-10-18T10:00:00Z', nextAfterId: '1', serverTime: 't' };
    });
    await store.sync();
    fail = true;
    const st = await store.sync();
    expect(st.error).toBe('offline');
    expect(st.count).toBe(1);
  });

  it('searches by name and barcode prefix', async () => {
    const store = new MemoryCatalogStore(join(dir, 'c.json'), async () => ({
      items: [wire('1', 'Milk 2.5%', ['111']), wire('2', 'Chocolate milk', ['222']), wire('3', 'Bread', ['4820'])],
      hasMore: false,
      nextSince: null,
      nextAfterId: null,
      serverTime: 't',
    }));
    await store.sync();
    expect(store.search('milk').map((i) => i.id)).toEqual(['1', '2']);
    expect(store.search('482').map((i) => i.id)).toEqual(['3']);
    expect(store.search('')).toEqual([]);
  });
});
