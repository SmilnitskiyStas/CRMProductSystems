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
  onOnlineChange: (cb) => subscribe('evt:online', cb),
  onSessionExpired: (cb) => subscribe<void>('evt:sessionExpired', () => cb()),
  onCatalogChange: (cb) => subscribe('evt:catalog', cb),
};

contextBridge.exposeInMainWorld('cashier', api);
