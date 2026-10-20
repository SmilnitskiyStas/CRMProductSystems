import type { OutboxStatus } from '@shared/types';
import type { OutboxRepo, OutboxRow } from './outbox';

/** In-memory OutboxRepo used by unit tests. */
export class MemoryOutboxRepo implements OutboxRepo {
  rows = new Map<string, OutboxRow>();
  seq = 0;

  insert(row: OutboxRow): void {
    if (this.rows.has(row.clientTransactionId)) throw new Error('duplicate clientTransactionId');
    this.rows.set(row.clientTransactionId, { ...row });
  }
  get(id: string): OutboxRow | null {
    const r = this.rows.get(id);
    return r ? { ...r } : null;
  }
  update(id: string, patch: Partial<Omit<OutboxRow, 'clientTransactionId'>>): void {
    const r = this.rows.get(id);
    if (r) this.rows.set(id, { ...r, ...patch });
  }
  listByStatus(statuses: OutboxStatus[], limit = 1000): OutboxRow[] {
    return [...this.rows.values()]
      .filter((r) => statuses.includes(r.status))
      .sort((a, b) => a.createdAt - b.createdAt)
      .slice(0, limit)
      .map((r) => ({ ...r }));
  }
  listRecent(limit: number): OutboxRow[] {
    return [...this.rows.values()]
      .filter((r) => r.status !== 'dismissed')
      .sort((a, b) => b.createdAt - a.createdAt)
      .slice(0, limit)
      .map((r) => ({ ...r }));
  }
  count(statuses: OutboxStatus[]): number {
    return [...this.rows.values()].filter((r) => statuses.includes(r.status)).length;
  }
  nextSeq(): number {
    return ++this.seq;
  }
}
