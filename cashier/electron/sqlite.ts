import Database from 'better-sqlite3';
import type { CatalogItem, OutboxStatus } from '@shared/types';
import type { CatalogPersistence, PersistedCatalog } from './catalogStore';
import type { OutboxRepo, OutboxRow } from './outbox';

/**
 * Local SQLite database (userData/cashier.db): catalog cache, offline sales outbox, kv.
 * Only this file touches better-sqlite3 (a native module), so everything else stays unit-testable.
 */
export type Db = Database.Database;

const SCHEMA = `
CREATE TABLE IF NOT EXISTS catalog_items (
  id         TEXT PRIMARY KEY,
  name       TEXT NOT NULL,
  barcodes   TEXT NOT NULL,          -- JSON array
  price      REAL,
  vat_rate   REAL NOT NULL DEFAULT 0,
  unit       TEXT NOT NULL DEFAULT '',
  active     INTEGER NOT NULL DEFAULT 1,
  updated_at TEXT
);
CREATE TABLE IF NOT EXISTS outbox_sales (
  client_transaction_id TEXT PRIMARY KEY,
  payload               TEXT NOT NULL,   -- JSON CreateSaleRequest
  status                TEXT NOT NULL,   -- pending | sending | synced | rejected | dismissed
  attempts              INTEGER NOT NULL DEFAULT 0,
  last_error            TEXT,
  created_at            INTEGER NOT NULL,
  next_attempt_at       INTEGER NOT NULL DEFAULT 0,
  local_receipt_no      TEXT NOT NULL,
  server_receipt_no     TEXT,
  receipt               TEXT NOT NULL    -- JSON SaleReceipt snapshot (reprint / history)
);
CREATE INDEX IF NOT EXISTS ix_outbox_status ON outbox_sales(status, created_at);
CREATE TABLE IF NOT EXISTS kv (
  key   TEXT PRIMARY KEY,
  value TEXT
);
`;

export function openDatabase(file: string): Db {
  const db = new Database(file);
  db.pragma('journal_mode = WAL');
  db.pragma('synchronous = NORMAL');
  db.exec(SCHEMA);
  return db;
}

export class SqliteKv {
  constructor(private readonly db: Db) {}
  get(key: string): string | null {
    const r = this.db.prepare('SELECT value FROM kv WHERE key = ?').get(key) as { value: string | null } | undefined;
    return r?.value ?? null;
  }
  set(key: string, value: string | null): void {
    this.db
      .prepare('INSERT INTO kv(key, value) VALUES(?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value')
      .run(key, value);
  }
}

export class SqliteCatalogPersistence implements CatalogPersistence {
  constructor(
    private readonly db: Db,
    private readonly kv: SqliteKv,
  ) {}

  load(): PersistedCatalog {
    const rows = this.db
      .prepare('SELECT id, name, barcodes, price, vat_rate, unit FROM catalog_items WHERE active = 1')
      .all() as { id: string; name: string; barcodes: string; price: number | null; vat_rate: number; unit: string }[];
    const items: CatalogItem[] = rows.map((r) => ({
      id: r.id,
      name: r.name,
      barcodes: JSON.parse(r.barcodes) as string[],
      price: r.price,
      vatRate: r.vat_rate,
      unit: r.unit,
    }));
    return { items, cursor: this.kv.get('catalog.cursor'), lastSyncAt: this.kv.get('catalog.lastSyncAt') };
  }

  applyDelta(upserts: CatalogItem[], removedIds: string[], cursor: string | null, lastSyncAt: string | null): void {
    const up = this.db.prepare(
      `INSERT INTO catalog_items(id, name, barcodes, price, vat_rate, unit, active, updated_at)
       VALUES(@id, @name, @barcodes, @price, @vat, @unit, 1, @updatedAt)
       ON CONFLICT(id) DO UPDATE SET name=@name, barcodes=@barcodes, price=@price, vat_rate=@vat, unit=@unit, active=1, updated_at=@updatedAt`,
    );
    const del = this.db.prepare('DELETE FROM catalog_items WHERE id = ?');
    this.db.transaction(() => {
      for (const id of removedIds) del.run(id);
      for (const it of upserts) {
        up.run({
          id: it.id,
          name: it.name,
          barcodes: JSON.stringify(it.barcodes),
          price: it.price,
          vat: it.vatRate,
          unit: it.unit,
          updatedAt: cursor,
        });
      }
      this.kv.set('catalog.cursor', cursor);
      this.kv.set('catalog.lastSyncAt', lastSyncAt);
    })();
  }

  clear(): void {
    this.db.transaction(() => {
      this.db.exec('DELETE FROM catalog_items');
      this.kv.set('catalog.cursor', null);
      this.kv.set('catalog.lastSyncAt', null);
    })();
  }
}

interface RawOutbox {
  client_transaction_id: string;
  payload: string;
  status: OutboxStatus;
  attempts: number;
  last_error: string | null;
  created_at: number;
  next_attempt_at: number;
  local_receipt_no: string;
  server_receipt_no: string | null;
  receipt: string;
}

const toRow = (r: RawOutbox): OutboxRow => ({
  clientTransactionId: r.client_transaction_id,
  payload: JSON.parse(r.payload),
  status: r.status,
  attempts: r.attempts,
  lastError: r.last_error,
  createdAt: r.created_at,
  nextAttemptAt: r.next_attempt_at,
  localReceiptNo: r.local_receipt_no,
  serverReceiptNo: r.server_receipt_no,
  receipt: JSON.parse(r.receipt),
});

const COLUMN: Record<string, string> = {
  status: 'status',
  attempts: 'attempts',
  lastError: 'last_error',
  nextAttemptAt: 'next_attempt_at',
  serverReceiptNo: 'server_receipt_no',
  receipt: 'receipt',
  payload: 'payload',
  localReceiptNo: 'local_receipt_no',
  createdAt: 'created_at',
};

export class SqliteOutboxRepo implements OutboxRepo {
  constructor(
    private readonly db: Db,
    private readonly kv: SqliteKv,
  ) {}

  insert(row: OutboxRow): void {
    this.db
      .prepare(
        `INSERT INTO outbox_sales(client_transaction_id, payload, status, attempts, last_error, created_at,
           next_attempt_at, local_receipt_no, server_receipt_no, receipt)
         VALUES(?,?,?,?,?,?,?,?,?,?)`,
      )
      .run(
        row.clientTransactionId,
        JSON.stringify(row.payload),
        row.status,
        row.attempts,
        row.lastError,
        row.createdAt,
        row.nextAttemptAt,
        row.localReceiptNo,
        row.serverReceiptNo,
        JSON.stringify(row.receipt),
      );
  }

  get(id: string): OutboxRow | null {
    const r = this.db.prepare('SELECT * FROM outbox_sales WHERE client_transaction_id = ?').get(id) as RawOutbox | undefined;
    return r ? toRow(r) : null;
  }

  update(id: string, patch: Partial<Omit<OutboxRow, 'clientTransactionId'>>): void {
    const sets: string[] = [];
    const vals: unknown[] = [];
    for (const [k, v] of Object.entries(patch)) {
      const col = COLUMN[k];
      if (!col) continue;
      sets.push(`${col} = ?`);
      vals.push(k === 'receipt' || k === 'payload' ? JSON.stringify(v) : v);
    }
    if (sets.length === 0) return;
    this.db.prepare(`UPDATE outbox_sales SET ${sets.join(', ')} WHERE client_transaction_id = ?`).run(...vals, id);
  }

  listByStatus(statuses: OutboxStatus[], limit = 1000): OutboxRow[] {
    const ph = statuses.map(() => '?').join(',');
    return (
      this.db
        .prepare(`SELECT * FROM outbox_sales WHERE status IN (${ph}) ORDER BY created_at ASC LIMIT ?`)
        .all(...statuses, limit) as RawOutbox[]
    ).map(toRow);
  }

  listRecent(limit: number): OutboxRow[] {
    return (
      this.db
        .prepare("SELECT * FROM outbox_sales WHERE status != 'dismissed' ORDER BY created_at DESC LIMIT ?")
        .all(limit) as RawOutbox[]
    ).map(toRow);
  }

  count(statuses: OutboxStatus[]): number {
    const ph = statuses.map(() => '?').join(',');
    const r = this.db.prepare(`SELECT COUNT(*) AS n FROM outbox_sales WHERE status IN (${ph})`).get(...statuses) as { n: number };
    return r.n;
  }

  nextSeq(): number {
    return this.db.transaction(() => {
      const n = Number(this.kv.get('receipt.seq') ?? '0') + 1;
      this.kv.set('receipt.seq', String(n));
      return n;
    })();
  }
}
