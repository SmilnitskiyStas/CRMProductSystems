import { app } from 'electron';
import { join } from 'node:path';
import type {
  AppState,
  AppPrefs,
  CameraPrefs,
  ConnectionState,
  CatalogItem,
  CatalogStatus,
  OperatorInfo,
  PrintResult,
  PromotionsState,
  LoyaltyMember,
  PrintSettings,
  QueueEntry,
  QueueSummary,
  RegisterInfo,
  Result,
  SaleGatewayResult,
  SaleSubmitRequest,
  ShiftInfo,
} from '@shared/types';
import { MemoryCatalogStore, type CatalogStore, type SyncCatalogPageWire } from './catalogStore';
import { ConnectivityMonitor } from './connectivity';
import { httpRequest, normalizeServerUrl, setNetworkErrorListener, toApiError, toResult } from './http';
import {
  closeShiftBlockReason,
  OutboxService,
  type BatchItemWire,
  type BatchResponse,
  type OutboxPayload,
} from './outbox';
import { AppPrefsStore } from './appPrefs';
import { CameraPrefsStore } from './cameraPrefs';
import { PrintService, PrintSettingsStore } from './print/printService';
import { toLoyaltyResult, type LoyaltyResolveWire } from './loyalty';
import { PromotionsStore, type SyncPromotionsWire } from './promotions';
import { mergeServerSale, OutboxSalesGateway, type SalesGateway } from './salesGateway';
import { SecureConfigStore } from './secureStore';
import { openDatabase, SqliteCatalogPersistence, SqliteKv, SqliteOutboxRepo, SqlitePromotionsPersistence, type Db } from './sqlite';

interface ActivateWire {
  deviceToken: string;
  registerId: string;
  number: number;
  name: string;
  type: string;
  storeId: string;
  storeName: string | null;
}
interface SessionWire {
  accessToken: string;
  expiresAt: string;
  sessionId: string;
  registerId: string;
  storeId: string;
  cashierId: string;
  cashierName: string;
}
interface ShiftWire {
  shiftId: string;
  storeId: string;
  status: string;
  openedAt: string;
  closedAt: string | null;
  totalSales: number;
  shiftNumber: number | null;
  openingCash: number | null;
  closingCash: number | null;
  expectedCashAmount: number | null;
  cashDiscrepancy: number | null;
}

const mapShift = (s: ShiftWire): ShiftInfo => ({
  shiftId: s.shiftId,
  storeId: s.storeId,
  status: s.status,
  openedAt: s.openedAt,
  closedAt: s.closedAt,
  totalSales: s.totalSales,
  shiftNumber: s.shiftNumber,
  openingCash: s.openingCash,
  closingCash: s.closingCash,
  expectedCashAmount: s.expectedCashAmount,
  cashDiscrepancy: s.cashDiscrepancy,
});

const CATALOG_PAGE_LIMIT = 500;
const CATALOG_REFRESH_MS = 5 * 60 * 1000;
const PROMOTIONS_REFRESH_MS = 60 * 1000;
const OUTBOX_TICK_MS = 15_000;
const SALE_SYNC_WAIT_MS = 5_000;

export interface ServiceEvents {
  onlineChange(state: ConnectionState): void;
  sessionExpired(): void;
  catalogChange(status: CatalogStatus): void;
  queueChange(summary: QueueSummary): void;
  printResult(result: PrintResult): void;
  promotionsChange(state: PromotionsState): void;
}

/**
 * Main-process application service: owns config, tokens, API calls, the local SQLite database
 * (catalog + sales outbox) and printing. The renderer only sees typed results via the preload
 * bridge - never a token.
 */
export class CashierService {
  private readonly config = new SecureConfigStore();
  /** Operator JWT: memory + safeStorage-encrypted on disk until it expires (offline restart resumes). */
  private operator: { info: OperatorInfo; token: string } | null = null;
  private monitor!: ConnectivityMonitor;
  private outboxTimer: NodeJS.Timeout | null = null;
  private catalogTimer: NodeJS.Timeout | null = null;
  private promoTimer: NodeJS.Timeout | null = null;
  private promotions!: PromotionsStore;
  private db!: Db;
  private kv!: SqliteKv;
  private catalog!: CatalogStore;
  private outbox!: OutboxService;
  private sales!: SalesGateway;
  private print!: PrintService;
  private printSettings!: PrintSettingsStore;
  private cameraPrefs!: CameraPrefsStore;
  private appPrefs!: AppPrefsStore;

  constructor(private readonly events: ServiceEvents) {}

  async init(): Promise<void> {
    await this.config.load();

    this.db = openDatabase(join(app.getPath('userData'), 'cashier.db'));
    this.kv = new SqliteKv(this.db);
    this.catalog = new MemoryCatalogStore(
      new SqliteCatalogPersistence(this.db, this.kv),
      (cursor) => this.fetchCatalogPage(cursor),
      (s) => this.events.catalogChange(s),
    );
    await this.catalog.load(); // barcode lookup works offline straight after start

    this.outbox = new OutboxService(new SqliteOutboxRepo(this.db, this.kv), {
      send: (p) => this.sendBatch(p),
      now: Date.now,
      mergeServerSale,
      onChange: (s) => this.events.queueChange(s),
    });
    this.outbox.recoverStuck();

    // published promotion rules: cached in SQLite, so they also apply offline
    this.promotions = new PromotionsStore(
      new SqlitePromotionsPersistence(this.kv),
      (etag) => this.fetchPromotions(etag),
      (s) => this.events.promotionsChange(s),
    );
    this.promotions.load();

    this.sales = new OutboxSalesGateway(
      this.outbox,
      this.catalog,
      () => {
        const reg = this.config.snapshot.register;
        if (!reg || !this.operator) return null;
        return { storeId: reg.storeId, cashierId: this.operator.info.cashierId, registerNumber: reg.number, registerName: reg.name, storeName: reg.storeName, cashierName: this.operator.info.cashierName };
      },
      Date.now,
      () => this.promotions.state().rules,
      () => this.promotions.state().storeTimeZone,
    );

    this.printSettings = new PrintSettingsStore(join(app.getPath('userData'), 'print-settings.json'));
    this.print = new PrintService(this.printSettings);
    this.cameraPrefs = new CameraPrefsStore(join(app.getPath('userData'), 'camera-prefs.json'));
    this.appPrefs = new AppPrefsStore(join(app.getPath('userData'), 'app-prefs.json'));

    // Resume a still-valid operator session after restart (works offline).
    const saved = this.config.snapshot.operator;
    if (saved && new Date(saved.info.expiresAt).getTime() > Date.now()) {
      this.operator = saved;
      this.startBackgroundWork();
    }

    this.monitor = new ConnectivityMonitor(
      async () => {
        // ANY HTTP response (even 401/404) means the server is reachable
        const res = await httpRequest<unknown>(this.config.snapshot.serverUrl, '/health/live', { timeoutMs: 3_000, probe: true });
        return !res.networkError;
      },
      {
        onState: (s) => this.events.onlineChange(s),
        onReconnected: () => this.onReconnected(),
      },
    );
    setNetworkErrorListener(() => this.monitor.trigger());
    this.monitor.start();
    this.outboxTimer = setInterval(() => void this.outbox.syncNow(false), OUTBOX_TICK_MS);
  }

  dispose(): void {
    this.monitor?.stop();
    setNetworkErrorListener(null);
    if (this.outboxTimer) clearInterval(this.outboxTimer);
    if (this.catalogTimer) clearInterval(this.catalogTimer);
    if (this.promoTimer) clearInterval(this.promoTimer);
    try {
      this.db?.close();
    } catch {
      /* already closed */
    }
  }

  // ── state ────────────────────────────────────────────────────────────────

  getState(): AppState {
    const c = this.config.snapshot;
    return {
      version: app.getVersion(),
      serverUrl: c.serverUrl,
      register: c.register,
      operator: this.currentOperator()?.info ?? null,
      online: this.monitor.state.online,
      connection: this.monitor.state,
      queue: this.outbox.summary(),
    };
  }

  /** Operator whose token has not expired locally (expired -> treated as signed out). */
  private currentOperator() {
    if (this.operator && new Date(this.operator.info.expiresAt).getTime() <= Date.now()) {
      this.dropOperator(true);
    }
    return this.operator;
  }

  /** offline -> online: flush the outbox, delta-sync the catalog and refresh the cached shift. */
  private onReconnected(): void {
    void this.outbox.syncNow(true);
    if (this.operator) {
      void this.promotions.sync();
      void this.catalog.sync(); // a full download happens automatically when the local catalog is empty
      void this.getCurrentShift(); // replaces the cached copy; a restored session is validated here (401 -> PIN screen)
    }
  }

  probeNow = (): Promise<ConnectionState> => this.monitor.probeNow();
  retryConnection = (): Promise<ConnectionState> => this.monitor.probeNow();

  /** One-off reachability check of an arbitrary URL (activation screen, before anything is saved). */
  async checkServer(urlRaw: string): Promise<{ reachable: boolean; message: string | null }> {
    const url = normalizeServerUrl(urlRaw);
    if (!url) return { reachable: false, message: 'Некоректна адреса сервера (потрібно http:// або https://).' };
    const res = await httpRequest<unknown>(url, '/health/live', { timeoutMs: 3_000, probe: true });
    return res.networkError
      ? { reachable: false, message: 'Сервер недоступний (немає відповіді). Перевірте адресу та що API запущено.' }
      : { reachable: true, message: null };
  }

  private startBackgroundWork(): void {
    void this.catalog.sync();
    if (this.catalogTimer) clearInterval(this.catalogTimer);
    this.catalogTimer = setInterval(() => void this.catalog.sync(), CATALOG_REFRESH_MS);
    void this.promotions.sync();
    if (this.promoTimer) clearInterval(this.promoTimer);
    this.promoTimer = setInterval(() => void this.promotions.sync(), PROMOTIONS_REFRESH_MS); // cheap: etag / notModified
  }

  // ── activation ───────────────────────────────────────────────────────────

  async activate(serverUrlRaw: string, activationCode: string): Promise<Result<RegisterInfo>> {
    const serverUrl = normalizeServerUrl(serverUrlRaw);
    if (!serverUrl) {
      return { ok: false, error: { kind: 'validation', message: 'Некоректна адреса сервера (потрібно http:// або https://).' } };
    }
    const code = activationCode.trim();
    if (!code) {
      return { ok: false, error: { kind: 'validation', message: 'Введіть код активації.' } };
    }
    const res = await httpRequest<ActivateWire>(serverUrl, '/api/pos/registers/activate', {
      method: 'POST',
      body: { activationCode: code, appVersion: app.getVersion() },
    });
    const mapped = toResult(res, (w) => w);
    if (!mapped.ok) return mapped;

    const w = mapped.data;
    const register: RegisterInfo = {
      registerId: w.registerId,
      number: w.number,
      name: w.name,
      type: w.type,
      storeId: w.storeId,
      storeName: w.storeName,
    };
    try {
      await this.config.setActivation(serverUrl, register, w.deviceToken);
    } catch (e) {
      return { ok: false, error: { kind: 'server', message: (e as Error).message } };
    }
    this.operator = null;
    this.kv.set('shift.current', null);
    await this.catalog.clear();
    this.promotions.clear();
    void this.monitor.probeNow();
    return { ok: true, data: register };
  }

  // ── operator session ─────────────────────────────────────────────────────

  async login(pin: string): Promise<Result<OperatorInfo>> {
    const { serverUrl, deviceToken, register } = this.config.snapshot;
    if (!deviceToken || !register) {
      return { ok: false, error: { kind: 'validation', message: 'Касу не активовано.' } };
    }
    const res = await httpRequest<SessionWire>(serverUrl, '/api/pos/registers/session', {
      method: 'POST',
      headers: { 'X-Register-Token': deviceToken },
      body: { pin, appVersion: app.getVersion() },
    });
    const mapped = toResult(res, (w) => w);
    if (!mapped.ok) {
      if (mapped.error.kind === 'network') {
        mapped.error.message = 'Немає зв’язку з сервером: вхід за PIN-кодом потребує мережі.';
      }
      return mapped;
    }

    const w = mapped.data;
    const info: OperatorInfo = {
      cashierId: w.cashierId,
      cashierName: w.cashierName,
      sessionId: w.sessionId,
      expiresAt: w.expiresAt,
    };
    this.operator = { info, token: w.accessToken };
    await this.config.setOperator(this.operator);
    this.outbox.clearNeedsLogin();
    this.startBackgroundWork();
    void this.outbox.syncNow(true); // resume the queue after a PIN re-login
    return { ok: true, data: info };
  }

  async logout(reason: 'logout' | 'idle'): Promise<void> {
    const op = this.operator;
    this.dropOperator(false);
    if (!op) return;
    // Best effort: the server closes the register session; failure must not block the UI.
    await httpRequest(this.config.snapshot.serverUrl, '/api/pos/registers/session/end', {
      method: 'POST',
      headers: { Authorization: `Bearer ${op.token}` },
      body: { reason },
      timeoutMs: 5_000,
    });
  }

  private dropOperator(notify: boolean): void {
    this.operator = null;
    void this.config.setOperator(null);
    if (this.catalogTimer) {
      clearInterval(this.catalogTimer);
      this.catalogTimer = null;
    }
    if (this.promoTimer) {
      clearInterval(this.promoTimer);
      this.promoTimer = null;
    }
    if (notify) this.events.sessionExpired();
  }

  /** Authenticated call as the operator; a 401 ends the local session (-> PIN screen). */
  private async operatorCall<T>(path: string, method: 'GET' | 'POST', body?: unknown) {
    const op = this.currentOperator();
    if (!op) {
      return { status: 401, body: null as T | null, networkError: false };
    }
    const res = await httpRequest<T>(this.config.snapshot.serverUrl, path, {
      method,
      body,
      headers: { Authorization: `Bearer ${op.token}` },
    });
    if (res.status === 401) this.dropOperator(true);
    return res;
  }

  // ── shifts (open/close need the network; a shift opened earlier is cached for offline sales) ──

  async getCurrentShift(): Promise<Result<ShiftInfo | null>> {
    const res = await this.operatorCall<ShiftWire>('/api/pos/shifts/current', 'GET');
    if (res.networkError) {
      const cached = this.readCachedShift();
      if (cached) return { ok: true, data: cached };
      return toResult(res, mapShift);
    }
    if (res.status === 404) {
      this.kv.set('shift.current', null);
      return { ok: true, data: null };
    }
    const r = toResult(res, mapShift);
    if (r.ok) this.kv.set('shift.current', r.data.status === 'Closed' ? null : JSON.stringify(r.data));
    return r;
  }

  private readCachedShift(): ShiftInfo | null {
    try {
      const raw = this.kv.get('shift.current');
      const s = raw ? (JSON.parse(raw) as ShiftInfo) : null;
      // only for the same register/store and still open
      return s && s.status !== 'Closed' && s.storeId === this.config.snapshot.register?.storeId ? s : null;
    } catch {
      return null;
    }
  }

  async openShift(openingCash: number): Promise<Result<ShiftInfo>> {
    const storeId = this.config.snapshot.register?.storeId;
    const res = await this.operatorCall<ShiftWire>('/api/pos/shifts/open', 'POST', { storeId, openingCash });
    const r = toResult(res, mapShift);
    if (r.ok) {
      this.kv.set('shift.current', JSON.stringify(r.data));
      void this.promotions.sync(); // rules may have changed since login
    }
    if (!r.ok && r.error.kind === 'network') r.error.message = 'Відкриття зміни потребує зв’язку з сервером.';
    return r;
  }

  async closeShift(actualClosingCash: number): Promise<Result<ShiftInfo>> {
    await this.outbox.syncNow(true); // try to flush first
    const block = closeShiftBlockReason(this.outbox.summary().pending);
    if (block) return { ok: false, error: { kind: 'conflict', message: block } };

    const res = await this.operatorCall<ShiftWire>('/api/pos/shifts/close', 'POST', { actualClosingCash });
    const r = toResult(res, mapShift);
    if (r.ok) this.kv.set('shift.current', null);
    if (!r.ok && r.error.kind === 'network') r.error.message = 'Закриття зміни потребує зв’язку з сервером.';
    return r;
  }

  // ── catalog ──────────────────────────────────────────────────────────────

  private async fetchCatalogPage(cursor: { since: string | null; afterId: string | null }): Promise<SyncCatalogPageWire> {
    const q = new URLSearchParams({ limit: String(CATALOG_PAGE_LIMIT) });
    if (cursor.since) q.set('since', cursor.since);
    if (cursor.afterId) q.set('afterId', cursor.afterId);
    const res = await this.operatorCall<SyncCatalogPageWire>(`/api/pos/sync/catalog?${q}`, 'GET');
    if (res.networkError || res.status !== 200 || !res.body) {
      throw new Error(res.networkError ? 'Немає зв’язку з сервером' : toApiError(res).message);
    }
    return res.body;
  }

  catalogStatus = (): CatalogStatus => this.catalog.status();
  syncCatalog = (): Promise<CatalogStatus> => this.catalog.sync();
  lookupBarcode = (barcode: string): CatalogItem | null => this.catalog.findByBarcode(barcode);
  searchCatalog = (query: string): CatalogItem[] => this.catalog.search(query);

  // ── sales / outbox ───────────────────────────────────────────────────────

  /**
   * Writes the sale to the outbox first (provisional local receipt). When the server is reachable it
   * is sent right away and, if it answers in time, the SERVER's lines/totals replace the local ones
   * (re-pricing notice is derived from `localTotal`); otherwise the provisional receipt stands.
   */
  async submitSale(request: SaleSubmitRequest): Promise<SaleGatewayResult> {
    const res = await this.sales.submit(request);
    if (!res.ok) return res;
    let receipt = res.sale;
    const syncP = this.outbox.syncNow(true);
    if (this.monitor.state.online) {
      await Promise.race([syncP, new Promise((r) => setTimeout(r, SALE_SYNC_WAIT_MS))]);
      const row = this.outbox.getRow(request.clientTransactionId);
      if (row?.status === 'synced') receipt = row.receipt;
    }
    void this.print.printReceipt(receipt, { auto: true }).then((r) => {
      this.events.printResult({ ...r, clientTransactionId: request.clientTransactionId });
    });
    return { ok: true, sale: receipt };
  }

  private async fetchPromotions(etag: string | null): Promise<SyncPromotionsWire> {
    const q = etag ? `?etag=${encodeURIComponent(etag)}` : '';
    const res = await this.operatorCall<SyncPromotionsWire>(`/api/pos/sync/promotions${q}`, 'GET');
    if (res.networkError || res.status !== 200 || !res.body) {
      throw new Error(res.networkError ? 'Немає зв’язку з сервером' : toApiError(res).message);
    }
    return res.body;
  }

  /** Online-only: rotating TOTP QR/barcode (replay-protected on the server). Codes are never logged. */
  async resolveLoyaltyCode(code: string): Promise<Result<LoyaltyMember>> {
    const res = await this.operatorCall<LoyaltyResolveWire>('/api/loyalty/resolve-code', 'POST', { code: code.trim() });
    return toLoyaltyResult(res, 'code');
  }

  async resolveLoyaltyIdentifier(type: 'phone' | 'account' | 'card', value: string): Promise<Result<LoyaltyMember>> {
    const res = await this.operatorCall<LoyaltyResolveWire>('/api/loyalty/resolve-identifier', 'POST', {
      identifierType: type,
      // the backend accepts digits only: strip +, spaces, dashes, brackets typed by the cashier
      value: value.replace(/\D/g, ''),
    });
    return toLoyaltyResult(res, 'identifier');
  }

  getPromotions = (): PromotionsState => this.promotions.state();
  syncPromotions = (): Promise<PromotionsState> => this.promotions.sync();

  /** POST /api/pos/sales/batch with the operator token; maps transport outcomes for the outbox. */
  private async sendBatch(payloads: OutboxPayload[]): Promise<BatchResponse> {
    const op = this.currentOperator();
    if (!op) return { kind: 'unauthorized' };
    const res = await httpRequest<{ results?: BatchItemWire[]; error?: string }>(
      this.config.snapshot.serverUrl,
      '/api/pos/sales/batch',
      {
        method: 'POST',
        body: { sales: payloads },
        headers: { Authorization: `Bearer ${op.token}` },
        timeoutMs: 30_000,
      },
    );
    if (res.networkError) return { kind: 'network' };
    if (res.status === 401) {
      this.dropOperator(true); // PIN screen; the queue stays intact and resumes after login
      return { kind: 'unauthorized' };
    }
    if (res.status === 200 && res.body?.results) return { kind: 'ok', results: res.body.results };
    return { kind: 'server', status: res.status, message: res.body?.error ?? toApiError(res).message };
  }

  queueList = (): QueueEntry[] => this.outbox.list();
  queueSummary = (): QueueSummary => this.outbox.summary();

  async queueSyncNow(): Promise<QueueSummary> {
    await this.outbox.syncNow(true);
    return this.outbox.summary();
  }

  async queueRetry(id: string): Promise<QueueSummary> {
    this.outbox.retryRejected(id);
    await this.outbox.syncNow(true);
    return this.outbox.summary();
  }

  queueDismiss(id: string): QueueSummary {
    this.outbox.dismissRejected(id);
    return this.outbox.summary();
  }

  // ── printing ─────────────────────────────────────────────────────────────

  getCameraPrefs = (): CameraPrefs => this.cameraPrefs.get();
  saveCameraPrefs = (p: CameraPrefs): CameraPrefs => this.cameraPrefs.save(p);
  getAppPrefs = (): AppPrefs => this.appPrefs.get();
  saveAppPrefs = (p: AppPrefs): AppPrefs => this.appPrefs.save(p);

  getPrintSettings = (): PrintSettings => this.printSettings.get();
  savePrintSettings = (s: PrintSettings): PrintSettings => this.printSettings.save(s);
  testPrint = (): Promise<PrintResult> => this.print.testPrint();
  openDrawer = (): Promise<PrintResult> => this.print.openDrawer();

  async printReceipt(id: string): Promise<PrintResult> {
    const row = this.outbox.getRow(id);
    if (!row) return { ok: false, error: 'Чек не знайдено.', clientTransactionId: id };
    const r = await this.print.printReceipt(row.receipt, { reprint: true });
    return { ...r, clientTransactionId: id };
  }
}
