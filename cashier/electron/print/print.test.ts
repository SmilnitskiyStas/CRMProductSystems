import { describe, expect, it } from 'vitest';
import type { SaleReceipt } from '@shared/types';
import { EscPosBuilder } from './builder';
import { encodeText } from './encoder';
import { buildReceiptJob, buildTestJob, columnsFor, drawerJob, receiptLines, twoCol, wrapText } from './layout';
import { DEFAULT_PRINT_SETTINGS, normalizeSettings, PrintService, PrintSettingsStore } from './printService';

const hex = (b: Uint8Array) => Buffer.from(b).toString('hex');

const receipt = (over: Partial<SaleReceipt> = {}): SaleReceipt => ({
  transactionId: 't',
  receiptNumber: 'L-2-7',
  localReceiptNo: 'L-2-7',
  local: true,
  subtotal: 72.22,
  paymentType: 'Cash',
  paymentAmount: 100,
  change: 27.78,
  fiscalStatus: 'offline',
  fiscalNumber: null,
  createdAt: '2026-10-18T10:15:00',
  storeName: 'Свіжий Кут',
  registerNumber: 2,
  registerName: 'Каса 2',
  cashierName: 'Олена',
  items: [
    { name: 'Хліб Київхліб Столичний житній полов.наріз.скибк.уп 0,475кг', barcode: '1', quantity: 2, unitPrice: 36.11, total: 72.22 },
  ],
  ...over,
});

describe('EscPosBuilder', () => {
  it('emits init, codepage, cut and drawer-kick sequences', () => {
    const b = new EscPosBuilder('cp866').init().selectCodepage(17).cut().drawerKick(0).build();
    expect(hex(b)).toBe('1b40' + '1b7411' + '1d564200' + '1b7000' + '19fa');
  });

  it('skips ESC t for utf-8 and encodes text as utf-8', () => {
    const b = new EscPosBuilder('utf8').init().selectCodepage(17).line('і').build();
    expect(hex(b)).toBe('1b40' + 'd196' + '0a');
  });

  it('align / bold / size / feed bytes', () => {
    const b = new EscPosBuilder('cp866').align('center').bold(true).size(2, 2).feed(3).build();
    expect(hex(b)).toBe('1b6101' + '1b4501' + '1d2111' + '1b6403');
  });

  it('qr code stores the payload with the right length header', () => {
    const b = hex(new EscPosBuilder('cp866').qr('AB').build());
    expect(b).toContain('1d286b050031503041' + '42'); // len = 2 + 3, "P0" then 'A','B'
    expect(b.endsWith('1d286b0300315130')).toBe(true);
  });
});

describe('encodeText', () => {
  it('encodes Cyrillic in CP866 and falls back for і/ґ', () => {
    expect(hex(encodeText('Аб', 'cp866'))).toBe('80a1');
    expect(hex(encodeText('ЄєЇї', 'cp866'))).toBe('f2f3f4f5');
    expect(encodeText('іґ', 'cp866')).toEqual(Uint8Array.from([0x69, 0xa3])); // i, г
  });
  it('CP1125 and CP1251 carry every Ukrainian letter', () => {
    expect(hex(encodeText('ҐґЄєІіЇї', 'cp1125'))).toBe('f2f3f4f5f6f7f8f9');
    expect(hex(encodeText('ҐґЄєІіЇї', 'cp1251'))).toBe('a5b4aaba b2b3afbf'.replace(' ', ''));
    expect(hex(encodeText('Аа', 'cp1251'))).toBe('c0e0');
  });
  it('replaces unknown characters with ? instead of corrupting the stream', () => {
    expect(encodeText('a€', 'cp866')).toEqual(Uint8Array.from([0x61, 0x3f]));
  });
});

describe('layout', () => {
  it('uses 32 / 48 columns for 58 / 80 mm', () => {
    expect([columnsFor(58), columnsFor(80)]).toEqual([32, 48]);
  });

  it('wraps text to the width and hard-splits long words', () => {
    for (const w of [32, 48]) {
      const lines = wrapText('Хліб Київхліб Столичний житній полов.наріз.скибк.уп 0,475кг', w);
      expect(lines.every((l) => l.length <= w)).toBe(true);
      expect(lines.join(' ').replace(/\s+/g, ' ')).toContain('Київхліб');
    }
    expect(wrapText('x'.repeat(70), 32).map((l) => l.length)).toEqual([32, 32, 6]);
  });

  it('two-column rows never exceed the width', () => {
    expect(twoCol('2 x 36,11', '72,22', 32)).toHaveLength(32);
    expect(twoCol('a'.repeat(60), '72,22', 32)).toHaveLength(32);
  });

  it.each([32, 48])('receipt lines fit %i columns and mark an offline receipt', (cols) => {
    const lines = receiptLines(receipt(), cols);
    expect(lines.every((l) => l.length <= cols)).toBe(true);
    const text = lines.join('\n');
    expect(text).toContain('ЧЕК НЕ ФІСКАЛІЗОВАНО');
    expect(text).toContain('ОФЛАЙН');
    expect(text).toContain('Чек L-2-7 (локальний)');
    expect(text).toContain('Решта');
  });

  it('shows both numbers after sync and the fiscal number when fiscalized', () => {
    const text = receiptLines(
      receipt({ receiptNumber: 'R-123', local: false, fiscalStatus: 'fiscalized', fiscalNumber: 'FN-99' }),
      32,
    ).join('\n');
    expect(text).toContain('Чек R-123 (лок. L-2-7)');
    expect(text).toContain('FN-99');
    expect(text).not.toContain('НЕ ФІСКАЛІЗОВАНО');
  });

  it('receipt job: init, codepage, cut; drawer pulse only when requested; QR only when fiscalized', () => {
    const s = { ...DEFAULT_PRINT_SETTINGS };
    const plain = buildReceiptJob(receipt(), s, { openDrawer: false });
    expect(hex(plain).startsWith('1b40' + '1b7411')).toBe(true);
    expect(hex(plain)).toContain('1d564200');
    expect(hex(plain)).not.toContain('1b700019fa');
    expect(hex(plain)).not.toContain('1d286b');
    const drawer = buildReceiptJob(receipt(), s, { openDrawer: true });
    expect(hex(drawer).endsWith('1b700019fa')).toBe(true);
    const fiscal = buildReceiptJob(receipt({ fiscalStatus: 'fiscalized', fiscalNumber: 'FN-99' }), s, { openDrawer: false });
    expect(hex(fiscal)).toContain('1d286b');
  });

  it('test page and drawer job', () => {
    expect(hex(buildTestJob(DEFAULT_PRINT_SETTINGS))).toContain('1d564200');
    expect(hex(drawerJob(DEFAULT_PRINT_SETTINGS))).toBe('1b40' + '1b7000' + '19fa');
  });
});

describe('PrintService', () => {
  const store = (over = {}) => {
    const s = new PrintSettingsStore('Z:/nonexistent/settings.json');
    // bypass disk: mutate the cached copy
    Object.assign(s.get(), normalizeSettings({ ...DEFAULT_PRINT_SETTINGS, ...over }));
    return s;
  };

  it('never throws when the transport fails', async () => {
    const svc = new PrintService(store({ autoPrint: true }), async () => {
      throw new Error('paper out');
    });
    expect(await svc.printReceipt(receipt())).toEqual({ ok: false, error: 'paper out' });
    expect((await svc.testPrint()).ok).toBe(false);
  });

  it('auto-print respects the toggle, drawer toggle and copies', async () => {
    const sent: Uint8Array[] = [];
    const send = async (_s: unknown, b: Uint8Array) => void sent.push(b);
    await new PrintService(store({ autoPrint: false }), send).printReceipt(receipt(), { auto: true });
    expect(sent).toHaveLength(0);

    await new PrintService(store({ autoPrint: false, openDrawerOnCash: true }), send).printReceipt(receipt(), { auto: true });
    expect(sent).toHaveLength(1); // drawer-only pulse
    expect(hex(sent[0])).toBe('1b40' + '1b7000' + '19fa');

    sent.length = 0;
    await new PrintService(store({ autoPrint: true, copies: 2 }), send).printReceipt(receipt(), { auto: true });
    expect(sent).toHaveLength(2);
  });

  it('normalizes settings', () => {
    expect(normalizeSettings({ copies: 99, widthMm: 70 as 58 })).toMatchObject({ copies: 5, widthMm: 80 });
  });
});
