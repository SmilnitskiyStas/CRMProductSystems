# TASK-729 - Cashier phase 4: offline queue (SQLite) + ESC/POS printing

Status: review. Not committed. Backend untouched.

## Offline
- better-sqlite3 **pinned 11.10.0** (13.x has no Electron-33 prebuilt; node-gyp source build fails here - no MSVC). postinstall `electron-builder install-app-deps` fetches the Electron prebuilt; `asarUnpack` for the module; installer builds.
- `electron/sqlite.ts` (only native-module user): `catalog_items`, `outbox_sales`, `kv`. Catalog = in-memory index + SQLite persistence (`CatalogPersistence` seam), cursor with 2-min overlap, survives restarts -> offline lookup.
- `electron/outbox.ts` (pure, tested): sale always written to outbox first (local receipt `L-<regNo>-<seq>`), `OutboxService` batches <=100 to `POST /api/pos/sales/batch`, backoff 5s..5min, created/duplicate -> synced, 409/423/other 4xx -> rejected, 5xx/network -> retry, 401 -> queue kept + PIN re-login + resume. Triggers: 15 s tick, connectivity regained, after login, manual. Closing a shift syncs first and is blocked while pending/sending. Rejected: retry / dismiss (confirm; row kept as `dismissed`).
- Operator token persisted via safeStorage with expiry -> offline restart returns to checkout; open shift cached in kv. Open/close shift need network.
- UI: status bar queue count + online/offline, "Черга" screen (attention/history, retry, dismiss, reprint), red banner for rejected, offline banner at checkout (sales no longer blocked offline).

## Printing
- `electron/print/`: own ESC/POS builder, encoders CP866 / CP1125 / CP1251 / UTF-8 (CP866 has no і/ґ -> transliterated; CP1125/1251 full Ukrainian), 58/80 mm (32/48 cols), bold/align/size, CODE128, QR (fiscal number), cut, drawer pulse.
- Transports: Windows printer by name via RAW spooler (winspool through PowerShell Add-Type, no native module), TCP 9100, serial COM (PowerShell SerialPort), `file` virtual transport. Settings screen "Налаштування друку" (stored in userData/print-settings.json): auto-print, copies, drawer-on-cash, test print, drawer test. Print failure never blocks the sale: result event + warning with "Повторити друк"; reprint from queue/history.

## Verification
tsc (node+web), eslint, vitest 54 tests (outbox state machine, backoff, batch mapping, cursor/overlap, close-blocked, ESC/POS bytes, layout 32/48), `npm run build`, `npm run dist` -> `cashier/release/ShelfGuard Cashier Setup 0.1.0.exe`.
Packaged smoke vs mock API on :5078 (CDP): SQLite loads; offline sale -> L-3-1 pending, shift close blocked, test print + receipt written to file transport (starts 1b40 1b74); graceful restart offline -> operator + shift + catalog restored; server back -> queue synced, server receipt number stored, close shift OK.

## Not verified / risks
Real backend batch endpoint, real printer (RAW spooler/TCP/COM never exercised on hardware), printer codepage table numbers (ESC t n) vary by model. safeStorage key (`Local State`) is flushed by Chromium late: a hard kill/power loss on the very first run can lose it -> device token undecryptable -> re-activation needed (queue in SQLite survives). Sync attributes sales to the cashier whose token sends the batch (server uses token cashier). Prices on offline receipts come from the local catalog; server prices win on sync.

## Follow-up: per-sale cashier attribution
Outbox payload (JSON column, so no SQLite migration) now carries `cashierId` (operator at sale time) and `occurredAt` (receipt time) and both go in the batch items; legacy rows lack them and are sent unchanged (server uses token cashier). Queue entries show the cashier name; server rejections (4xx, incl. cashier-not-allowed) become `rejected` with the reason. Flushing by a different cashier is not blocked. Tests in `electron/salesGateway.test.ts`. Not run against the real backend yet.
