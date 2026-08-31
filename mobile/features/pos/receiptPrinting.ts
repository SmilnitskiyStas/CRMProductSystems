import * as Print from 'expo-print';
import type { SaleResponse } from './types';

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char] ?? char);
}

export function buildReceiptHtml(result: SaleResponse): string {
  const rows = result.items.map((item) => `<tr><td>${escapeHtml(item.productName)}</td><td>${item.quantity}</td><td>${item.lineTotal.toFixed(2)} ₴</td></tr>`).join('');
  return `<!doctype html><html><head><meta charset="utf-8"><style>body{font-family:Arial,sans-serif;width:280px;margin:0 auto;color:#111}h1{text-align:center;font-size:20px}table{width:100%;border-collapse:collapse}td{padding:6px 0;border-bottom:1px dashed #aaa}td:nth-child(2),td:last-child{text-align:right}.total{font-size:18px;font-weight:bold;display:flex;justify-content:space-between;margin-top:12px}.meta{text-align:center;font-size:11px;color:#555}</style></head><body><h1>Чек</h1><div class="meta">Транзакція #${escapeHtml(result.transactionId.slice(0, 8).toUpperCase())}</div>${result.fiscalNumber ? `<div class="meta">Фіскальний № ${escapeHtml(result.fiscalNumber)}</div>` : ''}<table>${rows}</table><div class="total"><span>Разом</span><span>${result.subtotal.toFixed(2)} ₴</span></div><p class="meta">Оплата: ${result.paymentType === 'Cash' ? 'Готівка' : 'Картка'}</p></body></html>`;
}

export async function printSaleReceipt(result: SaleResponse): Promise<void> {
  await Print.printAsync({ html: buildReceiptHtml(result) });
}
