import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import type { PrintResult, PrintSettings, SaleReceipt } from '@shared/types';
import { DEFAULT_CODEPAGE_ID } from './encoder';
import { buildReceiptJob, buildTestJob, drawerJob } from './layout';
import { sendBytes } from './transports';

export const DEFAULT_PRINT_SETTINGS: PrintSettings = {
  autoPrint: false,
  transport: 'windows',
  printerName: '',
  tcpHost: '',
  tcpPort: 9100,
  serialPort: 'COM1',
  serialBaud: 9600,
  filePath: '',
  codepage: 'cp866',
  codepageId: DEFAULT_CODEPAGE_ID.cp866,
  widthMm: 80,
  copies: 1,
  openDrawerOnCash: false,
};

export function normalizeSettings(raw: Partial<PrintSettings> | null | undefined): PrintSettings {
  const s = { ...DEFAULT_PRINT_SETTINGS, ...(raw ?? {}) };
  s.copies = Math.min(Math.max(Math.round(Number(s.copies) || 1), 1), 5);
  s.widthMm = s.widthMm === 58 ? 58 : 80;
  s.tcpPort = Math.round(Number(s.tcpPort) || 9100);
  s.serialBaud = Math.round(Number(s.serialBaud) || 9600);
  s.codepageId = Math.min(Math.max(Math.round(Number(s.codepageId) || 0), 0), 255);
  return s;
}

export class PrintSettingsStore {
  private cached: PrintSettings;
  constructor(private readonly file: string) {
    try {
      this.cached = normalizeSettings(JSON.parse(readFileSync(file, 'utf8')));
    } catch {
      this.cached = normalizeSettings(null);
    }
  }
  get(): PrintSettings {
    return this.cached;
  }
  save(next: Partial<PrintSettings>): PrintSettings {
    this.cached = normalizeSettings(next);
    mkdirSync(dirname(this.file), { recursive: true });
    writeFileSync(this.file, JSON.stringify(this.cached, null, 2), 'utf8');
    return this.cached;
  }
}

const fail = (e: unknown): PrintResult => ({ ok: false, error: e instanceof Error ? e.message : String(e) });

/** Printing never throws: every failure comes back as a result so a sale is never blocked or lost. */
export class PrintService {
  constructor(
    private readonly settings: PrintSettingsStore,
    private readonly send: typeof sendBytes = sendBytes,
  ) {}

  async printReceipt(r: SaleReceipt, opts: { reprint?: boolean; auto?: boolean } = {}): Promise<PrintResult> {
    const s = this.settings.get();
    const openDrawer = s.openDrawerOnCash && r.paymentType.toLowerCase() === 'cash' && !opts.reprint;
    try {
      if (opts.auto && !s.autoPrint) {
        // printing disabled: still honour the drawer toggle for cash sales
        if (openDrawer) await this.send(s, drawerJob(s));
        return { ok: true };
      }
      for (let i = 0; i < s.copies; i++) {
        await this.send(s, buildReceiptJob(r, s, { openDrawer: openDrawer && i === 0, reprint: opts.reprint || i > 0 }));
      }
      return { ok: true };
    } catch (e) {
      return fail(e);
    }
  }

  async testPrint(): Promise<PrintResult> {
    try {
      await this.send(this.settings.get(), buildTestJob(this.settings.get()));
      return { ok: true };
    } catch (e) {
      return fail(e);
    }
  }

  async openDrawer(): Promise<PrintResult> {
    try {
      await this.send(this.settings.get(), drawerJob(this.settings.get()));
      return { ok: true };
    } catch (e) {
      return fail(e);
    }
  }
}
