# TASK-362 — Backend: Block 11 pre-launch audit — IoT / Weather / Events / Cannibalization

**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-361

Block 11 of the pre-launch audit (`C:\Users\stass\.claude\plans\eager-pondering-tower.md`).
Scope: `Features/IoT`, `Features/Weather`, `Features/Events`, `Features/Cannibalization` +
worker jobs `weather-fetch.job.ts`, `mqtt-listener.ts` (KI-016 lead) and a full sweep of the
remaining worker jobs not yet checked in this audit series.

## KI-016 — confirmed and fixed (P0, same bug class as Blocks 7/9)

Confirmed live against the dev DB (`\d <table>`, cross-checked with
`AppDbContextModelSnapshot.cs`): `iot_devices`, `weather_data`, `temperature_readings`,
`product_stock` all have their store-scoping column renamed to `"LocationId"` (v4
Store→Location rename); `stock_events` genuinely kept `"StoreId"` (never renamed);
`weight_readings` has no store column at all.

Fixed:
- `worker/src/jobs/weather-fetch.job.ts` — `INSERT INTO weather_data` used `"StoreId"` in
  both the column list and `ON CONFLICT` clause (the earlier TASK-358 fix only corrected the
  `SELECT FROM locations` half). Every upsert had been throwing "column StoreId does not
  exist" — `weather_data` was never actually populated even after TASK-358.
- `worker/src/jobs/mqtt-listener.ts` — `"StoreId"` → `"LocationId"` in 4 places: the
  `iot_devices` device lookup (`handleMessage`), the `iot_devices` offline scan
  (`checkOfflineDevices`), the `temperature_readings` INSERT, and the `product_stock` FEFO
  write-down SELECT. Left the two `stock_events` INSERTs untouched — genuinely correct as-is.

**Found one level deeper while fixing (same investigation, not previously documented):**
`weather-fetch.job.ts` and `ai-order.job.ts` (both `db.connect()` blocks) never called
`SET app.role = 'worker'` at all; `notification.job.ts`'s `handleExpiryAlert`/`handleIotAlert`
likewise never set it. Under the Block 2 fail-closed RLS fix
(`20260714180000_FixFailOpenTenantIsolationOnReset`), every one of these queries silently
returns zero rows unless the pooled pg connection happens to have inherited `app.role` from
another job that reused the same physical connection (node-pg does not reset session state on
`release()` — SET persists on the pooled connection). This is exactly the class of bug the
`worker_bypass` policy (TASK-343) exists to prevent, but only works if the job actually sets
the role. Fixed by adding the explicit `SET app.role = 'worker'` each function was missing —
matches the established pattern in every other worker job file (`expiry-check.job.ts`,
`stock-snapshot.job.ts`, `mqtt-listener.ts`'s other functions, `notification-dispatch.job.ts`).

Practical effect before the fix: the nightly 05:00 AI-order cron (`ai-order.job.ts`) would
have found zero active locations even with TASK-358's table-name fix applied; the 06:00
weather-fetch cron would have thrown on every INSERT; expiry-warning/critical/expired and
temp_alert/iot_offline notifications (`notification.job.ts`) would have silently found zero
matching users and sent nothing, depending on which pooled connection happened to serve the
request.

### Live verification (dev stack, not just tsc/build)

Rebuilt and restarted the `crmproductsystems-worker-1` container with the fixes.
- Temporarily set `Latitude`/`Longitude` on one dev location, enqueued a real `weather-fetch`
  BullMQ job → `weather_data` populated with 7 real Open-Meteo forecast rows, correct
  `LocationId` column confirmed via direct SQL. Reverted the location and deleted the test rows.
- Inserted a temporary `iot_devices` test row (`temp_sensor`, `fridge` profile), published real
  MQTT messages via `mosquitto_pub`: 5.2°C (no alert), 9.5°C (correctly triggered the fridge
  >8°C alert per v3-spec §4), 9999°C (correctly rejected by the new plausibility check, not
  inserted). `temperature_readings` confirmed written with correct `LocationId` and `IsAlert`.
- The 9.5°C alert enqueued a real `temp_alert` notification job; confirmed in `notification_queue`
  that it found and logged all 3 real matching users for the tenant (`store_manager`/
  `network_manager`/`enterprise_admin`, status `skipped` — no Telegram chat id on test users,
  expected) — this is direct proof the `SET app.role='worker'` fix restores RLS visibility for
  `handleIotAlert`, not just that the SQL parses.
- Cleaned up all test data (device, readings, notification_queue rows, temp lat/long) after
  verification; no dev seed data left behind.

Worker `tsc --noEmit` clean after every edit.

## Sensor range validation (v3-spec §1/§4 "чи є валідація діапазонів")

Weight sensors already had this via the existing confidence model
(`assessWeightDelta` — a non-multiple-of-`unit_weight_grams` delta gets confidence 60 and is
never auto-applied, matching v3-spec §1 exactly). Temperature sensors had none — a
broken/miswired sensor could write a physically impossible reading straight into
`temperature_readings` and (if sustained 2h) falsely flag batches `temp_violation`. Added
`isPlausibleTemperature`/`isPlausibleHumidity` (`worker/src/services/iot-rules.ts`, -60..60°C
bounds — generous, this is a "sensor is broken" filter, not the business-rule
`tempAlertThreshold`) and wired it into `mqtt-listener.ts`'s `handleMessage` before the value
ever reaches `handleTemperature`. Live-verified above (9999°C rejected, logged, not inserted).
No worker test framework exists in this repo (no jest/vitest in `worker/package.json` — matches
the pre-existing pattern, zero `.test.ts` files anywhere under `worker/src`), so this is
documented via the live MQTT verification instead of a unit test.

## Functional review — no other bugs found

- **IoT device→location binding**: `IotDeviceService`/`IotDeviceRepository` — clean bulk
  queries, no N+1 (`GetLatestTemperaturesAsync` does one device query + one bulk reading query
  per store, not per-device). `RegisterAsync`/`UpdateAsync` don't cross-validate that `ZoneId`
  belongs to the device's own `LocationId` (relies on FK + RLS only) — same low-severity,
  not-fixed pattern already flagged for Receipts/Transfers in Block 4; not re-flagged as a new
  issue.
- **Weather fallback**: `WeatherCoefficientResolver.Resolve` returns `1m` (neutral) when
  `WeatherData` is null or no rules exist — confirmed by the existing
  `WeatherCoefficientResolverTests.No_weather_data_is_neutral` test. External Open-Meteo
  outage only blocks that one location's forecast row (try/catch per-store in
  `WeatherService.FetchAsync`/`weather-fetch.job.ts`), never breaks AI order generation — the
  Block 7 requirement holds.
- **Weather coefficient TempAbove/TempBelow both compare against `TempMax`** (not `TempMin` for
  the cold case) — checked whether this is a bug: it's an explicit, documented design choice
  (`WeatherCoefficient.cs`'s own doc comment: "Applies when temp_max > TempAbove, or temp_max <
  TempBelow") and is pinned by `WeatherCoefficientResolverTests.Cold_day_uses_temp_below`. Not
  a bug, left as-is.
- **Events**: `EventService`/`DefaultHolidays` coefficients match v2-spec §4 exactly (Новий рік
  2.5/3.0/1.2/1.8, Великдень 3.5/2.0/2.5, Початок школи 5.0/1.3, plus 8 березня and День
  Незалежності beyond the spec's example set — reasonable additions). `EventCoefficientResolver`
  correctly picks product > segment > category specificity and multiplies across independent
  events, matching v2-spec §3.
- **Cannibalization**: `CannibalizationService` constants (`PromoProductCoefficient = 2.0`,
  `SiblingCoefficient = 0.7`) match v2-spec §5's range exactly.
  `CannibalizationRepository.GetActivePromoCoefficientsAsync` correctly filters
  `p.IsApplied && Discount.Status == Active && ValidFrom/ValidUntil` — a generated-but-not-yet-
  applied suggestion cannot silently affect order calculations, confirmed by reading the
  repository query directly (not just the service).
- **OrderCalcService** (`Features/Orders/OrderCalcService.cs`) correctly wires all three
  multipliers (`eventCoef * weatherCoef * promoCoef`) into `OrderFormula.Compute`'s
  `demandMultiplier` — re-confirmed the Block 5 finding still holds, no regressions.
- **RLS**: all 8 tables named in the audit brief (`iot_devices`, `temperature_readings`,
  `weight_readings`, `weather_data`, `weather_coefficients`, `demand_events`,
  `demand_event_coefficients`, `promo_cannibalization`) are present in both the Block 2
  fail-closed fix (`20260714180000_FixFailOpenTenantIsolationOnReset`) and the `worker_bypass`
  migration (`20260712175141_AddWorkerBypassRlsPolicy`) — confirmed by reading both migration
  files' table arrays, and live-confirmed the actual policies on `iot_devices`/`weather_data`/
  `stock_events` via `\d` (tenant_isolation + provider_bypass + worker_bypass all present,
  fail-closed NULLIF pattern, no `IS NULL OR` branch).

## Found, not fixed — needs a product decision

**KI-019** (new): `IotController`/`WeatherController`/`EventsController`/
`CannibalizationController` — and in fact almost all of v2 (`Orders`/`Adu`/`Buffer`/
`AiOrders`) and v3 (`Pos`) — have no `[RequireModule]` gate at all, despite CLAUDE.md's
explicit architecture rule and `"auto_order"`/`"iot"`/`"pos"` all being valid module keys.
Not fixed: `Tenant.DefaultModulesForBusinessType` doesn't grant any business type these
modules by default, so naively adding the gate now would 403 every currently-working tenant
that hasn't been manually granted the module — a real risk of breaking live functionality
for near-launch clients. Needs a product decision on default module sets / backfill /
whether v2-v3 was deliberately left role-gated-only. See `known-issues.md` KI-019 for full
detail.

## Build/test status

`dotnet build` 0 err/0 warn, `dotnet test` 869/869 green (unchanged — this block touched only
`worker/`, no backend C# changes). Worker `tsc --noEmit` clean after every edit. Live-verified
end-to-end on the dev Docker stack (see above), not just static checks.
