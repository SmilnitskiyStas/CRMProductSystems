# TASK-660 (T13) — Mobile marketplace: delivery coverage + per-region metrics + response-time tile; fix fraction tiles

**Status:** done
**Agent:** mobile-developer
**Plan:** `C:\Users\stass\.claude\plans\eventual-whistling-rabbit.md` — "Mobile" section, T13 of T1–T16.
**Depends:** T3 / TASK-650 (final `SupplierProfileDto` / `SupplierMetricsDto` shape — on `main`,
commit `c5f02043`); backend `GET /api/geo/regions` (TASK-648, commit `db9e6cb7`).
**Scope:** mobile only. Read-only display — no supplier profile editing on mobile.

## Concurrent-work check

`git status` / `git diff --stat` before starting: only the known pre-existing unrelated
`M mobile/features/pos/receiptPrinting.ts` (another session's POS work) — not touched, not staged.
No uncommitted changes under `mobile/features/marketplace/` or `mobile/app/(app)/marketplace/`.
A concurrent frontend agent (T9) was actively editing `frontend/` files during this task
(`SupplierMetrics.tsx`, `marketplace/[id]/page.tsx`, new `DeliveryByRegionPanel.tsx` /
`SupplierCoveragePanel.tsx`, `messages/*.json`) — all `frontend/`, disjoint from this mobile
scope. Mobile Ukrainian strings were aligned to that agent's `frontend/messages/uk.json` wording
for cross-platform consistency (e.g. "на основі N замовлень", "за домовленістю",
"Не доставляє: …").

## What changed

### New — `mobile/features/geo/` (RN mirror of `frontend/features/geo/`)
- `types.ts` — `Region`, `DeliveryCoverageEntry`, `DeliveryCoverage`.
- `api.ts` — `getRegions()` → `apiClient.get('/geo/regions')` (mobile `apiClient` baseURL already
  ends in `/api`, so the path is `/geo/regions`, matching the `marketplace/api.ts` idiom).
- `hooks.ts` — `useRegions()` (React Query, `staleTime: Infinity`), `useRegionLabel()` →
  `(code) => string` (falls back to the raw code while the registry loads / on unknown code).
- No components (read-only inline labels only).

### `mobile/features/marketplace/types.ts`
- New `RegionDeliveryStat { regionCode; avgDeliveryDays; sampleSize }`.
- `SupplierMetrics` += `deliveryByRegion?: RegionDeliveryStat[] | null`, `deliverySampleSize?`,
  `responseSampleSize?`, `aggregatesComputedAt?: string | null` (all appended-optional; nightly
  worker job may not have run). Comment added noting `orderAccuracy`/`qualityScore` are 0–1
  fractions.
- `SupplierProfile` += `deliveryCoverage?: DeliveryCoverage | null` (imported from
  `@/features/geo/types`).

### `mobile/app/(app)/marketplace/[id].tsx`
- **Info card**: new `<DeliveryCoverageBlock>` after the payment-terms row, rendered only when
  `profile.deliveryCoverage` is present — "Регіони доставки" header, served regions
  (`useRegionLabel` name + `terms` or "за домовленістю"), muted "Не доставляє: …" line for
  `notServed`, and the free-text `note`. Matches the existing muted info-card row styling; a
  `border-t` separator sits above it.
- **`MetricTile`** extended with optional `fallback` (shown instead of "—" when value is null)
  and `sublabel` (faint sample-size line).
- **Metrics card** now 2×2 tiles:
  - `Доставка` — unchanged value, `sublabel` = "на основі N замовлень" from `deliverySampleSize`.
  - **`Час відповіді`** (NEW — previously omitted on mobile) — `responseTimeHours` + " год.",
    `fallback` "недостатньо даних" when null, `sublabel` "на основі N звернень" from
    `responseSampleSize`.
  - `Точність` / `Якість` — **fraction bug fixed**: was `Math.round(x)` + "%" (0.87 → "0%"), now
    `Math.round(x * 100)` + "%". `qualityScore` is always null from the backend → renders "—".
  - Collapsible per-region list ("Детальніше по регіонах" / "Приховати регіони" toggle) →
    `<DeliveryByRegionList>` — region name · `N дн.` · faint `n=N`, sorted fastest-first; shows
    "Ще недостатньо даних по регіонах" when `deliveryByRegion` is empty. Toggle appears when
    there is a per-region breakdown or a `deliverySampleSize`.
- Каталог / Відгуки tabs unchanged.

### `.claude/docs/known-issues.md`
- New **KI-037** documenting the fraction-tile bug and its fix.

## Verification

- `npx tsc --noEmit` — clean.
- `npx eslint` on touched files — 0 errors; 1 warning, **pre-existing** (`'FlatList' is defined
  but never used` in `[id].tsx`, unrelated to this change — left untouched to keep the diff
  minimal).
- `npm run test:ci` — 65 suites / 308 tests pass. **1 pre-existing unrelated failure**:
  `features/server-driven-ui/__tests__/resolveBlocks.test.ts` › "resolves authored newsList props
  into renderable data" (newsList resolver returns `{"items":[]}` — a fixture/resolver mismatch
  from the concurrent consumer-app workstream, commit `17d7c1b9`). `resolveBlocks.ts` imports only
  `consumer-content` / `loyalty` / `mobile-config` — nothing from `marketplace` or `geo`, so this
  failure is not caused by TASK-660.
- `npx expo export --platform android` — PASS (Metro bundled to a single 6.9MB hbc; exit 0).
- Emulator / Expo device run: NOT run (no Android device/AVD available in this environment, same
  standing TASK-435 constraint).

## Notes

- Read-only: no `DeliveryCoverageEditor` equivalent, no profile-write path on mobile.
- Mobile screen hardcodes Ukrainian (no i18n helper on this screen yet — TASK-451 is the mobile
  localization foundation); strings added here follow that convention and the web wording.
