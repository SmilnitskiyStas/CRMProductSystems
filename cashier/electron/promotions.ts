import type { PromotionsState } from '@shared/types';
import type { PromotionDefinition, PromotionRule } from '@shared/promotions/engine';

/** Wire shape of GET /api/pos/sync/promotions (SyncPromotionsResponse). */
export interface SyncPromotionsWire {
  etag: string;
  notModified: boolean;
  serverTime: string;
  /** IANA zone of the register's store; null/absent on older servers. */
  storeTimeZone?: string | null;
  rules: { id: string; versionId: string; name: string; createdAt: string; definition: PromotionDefinition }[];
}

export interface StoredPromotions {
  rules: PromotionRule[];
  etag: string | null;
  fetchedAt: string | null;
  storeTimeZone?: string | null;
}

/** Durable side: SQLite kv in the app, in-memory in tests. */
export interface PromotionsPersistence {
  load(): StoredPromotions | null;
  save(s: StoredPromotions): void;
  clear(): void;
}

export class MemoryPromotionsPersistence implements PromotionsPersistence {
  stored: StoredPromotions | null = null;
  load(): StoredPromotions | null {
    return this.stored;
  }
  save(s: StoredPromotions): void {
    this.stored = s;
  }
  clear(): void {
    this.stored = null;
  }
}

export type FetchPromotions = (etag: string | null) => Promise<SyncPromotionsWire>;

/**
 * Local copy of the published promotion rules (works offline). Refreshed with an etag so an
 * unchanged set costs one tiny request: on login, after reconnect, when a shift opens and every
 * ~60 s while online. A failed refresh keeps the cached rules.
 */
export class PromotionsStore {
  private rules: PromotionRule[] = [];
  private etag: string | null = null;
  private fetchedAt: string | null = null;
  private storeTimeZone: string | null = null;
  private error: string | null = null;
  private syncing: Promise<PromotionsState> | null = null;

  constructor(
    private readonly persistence: PromotionsPersistence,
    private readonly fetchRules: FetchPromotions,
    private readonly onChange?: (s: PromotionsState) => void,
    private readonly now: () => Date = () => new Date(),
  ) {}

  load(): void {
    try {
      const s = this.persistence.load();
      if (s) {
        this.rules = s.rules;
        this.etag = s.etag;
        this.fetchedAt = s.fetchedAt;
        this.storeTimeZone = s.storeTimeZone ?? null;
      }
    } catch {
      /* unreadable cache: start empty */
    }
  }

  state(): PromotionsState {
    return {
      rules: this.rules,
      etag: this.etag,
      fetchedAt: this.fetchedAt,
      storeTimeZone: this.storeTimeZone,
      syncing: this.syncing !== null,
      error: this.error,
    };
  }

  sync(): Promise<PromotionsState> {
    if (this.syncing) return this.syncing;
    const run = this.doSync().then(() => {
      this.syncing = null;
      const s = this.state();
      this.onChange?.(s);
      return s;
    });
    this.syncing = run;
    return run;
  }

  private async doSync(): Promise<void> {
    try {
      const res = await this.fetchRules(this.etag);
      this.fetchedAt = this.now().toISOString();
      if (res.storeTimeZone !== undefined) this.storeTimeZone = res.storeTimeZone; // absent on old servers: keep what we had
      if (!res.notModified) {
        this.rules = res.rules.map((r) => ({
          id: r.id,
          versionId: r.versionId,
          name: r.name,
          createdAt: r.createdAt,
          definition: r.definition,
        }));
        this.etag = res.etag;
      }
      this.error = null;
      this.persistence.save({
        rules: this.rules,
        etag: this.etag,
        fetchedAt: this.fetchedAt,
        storeTimeZone: this.storeTimeZone,
      });
    } catch (e) {
      this.error = (e as Error).message; // keep the cached rules
    }
  }

  clear(): void {
    this.rules = [];
    this.etag = null;
    this.fetchedAt = null;
    this.storeTimeZone = null;
    this.error = null;
    this.persistence.clear();
    this.onChange?.(this.state());
  }
}
