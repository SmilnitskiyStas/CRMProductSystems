# TASK-513: Pchilka POS -> ShelfGuard local dev data import

**Status:** done
**Agent:** backend-developer
**Authority:** scoped directly with user (not backlog-derived) — see orchestrator brief

## What changed

New one-off console project `backend/ShelfGuard.Tools.PchilkaImport` (added to `ShelfGuard.sln`),
referencing `ShelfGuard.Application`/`Infrastructure`/`Domain` directly so it reuses `AppDbContext`,
`ItemService.CreateAsync`, `CustomerService.CreateAsync`, `ITenantSessionOverride` — same building
blocks the real API uses, not hand-rolled SQL.

- `Source/PchilkaCliClient.cs`, `Source/PchilkaSourceReader.cs` — read-only Pchilka extraction
  (top-N products by 30-day quantity, product/group/unit/barcode catalog, orders+lines for the
  import window). SELECT-only throughout.
- `ImportOptions.cs`, `ImportResults.cs`, `ImportRunner.cs`, `Program.cs` — orchestration: enable
  tenant modules, resolve store, import categories+items, customers, FEFO stock batches, POS
  transactions+items. Each phase is its own `ITenantSessionOverride.ExecuteAsync` transaction
  (not one giant transaction) so a re-run with adjusted scope only redoes what's missing.
- `appsettings.json` — dev Postgres connection string (`shelfguard_app_dev`, matches
  `appsettings.Development.json`) + import scope parameters.

## Deviations from the brief (all evidence-based, logged per CLAUDE.md judgment-call rule)

1. **MySQL source access: `docker exec` into the Unix socket, not a TCP connection string.**
   The brief specified `Server=127.0.0.1;Port=3307;...` (MySqlConnector). That failed with an
   incomplete-handshake error. Root cause: `SHOW VARIABLES LIKE 'port'` returns `0` and
   `skip_networking=1` inside `pchilka-pos-mysql`, confirmed to persist across a full
   `docker restart` (not a boot-order fluke) — MySQL's official docker-entrypoint hard-forces
   `skip_networking` whenever `--skip-grant-tables` is passed, regardless of any other
   networking flag, as a deliberate guard against an unauthenticated server being reachable over
   the network. Did not attempt to override/weaken this (out of bounds — a security-setting
   change, not a data operation). Switched to `docker exec pchilka-pos-mysql mysql -uroot -N -B
   -e "<sql>"` via `Process.Start` with `ArgumentList` (no shell involved, no escaping needed) —
   same SELECT-only guarantee, reached over the container's own intended local-socket path.
   Dropped the `MySqlConnector` package reference (unused now).
2. **`PriceFinal`/`DiscountAmount` are per-unit, not `line_total` verbatim.** The brief said
   `PriceFinal←line_total`. Verified against source data that `line_total = unit_price*quantity -
   discount_total` (whole-line, net of discount), while `PosTransactionItem.PriceFinal` is a
   PER-UNIT price everywhere else in the codebase (`AnalyticsRepository`/
   `AudienceBuilderRepository` compute revenue as `PriceFinal * Quantity`, and
   `AudienceBuilderRepository.cs`'s own doc comment warns against exactly this mistake). Using
   raw `line_total` would have inflated every downstream revenue figure by an extra factor of
   `Quantity`. Used `PriceFinal = line_total / quantity`, `DiscountAmount = PriceRetail -
   PriceFinal` instead.
3. **Store resolution: tenant now has 4 locations, not 1.** The brief assumed a single seeded
   store (true when written); TASK-501..512's cross-store migration testing had since added a
   second real store ("Подільський") and two disposable same-timestamp zero-zone QA fixtures
   literally named "QA TASK-504". Picked the oldest non-"QA"-named location
   ("Свіжий Кут Центральний", `f52b6d99-...`) deterministically instead of an arbitrary DB order.
4. **`Locations`/`LocationZones` also need the `ITenantSessionOverride` wrap.** The brief said
   "Tenants/Locations carry no RLS" (true for `tenants`, false for `locations` — a grep for a
   literal `"ON locations"` migration string missed a programmatically-applied policy). Confirmed
   via `pg_class`: `locations` has `relrowsecurity=t, relforcerowsecurity=t`. Fixed by wrapping
   that read in the same tenant-scoped transaction as everything else.
5. **`product_stock`/`pos_transactions` need `app.role` set too, not just `app.tenant_id`.**
   Both tables carry a RESTRICTIVE `store_scope` policy (`AddLocationStoreScopeRlsPolicies`)
   requiring `app.role` to be one of `provider/provider_admin/worker/enterprise_admin`, or a
   matching `user_locations` row — this console app has no logged-in user. Each phase now also
   issues `SET LOCAL app.role = 'enterprise_admin'` (same tenant DbSeeder's own enterprise_admin
   user carries) inside the transaction, scoped identically to `ITenantSessionOverride`'s own
   `app.tenant_id` (reverts on commit/rollback).
6. **Zone-type taxonomy mismatch.** `LocationService`'s documented valid zone types are `shelf,
   fridge, freezer, display, production, warehouse`, but the live TASK-501..512 store data
   actually uses `refrigerated`/`fresh` too. Broadened the cold-chain zone match set accordingly.
7. **Customer/transaction linkage ratio.** Real `client_code` fill rate for this shop is ~38-44%
   (not "the bulk"), so ~44% of imported transactions carry a real `CustomerId` — did not
   fabricate customer links for anonymous real orders (would contradict "no synthetic fake
   data"). Still 2016 customer-linked transactions across 1258 distinct customers, plenty for
   RFM/marketing-analytics testing.

## Scope actually imported

Shop 33 ("Магазин37"), top 200 products by 30-day quantity (2026-07-12..08-11), receipts from
2026-08-08..08-11 (4 days — 7 days would have been ~8500 orders, too much for a dev tenant per
brief's own "few hundred to low thousands" guidance).

## Result (verified live against local dev Postgres, tenant `8abfbbb5-...` / `svizhy-kut`)

```
Items:         +200 created, 0 reused (200 selected)
Categories:    +86
Customers:     +1258 created, 0 reused (2016 of the 4598 transactions carry a real CustomerId)
Stock batches: +625 (near 200 / mid 200 / far 200 / expired 25)
Transactions:  +4598 (skipped existing 0, skipped empty 0)
Line items:    +10112
Status: OK
```

Tenant modules: `loyalty` was missing, added (rest were already enabled from prior work).

## Verification

- `dotnet build` on the full solution — clean, 0 warnings, 0 errors.
- Ran the tool live against local dev Postgres (`localhost:5435`) + the Pchilka MySQL container —
  completed successfully (numbers above).
- **Re-run idempotency verified**: ran the tool a second time immediately after — `items +0
  (reused 200)`, `customers +0 (reused 1258)`, `batches +0`, `transactions +0 (skipped existing
  4598)`. No duplicates.
- Spot-checked via `psql`: real Cyrillic product names/prices/units, `PerishabilityClass`
  correctly inferred per group (`Морозиво` → `chilled`), stock `Status` distribution exactly
  matches the deliberate mix (`critical 200 / warning 200 / safe 200 / expired 25`), receipt
  numbers (`PCH-33-371-...`), customer tags (`pchilka:<client_code>`), cash/card split visible.
- Row-count cross-check: post-import per-tenant totals minus pre-existing baseline (15
  items/customers, 25 stock, 141 pos_transactions from prior QA runs) match the tool's own
  reported deltas exactly.

## Not done / accepted gaps

- `PricePurchase` is a cosmetic placeholder (75% of observed average selling price) — the Pchilka
  export has no cost/price-list table in scope.
- `ProductStockId` on sale lines links to each item's nearest-still-sellable batch for display
  only; historical sales do **not** decrement current stock quantities (these are backfilled past
  receipts, not live sales — decrementing today's freshly-seeded FEFO batches by yesterday's
  Pchilka sales would defeat the point of the batches).
