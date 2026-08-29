import type { ReimbursementType } from './types';

export function calculateReimbursement(
  quantity: number,
  purchasePrice: number | null,
  type: ReimbursementType | null,
  value: number | null,
): number {
  if (!type || value == null || value < 0) return 0;
  if (type === 'fixed') return quantity * value;
  return quantity * (purchasePrice ?? 0) * value / 100;
}

export function money(value: number | null | undefined): string {
  return value == null ? '—' : `${value.toFixed(2)} ₴`;
}
