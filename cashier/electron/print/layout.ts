import type { PrintSettings, SaleReceipt } from '@shared/types';
import { EscPosBuilder } from './builder';

export const columnsFor = (widthMm: 58 | 80): number => (widthMm === 58 ? 32 : 48);

/** Word-wraps to `width` characters; words longer than the width are hard-split. */
export function wrapText(text: string, width: number): string[] {
  const lines: string[] = [];
  let cur = '';
  for (const word of text.split(/\s+/).filter(Boolean)) {
    let w = word;
    while (w.length > width) {
      if (cur) {
        lines.push(cur);
        cur = '';
      }
      lines.push(w.slice(0, width));
      w = w.slice(width);
    }
    if (!cur) cur = w;
    else if (cur.length + 1 + w.length <= width) cur += ' ' + w;
    else {
      lines.push(cur);
      cur = w;
    }
  }
  if (cur) lines.push(cur);
  return lines.length ? lines : [''];
}

/** `left` ... `right` on one line; the left part is truncated if both do not fit. */
export function twoCol(left: string, right: string, width: number): string {
  const room = width - right.length - 1;
  const l = left.length > room ? left.slice(0, Math.max(room, 0)) : left;
  return l + ' '.repeat(Math.max(width - l.length - right.length, 1)) + right;
}

export const center = (s: string, width: number): string =>
  s.length >= width ? s : ' '.repeat(Math.floor((width - s.length) / 2)) + s;

const money = (n: number): string => n.toFixed(2).replace('.', ',');
const qtyStr = (q: number): string => (Number.isInteger(q) ? String(q) : String(q).replace('.', ','));

function fmtDate(iso: string): string {
  const d = new Date(iso);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

export interface ReceiptOptions {
  openDrawer: boolean;
  /** Footer line marking a reprint. */
  reprint?: boolean;
}

/** Receipt body as plain text lines (also what snapshot/wrapping tests inspect). */
export function receiptLines(r: SaleReceipt, cols: number, opts: Pick<ReceiptOptions, 'reprint'> = {}): string[] {
  const out: string[] = [];
  const sep = '-'.repeat(cols);
  const fiscalized = r.fiscalStatus === 'fiscalized' && !!r.fiscalNumber;

  out.push(...wrapText(`Каса №${r.registerNumber ?? '-'}${r.registerName ? ' ' + r.registerName : ''}`, cols));
  if (r.cashierName) out.push(...wrapText(`Касир: ${r.cashierName}`, cols));
  out.push(fmtDate(r.createdAt));
  const rcpt = r.localReceiptNo && r.localReceiptNo !== r.receiptNumber
    ? `Чек ${r.receiptNumber} (лок. ${r.localReceiptNo})`
    : `Чек ${r.receiptNumber}${r.local ? ' (локальний)' : ''}`;
  out.push(...wrapText(rcpt, cols));
  out.push(sep);

  // Promotions split one product into sub-lines (2 x 42,73 + 1 x 0,01): print the name once,
  // then one row per sub-line; promo sub-lines get the rule label underneath.
  let savedKop = 0;
  let prev: string | null = null;
  for (const it of r.items) {
    const key = `${it.barcode}|${it.name}`;
    if (key !== prev) out.push(...wrapText(it.name, cols));
    prev = key;
    out.push(twoCol(`  ${qtyStr(it.quantity)} x ${money(it.unitPrice)}`, money(it.total), cols));
    if (it.promoLabel) out.push(...wrapText(`Акція: ${it.promoLabel}`, cols - 2).map((l) => `  ${l}`));
    if (it.discount && it.discount > 0) out.push(twoCol('  Знижка', '-' + money(it.discount), cols));
    if (it.promoLabel && it.regularUnitPrice !== undefined && it.regularUnitPrice > it.unitPrice) {
      savedKop += Math.round((it.regularUnitPrice - it.unitPrice) * it.quantity * 100);
    }
  }
  out.push(sep);
  if (savedKop > 0) out.push(twoCol('Знижка за акціями', '-' + money(savedKop / 100), cols));
  out.push(twoCol('РАЗОМ', money(r.subtotal), cols));
  const card = r.paymentType.toLowerCase() === 'card';
  out.push(twoCol(card ? 'Картка' : 'Готівка', money(r.paymentAmount), cols));
  if (!card && r.change > 0) out.push(twoCol('Решта', money(r.change), cols));
  out.push(sep);
  if (fiscalized) out.push(...wrapText(`Фіскальний номер: ${r.fiscalNumber}`, cols));
  else {
    out.push(center('ЧЕК НЕ ФІСКАЛІЗОВАНО', cols));
    out.push(center('ОФЛАЙН', cols));
  }
  if (opts.reprint) out.push(center('*** ДУБЛІКАТ ***', cols));
  return out;
}

/** Full ESC/POS job for one receipt (one copy). */
export function buildReceiptJob(r: SaleReceipt, s: PrintSettings, opts: ReceiptOptions): Uint8Array {
  const cols = columnsFor(s.widthMm);
  const b = new EscPosBuilder(s.codepage).init().selectCodepage(s.codepageId);
  const fiscalized = r.fiscalStatus === 'fiscalized' && !!r.fiscalNumber;

  b.align('center').bold(true).size(2, 2);
  for (const l of wrapText(r.storeName ?? 'ShelfGuard', Math.floor(cols / 2))) b.line(l);
  b.size(1, 1).bold(false).align('left');

  const lines = receiptLines(r, cols, { reprint: opts.reprint });
  // bold the total line
  for (const l of lines) {
    if (l.startsWith('РАЗОМ')) b.bold(true).size(1, 2).line(l).size(1, 1).bold(false);
    else if (l.trim() === 'ЧЕК НЕ ФІСКАЛІЗОВАНО' || l.trim() === 'ОФЛАЙН') b.bold(true).line(l).bold(false);
    else b.line(l);
  }
  if (fiscalized) b.align('center').qr(r.fiscalNumber!).align('left');

  b.feed(1).align('center').line('Дякуємо за покупку!').align('left').feed(3).cut();
  if (opts.openDrawer) b.drawerKick(0);
  return b.build();
}

export function buildTestJob(s: PrintSettings): Uint8Array {
  const cols = columnsFor(s.widthMm);
  const b = new EscPosBuilder(s.codepage).init().selectCodepage(s.codepageId);
  b.align('center').bold(true).size(2, 2).line('ТЕСТ ДРУКУ').size(1, 1).bold(false).align('left');
  b.line(`Кодування: ${s.codepage} (ESC t ${s.codepageId}), ${cols} колонок`);
  b.line('іїєґ ІЇЄҐ');
  b.line('абвгґдеєжзиіїйклмнопрстуфхцчшщьюя');
  b.line('АБВГҐДЕЄЖЗИІЇЙКЛМНОПРСТУФХЦЧШЩЬЮЯ');
  b.line('0123456789'.repeat(Math.ceil(cols / 10)).slice(0, cols));
  b.bold(true).line('Жирний').bold(false).size(2, 1).line('Подвійна').size(1, 1);
  b.align('center').barcode128('123456789012').qr('SHELFGUARD-TEST').align('left');
  b.feed(3).cut();
  return b.build();
}

export const drawerJob = (s: PrintSettings): Uint8Array =>
  new EscPosBuilder(s.codepage).init().drawerKick(0).build();
