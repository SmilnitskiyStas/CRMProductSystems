import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';
import type { CatalogItem, CatalogStatus } from '@shared/types';

/**
 * Local product catalog used for fast barcode lookup at the till.
 * Phase 3 ships the in-memory + JSON-file implementation; Phase 4 swaps in SQLite behind the
 * same interface (the sync cursor and delta semantics stay the same).
 */
export interface CatalogStore {
  load(): Promise<void>;
  /** Pulls the delta from the server and applies it. */
  sync(): Promise<CatalogStatus>;
  findByBarcode(barcode: string): CatalogItem | null;
  search(query: string, limit?: number): CatalogItem[];
  status(): CatalogStatus;
  /** Drops cached data (e.g. when the register is re-activated against another tenant). */
  clear(): Promise<void>;
}

/** Wire shape of GET /api/pos/sync/catalog (see SyncCatalogResponse). */
export interface SyncCatalogItemWire {
  id: string;
  name: string;
  barcodes: string[];
  priceRetail: number | null;
  vatRate: number;
  unit: string;
  isActive: boolean;
  updatedAt: string;
}

export interface SyncCatalogPageWire {
  items: SyncCatalogItemWire[];
  hasMore: boolean;
  nextSince: string | null;
  nextAfterId: string | null;
  serverTime: string;
}

export interface CatalogCursor {
  since: string | null;
  afterId: string | null;
}

export type FetchCatalogPage = (cursor: CatalogCursor) => Promise<SyncCatalogPageWire>;

interface PersistedCatalog {
  version: 1;
  cursor: string | null; // ISO "since" to resume from (already moved back by the overlap)
  lastSyncAt: string | null;
  items: CatalogItem[];
}

/** UpdatedAt is stamped at statement time, so resume slightly before the saved cursor and upsert by id. */
export const CURSOR_OVERLAP_MS = 2 * 60 * 1000;
const MAX_PAGES = 500; // safety net against a server that never reports hasMore=false

export class MemoryCatalogStore implements CatalogStore {
  private items = new Map<string, CatalogItem>();
  private byBarcode = new Map<string, string>();
  private cursor: string | null = null;
  private lastSyncAt: string | null = null;
  private syncing: Promise<CatalogStatus> | null = null;
  private error: string | null = null;

  constructor(
    private readonly filePath: string,
    private readonly fetchPage: FetchCatalogPage,
    private readonly onChange?: (s: CatalogStatus) => void,
  ) {}

  async load(): Promise<void> {
    try {
      const raw = JSON.parse(await readFile(this.filePath, 'utf8')) as PersistedCatalog;
      if (raw.version !== 1) return;
      this.items.clear();
      this.byBarcode.clear();
      for (const it of raw.items) this.index(it);
      this.cursor = raw.cursor;
      this.lastSyncAt = raw.lastSyncAt;
    } catch {
      /* no cache yet or unreadable: start empty, a full sync follows */
    }
  }

  private index(it: CatalogItem): void {
    this.items.set(it.id, it);
    for (const b of it.barcodes) this.byBarcode.set(b, it.id);
  }

  private unindex(id: string): void {
    const old = this.items.get(id);
    if (!old) return;
    for (const b of old.barcodes) if (this.byBarcode.get(b) === id) this.byBarcode.delete(b);
    this.items.delete(id);
  }

  sync(): Promise<CatalogStatus> {
    if (this.syncing) return this.syncing;
    const run = this.doSync().then(() => {
      this.syncing = null;
      const status = this.status();
      this.onChange?.(status);
      return status;
    });
    this.syncing = run;
    this.onChange?.(this.status());
    return run;
  }

  private async doSync(): Promise<void> {
    try {
      let cursor: CatalogCursor = {
        since: this.cursor ? new Date(new Date(this.cursor).getTime() - CURSOR_OVERLAP_MS).toISOString() : null,
        afterId: null,
      };
      let lastNextSince: string | null = null;
      for (let page = 0; page < MAX_PAGES; page++) {
        const res = await this.fetchPage(cursor);
        for (const w of res.items) {
          this.unindex(w.id);
          if (w.isActive) {
            this.index({
              id: w.id,
              name: w.name,
              barcodes: w.barcodes,
              price: w.priceRetail,
              vatRate: w.vatRate,
              unit: w.unit,
            });
          }
        }
        if (res.nextSince) lastNextSince = res.nextSince;
        if (!res.hasMore) break;
        cursor = { since: res.nextSince, afterId: res.nextAfterId };
      }
      if (lastNextSince) this.cursor = lastNextSince;
      this.lastSyncAt = new Date().toISOString();
      this.error = null;
      await this.persist();
    } catch (e) {
      this.error = (e as Error).message;
    }
  }

  private async persist(): Promise<void> {
    const data: PersistedCatalog = {
      version: 1,
      cursor: this.cursor,
      lastSyncAt: this.lastSyncAt,
      items: [...this.items.values()],
    };
    await mkdir(dirname(this.filePath), { recursive: true });
    const tmp = this.filePath + '.tmp';
    await writeFile(tmp, JSON.stringify(data), 'utf8');
    await rename(tmp, this.filePath);
  }

  findByBarcode(barcode: string): CatalogItem | null {
    const id = this.byBarcode.get(barcode.trim());
    return id ? (this.items.get(id) ?? null) : null;
  }

  search(query: string, limit = 30): CatalogItem[] {
    const q = query.trim().toLowerCase();
    if (!q) return [];
    const starts: CatalogItem[] = [];
    const contains: CatalogItem[] = [];
    for (const it of this.items.values()) {
      const name = it.name.toLowerCase();
      if (name.startsWith(q) || it.barcodes.some((b) => b.startsWith(q))) starts.push(it);
      else if (name.includes(q) || it.barcodes.some((b) => b.includes(q))) contains.push(it);
      if (starts.length >= limit) break;
    }
    return [...starts, ...contains].slice(0, limit);
  }

  status(): CatalogStatus {
    return {
      count: this.items.size,
      lastSyncAt: this.lastSyncAt,
      syncing: this.syncing !== null,
      error: this.error,
    };
  }

  async clear(): Promise<void> {
    this.items.clear();
    this.byBarcode.clear();
    this.cursor = null;
    this.lastSyncAt = null;
    this.error = null;
    await this.persist().catch(() => undefined);
  }
}
