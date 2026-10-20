# TASK-728 - Cashier desktop app, slice 3a (scaffold, activation, PIN, shift, checkout)

Status: review. Not committed.

## What
New standalone package `/cashier`: Electron 33 + React 18 + TS + Vite (electron-vite) + Tailwind 3, electron-builder NSIS (per-user, choose dir, desktop + start-menu shortcut, placeholder `build/icon.ico`).

- Main process (`electron/`): `service.ts` owns config, API calls, device token (safeStorage-encrypted in `userData/config.json`, never plaintext; activation fails if OS encryption is unavailable), operator JWT (memory only - restart returns to PIN screen), online poll (`/health/live`, 15 s), catalog refresh (5 min). `catalogStore.ts` = `CatalogStore` interface + `MemoryCatalogStore` (JSON cache, delta cursor with 2-min overlap, upsert by id, drops inactive). `salesGateway.ts` = `SalesGateway` interface + `HttpSalesGateway` (Phase 4 swaps in SQLite outbox). `preload.ts` typed contextBridge API; contextIsolation on, nodeIntegration off, sandbox on, single-instance lock, navigation/new-window blocked, CSP (meta in prod build, header in dev).
- Renderer (`src/`): Activation (server URL + code, shows register no./name), PIN pad (4-6 digits, keypad + keyboard, auto-submit at 6 digits / 600 ms pause from 4, API lockout/error messages, 5-min idle logout), Shift (open with opening cash; close with actual cash -> Z-report summary), Checkout (focus-independent scanner capture, search, cart, qty +/-/typed, hotkeys F2/F5/F6/F8/F9/F12 + arrows/+/-/Del, cash dialog with change + quick amounts, card, receipt screen), status bar (register, operator, online, version).
- Sale semantics: `SaleSubmitter` (single-flight, `ClientTransactionId` generated once and reused for retries after uncertain/5xx/failed; cart change or leaving dialog resets); 409 conflict, 423 locked, 401 -> session expired (back to PIN), network/5xx -> "uncertain" with safe Retry (server is idempotent, unlike mobile which forbids retry).
- Shared pure logic in `src/shared/`: `scanBuffer`, `cart`, `money`, `saleSubmission`, `types`.
- `.gitignore`: cashier node_modules/out/dist/release.

## Verification
- `npm install`, `tsc --noEmit` (node + web), `eslint .`, `vitest run` (24 tests: scan buffer, cart/change, sale state machine, catalog store), `npm run build`: all clean.
- `npm run dist` OK -> `cashier/release/ShelfGuard Cashier Setup 0.1.0.exe` (~78 MB, unsigned).
- Smoke: launched `release/win-unpacked` with a mock API on :5077 and drove it via CDP: page loads under prod CSP, `window.cashier` exposed, `require` undefined in renderer, activate / bad PIN / login / open shift / catalog sync / barcode lookup / sale submit all work; config.json has no plaintext device token.

## Not verified
Real backend (dev API not run), real scanner/touch screen, interactive UI clicking (visual review), installing the Setup .exe, Z-report against real fiscal data. No printer/bank terminal/returns/offline queue (Phases 4+). Installer unsigned (SmartScreen warning). Placeholder icon.
