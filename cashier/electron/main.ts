import { app, BrowserWindow, ipcMain, session, shell } from 'electron';
import { join } from 'node:path';
import type { SaleSubmitRequest } from '@shared/types';
import { CashierService } from './service';

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
            "img-src 'self' data:; connect-src 'self' ws://localhost:* http://localhost:*; object-src 'none'",
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
    service = new CashierService({
      onlineChange: (v) => send('evt:online', v),
      sessionExpired: () => send('evt:sessionExpired'),
      catalogChange: (s) => send('evt:catalog', s),
    });
    await service.init();
    registerIpc(service);
    installCsp();
    createWindow();
  });

  app.on('window-all-closed', () => {
    service?.dispose();
    app.quit();
  });
}
