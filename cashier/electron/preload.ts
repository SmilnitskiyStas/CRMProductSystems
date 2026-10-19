import { contextBridge, ipcRenderer } from 'electron';
import type { CashierApi } from '@shared/types';

const subscribe = <T>(channel: string, cb: (payload: T) => void): (() => void) => {
  const handler = (_e: unknown, payload: T) => cb(payload);
  ipcRenderer.on(channel, handler);
  return () => ipcRenderer.removeListener(channel, handler);
};

const api: CashierApi = {
  getState: () => ipcRenderer.invoke('app:getState'),
  activate: (serverUrl, code) => ipcRenderer.invoke('activation:activate', serverUrl, code),
  login: (pin) => ipcRenderer.invoke('auth:login', pin),
  logout: (reason) => ipcRenderer.invoke('auth:logout', reason),
  getCurrentShift: () => ipcRenderer.invoke('shift:current'),
  openShift: (openingCash) => ipcRenderer.invoke('shift:open', openingCash),
  closeShift: (actual) => ipcRenderer.invoke('shift:close', actual),
  catalogStatus: () => ipcRenderer.invoke('catalog:status'),
  syncCatalog: () => ipcRenderer.invoke('catalog:sync'),
  lookupBarcode: (barcode) => ipcRenderer.invoke('catalog:lookup', barcode),
  searchCatalog: (query) => ipcRenderer.invoke('catalog:search', query),
  submitSale: (request) => ipcRenderer.invoke('sales:submit', request),
  queueList: () => ipcRenderer.invoke('queue:list'),
  queueSummary: () => ipcRenderer.invoke('queue:summary'),
  queueSyncNow: () => ipcRenderer.invoke('queue:syncNow'),
  queueRetry: (id) => ipcRenderer.invoke('queue:retry', id),
  queueDismiss: (id) => ipcRenderer.invoke('queue:dismiss', id),
  getPrintSettings: () => ipcRenderer.invoke('print:getSettings'),
  savePrintSettings: (s) => ipcRenderer.invoke('print:saveSettings', s),
  listPrinters: () => ipcRenderer.invoke('print:listPrinters'),
  testPrint: () => ipcRenderer.invoke('print:test'),
  printReceipt: (id) => ipcRenderer.invoke('print:receipt', id),
  openDrawer: () => ipcRenderer.invoke('print:drawer'),
  getPromotions: () => ipcRenderer.invoke('promotions:get'),
  syncPromotions: () => ipcRenderer.invoke('promotions:sync'),
  onPromotionsChange: (cb) => subscribe('evt:promotions', cb),
  retryConnection: () => ipcRenderer.invoke('connection:retry'),
  checkServer: (url) => ipcRenderer.invoke('connection:check', url),
  getCameraPrefs: () => ipcRenderer.invoke('camera:getPrefs'),
  saveCameraPrefs: (p) => ipcRenderer.invoke('camera:savePrefs', p),
  getAppPrefs: () => ipcRenderer.invoke('app:getPrefs'),
  saveAppPrefs: (p) => ipcRenderer.invoke('app:savePrefs', p),
  onOnlineChange: (cb) => subscribe('evt:online', cb),
  onSessionExpired: (cb) => subscribe<void>('evt:sessionExpired', () => cb()),
  onCatalogChange: (cb) => subscribe('evt:catalog', cb),
  onQueueChange: (cb) => subscribe('evt:queue', cb),
  onPrintResult: (cb) => subscribe('evt:print', cb),
};

contextBridge.exposeInMainWorld('cashier', api);
