import { app, BrowserWindow, ipcMain, session, shell } from 'electron';
import { join } from 'node:path';
import type { AppPrefs, CameraPrefs, PrintSettings, SaleSubmitRequest } from '@shared/types';
import { CashierService } from './service';
import { configureHttpTransport } from './http';

const isDev = !app.isPackaged && !!process.env['ELECTRON_RENDERER_URL'];

let win: BrowserWindow | null = null;
let service: CashierService | null = null;

const send = (channel: string, payload?: unknown) => {
  if (win && !win.isDestroyed()) win.webContents.send(channel, payload);
};

function registerIpc(svc: CashierService): void {
  ipcMain.handle('app:getState', () => svc.getState());
  ipcMain.handle('activation:activate', (_e, url: string, code: string) => svc.activate(String(url), String(code)));
  ipcMain.handle('auth:login', (_e, pin: string) => svc.login(String(pin)));
  ipcMain.handle('auth:logout', (_e, reason: 'logout' | 'idle') => svc.logout(reason === 'idle' ? 'idle' : 'logout'));
  ipcMain.handle('shift:current', () => svc.getCurrentShift());
  ipcMain.handle('shift:open', (_e, cash: number) => svc.openShift(Number(cash)));
  ipcMain.handle('shift:close', (_e, cash: number) => svc.closeShift(Number(cash)));
  ipcMain.handle('catalog:status', () => svc.catalogStatus());
  ipcMain.handle('catalog:sync', () => svc.syncCatalog());
  ipcMain.handle('catalog:lookup', (_e, barcode: string) => svc.lookupBarcode(String(barcode)));
  ipcMain.handle('catalog:search', (_e, q: string) => svc.searchCatalog(String(q)));
  ipcMain.handle('sales:submit', (_e, req: SaleSubmitRequest) => svc.submitSale(req));
  ipcMain.handle('queue:list', () => svc.queueList());
  ipcMain.handle('queue:summary', () => svc.queueSummary());
  ipcMain.handle('queue:syncNow', () => svc.queueSyncNow());
  ipcMain.handle('queue:retry', (_e, id: string) => svc.queueRetry(String(id)));
  ipcMain.handle('queue:dismiss', (_e, id: string) => svc.queueDismiss(String(id)));
  ipcMain.handle('print:getSettings', () => svc.getPrintSettings());
  ipcMain.handle('print:saveSettings', (_e, s: PrintSettings) => svc.savePrintSettings(s));
  ipcMain.handle('print:listPrinters', async () => (win ? (await win.webContents.getPrintersAsync()).map((p) => p.name) : []));
  ipcMain.handle('print:test', () => svc.testPrint());
  ipcMain.handle('print:receipt', (_e, id: string) => svc.printReceipt(String(id)));
  ipcMain.handle('print:drawer', () => svc.openDrawer());
  ipcMain.handle('loyalty:resolveCode', (_e, code: string) => svc.resolveLoyaltyCode(String(code)));
  ipcMain.handle('loyalty:resolveIdentifier', (_e, type: string, value: string) =>
    svc.resolveLoyaltyIdentifier(type === 'account' || type === 'card' ? type : 'phone', String(value)),
  );
  ipcMain.handle('promotions:get', () => svc.getPromotions());
  ipcMain.handle('promotions:sync', () => svc.syncPromotions());
  ipcMain.handle('connection:retry', () => svc.retryConnection());
  ipcMain.handle('connection:check', (_e, url: string) => svc.checkServer(String(url)));
  ipcMain.handle('camera:getPrefs', () => svc.getCameraPrefs());
  ipcMain.handle('camera:savePrefs', (_e, p: CameraPrefs) => svc.saveCameraPrefs(p));
  ipcMain.handle('app:getPrefs', () => svc.getAppPrefs());
  ipcMain.handle('app:savePrefs', (_e, p: AppPrefs) => svc.saveAppPrefs(p));
}

/**
 * Camera access (barcode scanning with the laptop webcam): only `media` (video, no audio) and only
 * for our own window. Everything else - notifications, geolocation, clipboard, any other
 * webContents - is denied.
 */
function installPermissions(): void {
  const ses = session.defaultSession;
  const isOurs = (wc: Electron.WebContents | null) => !!win && !win.isDestroyed() && wc === win.webContents;
  ses.setPermissionRequestHandler((wc, permission, callback, details) => {
    const types = (details as { mediaTypes?: string[] }).mediaTypes ?? [];
    callback(permission === 'media' && isOurs(wc) && types.every((t) => t === 'video'));
  });
  ses.setPermissionCheckHandler((wc, permission, _origin, details) => {
    const t = (details as { mediaType?: string }).mediaType;
    return permission === 'media' && isOurs(wc) && (t === undefined || t === 'video');
  });
}

function installCsp(): void {
  // Packaged builds carry the CSP as a <meta> (file:// ignores response headers); dev needs a
  // relaxed header because the Vite client injects inline scripts and a websocket.
  if (!isDev) return;
  session.defaultSession.webRequest.onHeadersReceived((details, cb) => {
    cb({
      responseHeaders: {
        ...details.responseHeaders,
        'Content-Security-Policy': [
          "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; connect-src 'self' ws://localhost:* http://localhost:*; media-src 'self' blob:; object-src 'none'",
        ],
      },
    });
  });
}

function createWindow(): void {
  win = new BrowserWindow({
    width: 1280,
    height: 800,
    minWidth: 1024,
    minHeight: 640,
    backgroundColor: '#020617',
    title: 'ShelfGuard Cashier',
    autoHideMenuBar: true,
    show: false,
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
    },
  });

  win.once('ready-to-show', () => win?.show());
  win.on('focus', () => service?.probeNow()); // coming back to the app: re-check the server right away
  win.setMenuBarVisibility(false);

  // The renderer is a single local page: block navigation and new windows; open links externally.
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:\/\//i.test(url)) void shell.openExternal(url);
    return { action: 'deny' };
  });
  win.webContents.on('will-navigate', (e) => e.preventDefault());

  if (isDev) void win.loadURL(process.env['ELECTRON_RENDERER_URL']!);
  else void win.loadFile(join(__dirname, '../renderer/index.html'));
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (!win) return;
    if (win.isMinimized()) win.restore();
    win.focus();
  });

  void app.whenReady().then(async () => {
    configureHttpTransport({
      allowLoopbackHttp: !app.isPackaged && process.env.CASHIER_ALLOW_LOOPBACK_HTTP === 'true',
    });
    service = new CashierService({
      onlineChange: (s) => send('evt:online', s),
      sessionExpired: () => send('evt:sessionExpired'),
      catalogChange: (s) => send('evt:catalog', s),
      queueChange: (s) => send('evt:queue', s),
      promotionsChange: (s) => send('evt:promotions', s),
      printResult: (r) => send('evt:print', r),
    });
    await service.init();
    registerIpc(service);
    installCsp();
    installPermissions();
    createWindow();
  });

  app.on('window-all-closed', () => {
    service?.dispose();
    app.quit();
  });
}
