# TASK-735 - Cashier phase 3: promotion rules engine mirror, sync, cart/receipt

Status: review. Not committed. Backend untouched.

- Engine: `cashier/src/shared/promotions/engine.ts` = TS mirror of `PromotionEngine.cs` (exact BigInt fixed-point decimals: half-away promo rounding, half-even tier, 0.01 floor, best-for-customer, per_item/across_set groups, scope/audience/limits/excludeAlreadyDiscounted, same UTC assumption). `golden-vectors.json` copied from the backend; `engine.test.ts` runs EVERY vector (37, all green first run) + a drift test (byte-identical to the backend file when it exists); `npm run sync-vectors` copies it over.
- Catalog: `categoryId` on CatalogItem + SQLite `category_id` (additive ALTER); `catalog.schemaVersion` in kv, below 2 the cursor is dropped so one full resync runs (items stay usable offline meanwhile).
- Promotions sync (`electron/promotions.ts`): `GET /api/pos/sync/promotions?etag=`; rules+etag+fetchedAt persisted in SQLite kv (offline + restart safe); refresh on login/restored session, after reconnect, on shift open, every 60 s; failures keep the cache. IPC `getPromotions/syncPromotions` + `evt:promotions`.
- Cart/UI: `priceCart()` prices through the engine (storeId from the shift, `at` = now, 30 s tick); line shows struck regular price, promo segments ("2 × 42,73 + 1 × 0,01") and rule badge; footer "Знижка за акціями: −X ₴"; payment/change/receipt use the promo total. Wedge/camera flows unchanged (3rd unit flips the line instantly - covered by a cart test).
- Sale: request still barcode+quantity only. Offline: provisional receipt/outbox priced locally with promo sub-lines. Online: after enqueue the sale is sent immediately (<=5 s wait) and the SERVER receipt (lines, labels, total) is shown/printed; if totals differ `localTotal` is kept -> receipt notice "Ціну перераховано сервером" and the Черга row shows the difference (also for later batch sync). Change is recomputed from the server total.
- Print: receipt prints the product once, a row per promo sub-line, "Акція: <label>", and "Знижка за акціями" (snapshot tests 32/48 cols).

Verification: tsc (both), eslint, vitest 141, build, dist OK. Packaged smoke vs mock API: rules synced (second request sent etag -> notModified path), categoryId arrives, offline 3-pcs sale priced 85.47 with split sub-lines, rules persisted across restart offline, batch sync stored server receipt.

Not verified: real backend promotions endpoint/engine parity on live data, UI rendering by eye (cart badge/strike-through), loyalty rules (membership always null in this phase).
