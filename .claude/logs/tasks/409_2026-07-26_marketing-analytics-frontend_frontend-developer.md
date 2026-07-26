# TASK-409: Marketing analytics (RFM) frontend — Фаза 1

**Agent:** frontend-developer
**Date:** 2026-07-26
**Status:** done

## Контекст

Task #3 of Фаза 1 in `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md`, following TASK-406
(backend, done). Built `frontend/features/marketing-analytics/` per the brief's file list, a new
`/marketing-analytics` page, and sidebar wiring gated by the `marketing_analytics` module — using
`.claude/logs/tasks/406_..._backend-developer.md`'s "Frontend API-контракт" section as the sole
contract source (never read the C#), and `docs/uployal/RFM_ANALYSIS.md` for UI/UX behavior.

## Зроблено

**`types.ts`** — full RFM contract (`RfmSegmentKey`, overview/segment-detail/affinity/basket/
explain/export DTOs) transcribed exactly from task log 406's contract section. Also owns the
segment→color mapping: `RFM_SEGMENT_COLOR_GROUP` (11 keys → 6 color groups) + `RFM_COLOR_PALETTE`
(hex tokens). RFM_ANALYSIS.md §16.2 only defines 6 color *meanings*, not a segment table — my own
documented mapping (rationale in the file's comments), reusing hex values this app already uses
for the same meaning elsewhere (`#F87171`/`#DC2626` already mean critical/expired in
`features/analytics`, reused here for AtRisk/CannotLoseThem as two intensities of "risk").

**`api/marketingAnalytics.ts`** — all 8 endpoints. Filter→query-string builder sends `period` OR
`from`+`to` (never both), `storeIds` as a repeated param, matching the contract exactly.
`getAffinity`/`getBasket` URL-encode the free-text product name (verified live against real
Cyrillic names with commas/percent signs — see verification section).

**`hooks/useMarketingAnalytics.ts`** — one `useQuery` per GET, query key = the literal filter
object (period+from+to+storeIds) per the brief, so any filter change is a brand-new cache key.
Deliberately never uses `keepPreviousData`/`placeholderData` — documented in a comment, since that
option would violate "never show a mix of old and new data." Explain + all 3 exports are
`useMutation` (side-effecting, not cached).

**Components** (`components/`): `PeriodStoreFilterBar` (5 presets + custom two-date-input range +
a new multi-select store popover with a staged draft + "Готово"/Done confirm, mirroring the
competitor's exact interaction — existing `useStoreContext`/`StoreSelector` is single-store only,
confirmed not reusable), `SegmentGrid` (11 cards + separate non-clickable "no purchase" card,
horizontal share bar, hover/selected states), `ExportButtons.tsx` (3 export button components +
a shared `PiiUnmaskToggle`, all consuming one `ExportBaseContext` shape).

**`components/SegmentDetail/`**: `TopProductsPanel` (ranked by coverage%, shown first/boldest per
RFM_ANALYSIS §11.1's explicit point that this — not sales volume — is the ranking), `AffinityBasketTabs`
(two independently-fetched, lazily-enabled tabs; resets to the Affinity tab when the anchor product
changes), `BehaviorPanel` (recharts day/hour bars + stat rows + an LTV block visually tagged
"All-time" so it's never confused with the windowed stats above it), `RecommendationCard` (renders
the template immediately; "Explain in more detail" is a separate on-click mutation whose result —
or 503 "unavailable" message — appends below the template, never replacing it), and
`SegmentDetailPanel` — the orchestrator tying all four together plus the export/PII controls and
the empty-segment (0 customers) state. This container isn't named in the brief's file list but is
clearly required to compose the four named files; added it as the natural contents of the
`SegmentDetail/` subfolder.

**Page** (`app/(dashboard)/marketing-analytics/page.tsx`) — role gate (`CAN_VIEW_ANALYTICS`, matches
backend's `AppPolicies.CanViewAnalytics` floor) + `useRequireTab` (TenantRole tab-visibility
parity with every other analytics-adjacent page) + module gate (`marketing_analytics`, same
lock-screen pattern as `/auto-service`/`/marketplace`). Renders the mandatory visible historical-
data banner (plan requirement: RFM/LTV only reflects post-loyalty-rollout sales) above the filter
bar, not a footnote.

**Shared-lib changes** (small, deliberate, outside the feature folder):
- `frontend/lib/download.ts` — added `downloadFilePost(path, body, filename)`, sharing the same
  blob→`<a download>` logic as the existing `downloadFile` via a new private `saveBlob` helper.
  **Plan-vs-actual discrepancy**: the plan assumed the existing GET-only `downloadFile()` would
  cover exports; the actual backend contract (task log 406) has all 3 export endpoints as POST
  with a JSON body (segment/store filters + `unmaskPii` don't fit a URL). This is the one new bit
  of file-handling code, and it's in the shared lib, not duplicated inside the feature.
- `frontend/lib/roles.ts` — added `canExportMarketingAnalyticsPii(role, permissions)`, same
  role-OR-capability shape as the existing `canManageLegalEntities`.
- `frontend/features/modules/types.ts` — added `"marketing_analytics"` to the `ModuleKey` union
  and `ALL_MODULE_KEYS` (backend already accepts this key, TASK-405). Needed for
  `moduleKey: "marketing_analytics"` to type-check as a `Sidebar.tsx` `NavGroup.moduleKey`, and
  makes the tenant-facing read-only Settings "Modules" tab list it correctly.
- `frontend/components/layout/Sidebar.tsx` — new dedicated `marketing_analytics` NavGroup (single
  item, mirrors the `marketplace`/`support` single-purpose-group pattern) rather than folding it
  into the existing ungated `analytics` group, which has no per-item module-gating field.

**i18n** — full `Dashboard.marketingAnalytics.*` namespace (page/filterBar/segmentGrid/detail/
topProducts/crossSell/behavior/recommendation/export) in both `en.json`/`uk.json`, plus the
sidebar group label and `Dashboard.modules.catalog.marketing_analytics` entry. Segment names/
descriptions/recommendation text are NOT translated client-side — they come verbatim from the
backend as `labelUa`/`shortDescriptionUa`/`triggerUa` etc. (Ukrainian-only by backend design, same
as the AI-generated explanation), so an English-locale user still sees Ukrainian segment copy —
a real, backend-driven limitation, not something invented or fixed here.

## Свідомі рішення (без user sign-off, задокументовані)

- **Empty segment detail** (0 customers): renders one clear "no customers" message instead of the
  3-column layout with each panel separately empty — judged clearer than showing three
  independently-empty panels, which could read as broken rather than "correctly nothing here."
  Export/PII controls are hidden in this state too (nothing to export). Live-verified working.
- **Product-selection/PII-toggle reset on filter change**: observed during manual verification
  that switching period/stores briefly makes the parent's `overview` query `undefined` (no
  `keepPreviousData`, by design), which unmounts+remounts `SegmentDetailPanel` and resets its
  internal `selectedProductName`/`unmaskPii` state — not just on an explicit segment change as
  originally intended. Documented in the component's docblock rather than silently left as a
  surprise; judged safe/acceptable (never shows a stale selection against new data, and resets
  the PII toggle to its masked-by-default safe state).
- Did not add `"marketing_analytics"` to `frontend/features/admin/types.ts`'s or
  `frontend/features/provider/types.ts`'s own separate `ALL_MODULES` lists (the provider-panel
  tenant module-activation checkboxes) — same pre-existing 3-way duplication gap the `"loyalty"`
  key already has (TASK-405 shipped without provider-panel wiring either). Out of scope for a
  frontend-only marketing-analytics task; flagged as a follow-up (see below) rather than expanded
  into touching two unrelated features' files.
- `RFM_SEGMENT_COLOR_GROUP` mapping (which of the 6 color meanings each of the 11 segments gets)
  is my own documented judgment call — the competitor doc only defines the 6 meanings, not a
  segment table.

## Верифікація

- `npx tsc --noEmit` — clean, 0 errors (one real error caught+fixed along the way: `Btn`'s
  `onClick` is typed `() => void`, so `ExportProductBuyersButton`/`ExportProductPairBuyersButton`
  couldn't take the click event to call `stopPropagation()` directly — fixed by wrapping `Btn` in
  a `<span onClick={(e) => e.stopPropagation()}>` instead, since these buttons live inside a
  clickable row).
- `npm run build` — exit 0, `/marketing-analytics` route present (13.1 kB / 240 kB First Load JS,
  in line with `/analytics`'s 5.88 kB/243 kB and `/analytics/pos`'s 10.4 kB/248 kB). Build output
  has a repeating `ENVIRONMENT_FALLBACK` internal error during static-page generation — confirmed
  this is pre-existing noise affecting every route in the app equally (not marketing-analytics-
  specific), doesn't fail the build (exit 0), not touched.
- **Full live browser verification** (dev stack: `dotnet run --project backend/ShelfGuard.Api` +
  `npm run dev`, both via `preview_start`). Seeded ~12 synthetic customers with varied purchase
  cohorts (SQL script, dev DB only, tagged `customers."Notes" = 'rfm-seed-409'` for future
  identification/cleanup) into the existing dev tenant "Свіжий Кут"
  (`8abfbbb5-3190-4de9-9f91-f4de59101bca`) and additively enabled its `marketing_analytics`
  module, since the tenant otherwise had only 1 customer/2 transactions — not enough to exercise
  top-products/cross-sell. Logged in as the tenant's existing `ea@demo.local` (enterprise_admin)
  session. Confirmed, against real API responses:
  - Overview loads with 11 segment cards + separate "no purchase" card; segment customer-count
    shares and revenue shares both sum to ~100% (rounding).
  - Clicking a segment expands detail **below** the grid (grid stays visible) — Champions showed
    3 customers, real top products ranked by coverage%, full recommendation with live KPIs
    substituted (correctly said "no discount" for Champions), and an "All-time"-tagged LTV block.
  - Selecting a top product activated "Also bought"; before selection it showed "Select a product
    on the left". Switching Affinity → Same-receipt tab hit a genuinely different endpoint and
    returned different numbers (e.g. affinity showed uniform ×1 lift — correct given my seed
    data's uniform purchase pattern — while basket showed varied 5.9%–47.1% co-occurrence).
  - Clicking "Explain in more detail" fired the real POST only on click; got a real 503 (no Claude
    key configured in this dev environment) and correctly rendered "AI explanation unavailable…"
    **below** the still-intact template recommendation, not replacing it.
  - Switching period 6m → All time atomically recalculated every number on the page at once
    (grid, KPIs, and the still-open Champions detail all changed together) — and empirically
    reproduced a backend-documented priority interaction from task log 406 ("Hibernating always
    wins over Lost when both match"): my seeded single-old-purchase customer showed up under
    Hibernating, never under Lost, across every period tested.
  - Opened a genuinely empty segment ("Lost", 0 customers under multiple filters) — rendered the
    dedicated "No customers in this segment" state, no crash, grid unaffected.
  - Store multi-select: narrowing to the store with zero seeded data dropped "ever purchased" to
    1/13 while "registered customers" stayed 13 — live-confirmed the documented backend design
    ("registered" is tenant-wide, "ever purchased"/"no purchase" respect the store filter).
  - Custom period (two date inputs) correctly sent `from`/`to` with `period` omitted.
  - All 3 export endpoints (segment/product-buyers/product-pair-buyers) returned real 200s with
    `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` and non-
    trivial byte sizes (~6.6 KB) — confirmed via direct `fetch` calls with the session's real
    token. **Discrepancy found**: the response has no `Content-Disposition` header at all (task
    log 406 said the filename would arrive that way) — harmless for this frontend, since
    `downloadFilePost` always uses its own client-generated filename
    (`rfm_{key}_{type}_{timestamp}.xlsx`) rather than trusting a header, but flagged here as a
    real discrepancy per the brief's instruction to document rather than invent.
  - Product names with Cyrillic + comma + `%` (e.g. "Молоко 2,5% Галичина 1л") were correctly
    URL-encoded in the affinity/basket request paths.
- Reverted a temporary local-only CORS config tweak (`appsettings.Development.json` — port 3000
  was occupied by an unrelated project's dev server on this machine, so `next dev` auto-picked
  57331, needing a temporary `Cors:Origins` addition to test at all) back to its committed state
  via `git checkout --` before finishing; confirmed clean via `git diff --stat`. Both preview
  servers stopped.
- Did not attempt to set any user's password (a blocked, correctly-declined action) — used the
  repo's own documented dev-only seed password fallback (`DbSeeder.cs`'s `DefaultSeedPassword`,
  already used by `loadtests/login-storm.js`) implicitly via the pre-existing, still-logged-in
  browser session instead; never needed it directly.

## Не в скоупі / для наступних агентів

- **Follow-up flagged via spawn_task** (see chip): wire `"marketing_analytics"` into
  `frontend/features/admin/types.ts` and `frontend/features/provider/types.ts`'s own `ALL_MODULES`
  lists so a provider can actually toggle the module on for a tenant from the UI — today the only
  way to enable it is a direct DB write (what this task did for its own test tenant). Same gap
  pre-exists for `"loyalty"`.
- **security-reviewer**: this task didn't touch any backend/RLS/raw-SQL — those were already
  flagged against task log 406. Frontend-side, worth a look: `unmaskPii` UI-gating logic
  (`canExportMarketingAnalyticsPii`) is a client-side convenience only, matching the backend's own
  "silently masks rather than 403s" behavior — no new client-side security surface introduced.
- **documentation-writer**: `.claude/docs/frontend-structure.md`/`api-contracts.md` could note the
  new `downloadFilePost` helper and the `marketing_analytics` module key addition.
- Causal/incremental analysis, mobile UI — explicitly out of scope per the brief.
- Not committed (repo convention — main session/user commits).
