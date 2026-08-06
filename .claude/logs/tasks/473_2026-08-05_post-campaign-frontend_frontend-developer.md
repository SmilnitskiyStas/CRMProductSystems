# TASK-473: Frontend — Фаза 4 post-campaign audience analysis dashboard

**Agent:** frontend-developer
**Date:** 2026-08-05
**Status:** done — `tsc --noEmit`/`next lint`/`next build` all clean, no blocker.

## Context

Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фази 2-4". Source doc:
`docs/uployal/AUDIENCE_ANALYSIS.md`. Backend contract: task log 472 + read directly from
`PostCampaignDtos.cs`/`PostCampaignController.cs`/`PostCampaignService.cs`/`SegmentImportParser.cs`
(the DTO/controller files, not just the task-log prose, per the brief).

## What this pass built

New feature `frontend/features/marketing-analytics/post-campaign/`:
- `types.ts` — full DTO mirror + `RFM_PRIORITY_ORDER`/`RFM_LABELS_UA` (verbatim copy of backend
  `RfmSegmentCatalog.AllKeysInPriorityOrder`/`LabelUa` — needed because the migration matrix must
  render a FULL fixed 12-segment axis with dots for empty cells, but the API only ever returns
  non-zero cells/distribution entries). Re-exports `RfmSegmentKey`/`RFM_COLOR_PALETTE`/
  `RFM_SEGMENT_COLOR_GROUP`/`RFM_CANNOT_LOSE_THEM_ACCENT`/`RFM_NO_PURCHASE_COLOR` from the Фаза 1
  root `types.ts` so every component in this feature imports color tokens from its own `../types`.
- `api/postCampaign.ts` (all 10 endpoints, multipart import via `api.postForm`, repeated
  `storeIds` query param matching `price-segments/api`'s `buildStoreQs` convention) +
  `hooks/usePostCampaign.ts` (React Query, no `keepPreviousData` anywhere — same "clean loading
  state over a stale mixed number" rule every sibling phase follows).
- `store/usePostCampaignStore.ts` (Zustand, per the brief's explicit instruction to mirror
  `audience-builder`'s draft/committed pattern) — tracks `draftSegmentId` (latest import) vs
  `reportSegmentId` (segment actually displayed) as two separate ids, not one boolean, because
  every import call creates a brand-new `PostCampaignSegment` row server-side (no "update the same
  draft" endpoint) — the gap between the two ids IS the source doc §7 "unapplied changes" banner.
  Also tracks a `reportVersion` (the segment's `analyzedAt`) folded into every report query key,
  since the report GETs take no date params at all (the window is frozen server-side by
  `POST .../analyze`) — without a version bump, re-analyzing the SAME segment with new dates
  (§10.4 "Оновити") would never trigger a refetch. `clearAll()` implements source doc §9's exact
  rule (clears draft+report, keeps input mode and the after-period).
- 17 components: `ImportPanel`/`ColumnPreviewPicker` (list-or-file, drag/drop, auto-detected-column
  confirm/override — no client-side parse preview exists, since the strict parser is
  server-only by design; "Розпізнано N" only appears after a real import call returns),
  `ValidationSummary` (counts + first-20 unknown/invalid samples + error-report export),
  `DraftAnalyzedBanner`, `AfterPeriodBar` (date range + 5 presets + store filter + read-only
  before-window + the Analyze/Refresh trigger), `SegmentsList` (simple reopen dropdown),
  `TopKpiCards` (5 cards incl. the explicit "Не повернулись" 5th card the brief calls out as a
  fix over the competitor), `ReportTabs`, `RecommendationCard`, `DailyTurnoverChart`
  (recharts dual-line, solid teal after / dashed gray before, `k`-shorthand Y-axis),
  `SegmentStatusDonut`, `OverviewTab`, `RfmActivityCards` (4 cards, recency's inverted
  "down is good" color convention), `MigrationTab`/`MigrationDonuts`/`TransitionMatrix` (full
  12×12 grid, sticky first column, dot for empty cells, green=up/red=down/neutral=diagonal),
  `CustomerTable` (full server pagination, no Top-200 cap, reuses `SortableHeader`/
  `TablePaginationFooter` from `price-segments/components/TableControls.tsx` verbatim).
- Route `app/(dashboard)/marketing-analytics/post-campaign/page.tsx` — same `useMe`/`hasRole`/
  `useRequireTab`/`useModules` gating as the 3 sibling pages; owns every query + tab/pagination
  state, passes data down as props.
- Sidebar: 4th item in the existing `marketing_analytics` group (new `LineChart` icon; label kept
  short per commit `2538d285`'s precedent — "Post-Campaign"/"Пост-кампанія").
- i18n: full `Dashboard.postCampaign.*` namespace in both `en.json`/`uk.json` (validated with
  `JSON.parse` after editing) + 1 sidebar label each.

## Deliberate deviations from the brief (reasoned, not oversights)

1. **`RecommendationCard`/export buttons built locally, not literally imported.** The brief's
   Boundaries section lists these among components to "call the existing exports, don't
   copy-paste" — but on inspection, `RecommendationCard.tsx`'s actual implementation is hard-wired
   to the RFM `/explain` endpoint's own request shape (`segmentKey`+`filters`), and Фаза 2/3
   (`RecommendationBlock.tsx`, `ExportReceiptsButton.tsx`) both independently re-implement this
   exact block locally against their OWN DTO/endpoint shape rather than importing Фаза 1's version
   — confirmed by reading all three. Followed that actual, repeated precedent: built a local
   `RecommendationCard.tsx` (same visual Block/Sparkles/AI-badge pattern, own `/explain` wiring)
   and inline export buttons. The one genuinely generic, stateless piece — `PiiUnmaskToggle` — IS
   imported literally from the Фаза 1 root `ExportButtons.tsx`, per the brief.
2. **`BehaviorPanel.tsx` (cited as the recency delta-convention precedent) carries no such
   convention.** Read it in full — it only shows single-window stats, no before/after comparison,
   so it has no up/down/color rule to follow. Used `price-segments/AllTimeKpiCards.tsx`'s
   `InsightChip` instead (this codebase's actual delta-indicator precedent: icon from raw sign,
   color from "goodness" — inverted for recency only, giving the brief's requested
   positive-color/down-arrow combination for a negative recency delta).
3. **Store multi-select filter added to `AfterPeriodBar`**, not explicitly listed in the brief's
   screen flow. Every report GET accepts `storeIds`, and every sibling phase's own filter bar
   already exposes it — omitting it would be a real capability gap, so it was added following the
   same dropdown pattern as `audience-builder/components/AudiencePeriodBar.tsx` (re-implemented
   locally, not imported, matching that component's own "each phase owns its filter bar" doc
   comment).
4. **Customer table + its export always render below the 3 report tabs**, not nested inside one of
   them — the brief lists "Three tabs" (item 6) and "Customer table" (item 7) as separate bullets;
   the source doc's own section numbering also treats them as distinct.
5. One real bug caught during self-review (not by the type-checker): `ColumnPreviewPicker`'s local
   `pendingColumnIndex` override could leak from one file's chosen column into a completely
   different file's preview if the user picked a new file without first confirming. Fixed by
   resetting it inside `submit()`'s `onSuccess` (every fresh import result clears it).

## Build/verification status

- `npx tsc --noEmit` — clean, 0 errors.
- `npm run lint` — clean, 0 warnings.
- `npm run build` — exit 0, all 57 pages generated, `/marketing-analytics/post-campaign` present
  (15.4 kB, 257 kB First Load JS, in line with sibling analytics routes).
- **Dev server / route check (not full auth verification):** started `frontend-dev`, navigated to
  `/en/marketing-analytics/post-campaign` — server responded 200, Next.js compiled the full module
  graph with no errors, only console output was the expected `ERR_CONNECTION_REFUSED` (backend API
  wasn't running) and a pre-existing, unrelated `next-intl` `ENVIRONMENT_FALLBACK` warning that
  also fires on other, untouched pages. The app then redirected away exactly like every other
  `(dashboard)/*` route does when unauthenticated (`layout.tsx`'s own `!getToken()` guard) — not a
  defect in this page. **Could not verify the fully-authenticated rendered UI** (real KPIs/charts/
  matrix/table with seeded data, the way task log 421's browser pass did): that requires the .NET
  API running against the already-up Postgres container plus a valid logged-in session, and this
  product has no self-service signup (provider-onboarded tenants only) — no seeded local
  credentials were available in this session to reach that state within reasonable scope. Stating
  this explicitly per the brief rather than overclaiming a full live-UI pass.

## Not in scope (per brief, unchanged)

No backend files touched. No Фаза 0-3 frontend internals modified — only imported
(`TableControls`, `PiiUnmaskToggle`, RFM color constants) or read for pattern reference. No mobile
changes.

## Files

- `frontend/features/marketing-analytics/post-campaign/**` (new — types, api, hooks, store, 17
  components)
- `frontend/app/(dashboard)/marketing-analytics/post-campaign/page.tsx` (new)
- `frontend/components/layout/Sidebar.tsx` (new nav item + `LineChart` icon import)
- `frontend/messages/en.json`, `frontend/messages/uk.json` (full `postCampaign` namespace + 1
  sidebar label each)

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
