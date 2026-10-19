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

export interface PersistedCatalog {
  cursor: string | null; // ISO updatedAt cursor to resume from (the overlap is applied on resume)
  lastSyncAt: string | null;
  items: CatalogItem[];
}

/** Durable side of the catalog: SQLite in the app, in-memory in tests. */
export interface CatalogPersistence {
  load(): PersistedCatalog;
  /** Applies one sync atomically: upsert changed items, delete removed ids, store the cursor. */
  applyDelta(upserts: CatalogItem[], removedIds: string[], cursor: string | null, lastSyncAt: string | null): void;
  clear(): void;
}

export class MemoryCatalogPersistence implements CatalogPersistence {
  private items = new Map<string, CatalogItem>();
  private cursor: string | null = null;
  private lastSyncAt: string | null = null;
  load(): PersistedCatalog {
    return { cursor: this.cursor, lastSyncAt: this.lastSyncAt, items: [...this.items.values()] };
  }
  applyDelta(upserts: CatalogItem[], removedIds: string[], cursor: string | null, lastSyncAt: string | null): void {
    for (const id of removedIds) this.items.delete(id);
    for (const it of upserts) this.items.set(it.id, it);
    this.cursor = cursor;
    this.lastSyncAt = lastSyncAt;
  }
  clear(): void {
    this.items.clear();
    this.cursor = null;
    this.lastSyncAt = null;
  }
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

  /** In-memory index for instant barcode lookup; the persistence layer keeps it across restarts (works offline). */
  constructor(
    private readonly persistence: CatalogPersistence,
    private readonly fetchPage: FetchCatalogPage,
    private readonly onChange?: (s: CatalogStatus) => void,
  ) {}

  async load(): Promise<void> {
    try {
      const raw = this.persistence.load();
      this.items.clear();
      this.byBarcode.clear();
      for (const it of raw.items) this.index(it);
      this.cursor = raw.cursor;
      this.lastSyncAt = raw.lastSyncAt;
    } catch {
      /* unreadable cache: start empty, a full sync follows */
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
        since: this.cursor && this.items.size > 0 ? new Date(new Date(this.cursor).getTime() - CURSOR_OVERLAP_MS).toISOString() : null,
        afterId: null,
      };
      let lastNextSince: string | null = null;
      const upserts = new Map<string, CatalogItem>();
      const removed = new Set<string>();
      for (let page = 0; page < MAX_PAGES; page++) {
        const res = await this.fetchPage(cursor);
        for (const w of res.items) {
          this.unindex(w.id);
          if (w.isActive) {
            const item: CatalogItem = {
              id: w.id,
              name: w.name,
              barcodes: w.barcodes,
              price: w.priceRetail,
              vatRate: w.vatRate,
              unit: w.unit,
            };
            this.index(item);
            upserts.set(w.id, item);
            removed.delete(w.id);
          } else {
            upserts.delete(w.id);
            removed.add(w.id);
          }
        }
        if (res.nextSince) lastNextSince = res.nextSince;
        if (!res.hasMore) break;
        cursor = { since: res.nextSince, afterId: res.nextAfterId };
      }
      if (lastNextSince) this.cursor = lastNextSince;
      this.lastSyncAt = new Date().toISOString();
      this.error = null;
      this.persistence.applyDelta([...upserts.values()], [...removed], this.cursor, this.lastSyncAt);
    } catch (e) {
      this.error = (e as Error).message;
    }
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
    this.persistence.clear();
  }
}
