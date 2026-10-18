import { app } from 'electron';
import { join } from 'node:path';
import type {
  AppState,
  CatalogItem,
  CatalogStatus,
  OperatorInfo,
  RegisterInfo,
  Result,
  SaleGatewayResult,
  SaleSubmitRequest,
  ShiftInfo,
} from '@shared/types';
import { MemoryCatalogStore, type CatalogStore, type SyncCatalogPageWire } from './catalogStore';
import { httpRequest, normalizeServerUrl, toApiError, toResult } from './http';
import { HttpSalesGateway, type SalesGateway } from './salesGateway';
import { SecureConfigStore } from './secureStore';

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
const ONLINE_POLL_MS = 15_000;

export interface ServiceEvents {
  onlineChange(online: boolean): void;
  sessionExpired(): void;
  catalogChange(status: CatalogStatus): void;
}

/**
 * Main-process application service: owns config, tokens, API calls and the catalog. The renderer
 * only ever sees the typed results exposed through the preload bridge - never a token.
 */
export class CashierService {
  private readonly config = new SecureConfigStore();
  /** Operator JWT lives in memory only: a restart returns to the PIN screen. */
  private operator: { info: OperatorInfo; token: string } | null = null;
  private online = false;
  private pollTimer: NodeJS.Timeout | null = null;
  private catalogTimer: NodeJS.Timeout | null = null;
  private readonly catalog: CatalogStore;
  private readonly sales: SalesGateway;

  constructor(private readonly events: ServiceEvents) {
    this.catalog = new MemoryCatalogStore(
      join(app.getPath('userData'), 'catalog.json'),
      (cursor) => this.fetchCatalogPage(cursor),
      (s) => this.events.catalogChange(s),
    );
    this.sales = new HttpSalesGateway(
      () => ({ baseUrl: this.config.snapshot.serverUrl, operatorToken: this.operator?.token ?? null }),
      () => this.dropOperator(true),
    );
  }

  async init(): Promise<void> {
    await this.config.load();
    await this.catalog.load();
    void this.pollOnline();
    this.pollTimer = setInterval(() => void this.pollOnline(), ONLINE_POLL_MS);
  }

  dispose(): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
    if (this.catalogTimer) clearInterval(this.catalogTimer);
  }

  // ── state ────────────────────────────────────────────────────────────────

  getState(): AppState {
    const c = this.config.snapshot;
    return {
      version: app.getVersion(),
      serverUrl: c.serverUrl,
      register: c.register,
      operator: this.operator?.info ?? null,
      online: this.online,
    };
  }

  private async pollOnline(): Promise<void> {
    const res = await httpRequest<unknown>(this.config.snapshot.serverUrl, '/health/live', { timeoutMs: 5_000 });
    const now = !res.networkError && res.status >= 200 && res.status < 300;
    if (now !== this.online) {
      this.online = now;
      this.events.onlineChange(now);
      if (now && this.operator) void this.catalog.sync();
    }
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
    await this.catalog.clear();
    void this.pollOnline();
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
    // 401 with a device-level message means the token was revoked server-side.
    const mapped = toResult(res, (w) => w);
    if (!mapped.ok) return mapped;

    const w = mapped.data;
    const info: OperatorInfo = {
      cashierId: w.cashierId,
      cashierName: w.cashierName,
      sessionId: w.sessionId,
      expiresAt: w.expiresAt,
    };
    this.operator = { info, token: w.accessToken };
    this.startCatalogRefresh();
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
    if (this.catalogTimer) {
      clearInterval(this.catalogTimer);
      this.catalogTimer = null;
    }
    if (notify) this.events.sessionExpired();
  }

  private startCatalogRefresh(): void {
    void this.catalog.sync();
    if (this.catalogTimer) clearInterval(this.catalogTimer);
    this.catalogTimer = setInterval(() => void this.catalog.sync(), CATALOG_REFRESH_MS);
  }

  /** Authenticated call as the operator; a 401 ends the local session (-> PIN screen). */
  private async operatorCall<T>(path: string, method: 'GET' | 'POST', body?: unknown) {
    if (!this.operator) {
      return {
        status: 401,
        body: null as T | null,
        networkError: false,
      };
    }
    const res = await httpRequest<T>(this.config.snapshot.serverUrl, path, {
      method,
      body,
      headers: { Authorization: `Bearer ${this.operator.token}` },
    });
    if (res.status === 401) this.dropOperator(true);
    return res;
  }

  // ── shifts ───────────────────────────────────────────────────────────────

  async getCurrentShift(): Promise<Result<ShiftInfo | null>> {
    const res = await this.operatorCall<ShiftWire>('/api/pos/shifts/current', 'GET');
    if (res.status === 404) return { ok: true, data: null };
    return toResult(res, mapShift);
  }

  async openShift(openingCash: number): Promise<Result<ShiftInfo>> {
    const storeId = this.config.snapshot.register?.storeId;
    const res = await this.operatorCall<ShiftWire>('/api/pos/shifts/open', 'POST', { storeId, openingCash });
    return toResult(res, mapShift);
  }

  async closeShift(actualClosingCash: number): Promise<Result<ShiftInfo>> {
    const res = await this.operatorCall<ShiftWire>('/api/pos/shifts/close', 'POST', { actualClosingCash });
    return toResult(res, mapShift);
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

  // ── sales ────────────────────────────────────────────────────────────────

  submitSale(request: SaleSubmitRequest): Promise<SaleGatewayResult> {
    return this.sales.submit(request);
  }
}
