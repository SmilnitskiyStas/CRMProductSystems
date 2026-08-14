# TASK-525: Consumer App — split into separate pages + banner/promo history tabs

**Agent:** frontend-developer
**Date:** 2026-08-14
**Status:** done — built, tsc/lint clean, all flows live-verified against the real dev backend.
No blocker.

## Context

Follow-up to TASK-522 (admin frontend, done) using the lifecycle contract TASK-523/524
(database-engineer/backend-developer) already shipped: `BannerDto.publishedAt`/`lifecycleStatus`
("draft"|"running"|"past"), `POST /api/banners/{id}/publish`,
`CreateBannerRequest.publishImmediately`. `Discount` needed zero backend changes — its own
`status` enum (pending/active/expired/cancelled) already covers the same 3 buckets.

## Done

### Part 1 — page split (mirrors `marketing-analytics`'s group exactly)
- `frontend/app/(dashboard)/consumer-app/page.tsx` — now hosts only `<BonusProgramSection />`.
- New `frontend/app/(dashboard)/consumer-app/banners/page.tsx`,
  `.../promotions/page.tsx`, `.../catalog/page.tsx` — same role gate
  (`AT_LEAST_ENTERPRISE_ADMIN`) and page-shell shape as the existing page, one section each.
- `frontend/components/layout/Sidebar.tsx` — `consumer_app` group's `items` expanded from 1 to 4
  (`/consumer-app` now `exact: true`, plus `/consumer-app/banners`, `/consumer-app/promotions`,
  `/consumer-app/catalog`). Replaced the stale TASK-500 comment (said sections would stay on one
  page) with one noting TASK-525 reversed that decision.
- i18n: `Dashboard.sidebar.groups.consumerApp.{banners,promotions,catalog}` +
  `Dashboard.consumerApp.{bannersPage,promotionsPage,catalogPage}.{title,subtitle}` in both
  `uk.json`/`en.json`.

### Part 2 — Banners history tabs
- New shared `frontend/features/consumer-app/components/LifecycleTabs.tsx` (Активні/Минулі/
  Чернетки strip with count badges, same underline-tab visual as `ModeTabs.tsx`) — reused by both
  `BannersSection` and `PromoProductsSection`.
- `BannersSection.tsx` — tabs filter the already-fetched list by `banner.lifecycleStatus` (no new
  fetch). Draft rows get a row-level "Опублікувати" action (`usePublishBanner`, new hook wrapping
  `POST /{id}/publish`). Row status pill now shows a distinct "Чернетка" label for drafts instead
  of the isCurrentlyActive-derived Active/Paused pill (that pill was misleading pre-publish, e.g.
  a never-shown draft could still read "Активний").
- `BannerForm.tsx` — "Опублікувати одразу" toggle (default ON, create mode only — sent as
  `publishImmediately` on every create call, never omitted). Editing a draft
  (`lifecycleStatus === "draft"`) hides the toggle and shows a standalone "Опублікувати" button
  calling the same publish hook, since `UpdateBannerRequest` has no such field.
- `frontend/features/consumer-app/types.ts` — `BannerDto` gained `publishedAt`/`lifecycleStatus`;
  `CreateBannerRequest.publishImmediately` added as a required field (client always sends it
  explicitly, matching TASK-524's contract note).
- `frontend/features/consumer-app/api/banners.ts` / `hooks/useBanners.ts` — added
  `bannersApi.publish` / `usePublishBanner`.

### Part 3 — Promotions history tabs
- `PromoProductsSection.tsx` — same 3-tab pattern, bucketed from `Discount.status` (pending→draft,
  active→running, expired|cancelled→past). Removed the `status=active` query param from
  `usePromoProducts` — `GET /api/discounts?storeId=` now returns full history, bucketed
  client-side. "Опублікувати одразу" toggle (default ON) in the add form: ON keeps today's
  create→approve chain; OFF only creates (`status: "pending"`). Чернетки-tab rows get both
  "Опублікувати" (`PUT .../approve`, new `usePublishPromoProduct` hook) and the existing "Зняти з
  акції" (cancel — `Discount.Cancel()` explicitly allows cancelling from pending, doubling as
  discard-draft). Минулі tab is read-only (no actions), matching `Discount.cs`'s own guards.
- `hooks/usePromoProducts.ts` — `useCreatePromoProduct` now takes a `publishImmediately` flag
  controlling whether the approve call fires; added `usePublishPromoProduct`.

## Verification

- `npx tsc --noEmit` — clean.
- `npm run lint` (`next lint`) — clean, no warnings.
- Live browser verification (Chrome preview, `ea@demo.local`/enterprise_admin, dev backend
  `:5000` + dev Postgres `crmproductsystems-postgres-1`):
  - Sidebar: confirmed 4 links under "App" (`/consumer-app`, `/consumer-app/banners`,
    `/consumer-app/promotions`, `/consumer-app/catalog`), each its own page.
  - `/consumer-app` now renders only the Bonus Program card (other 3 sections gone).
  - Banners: created one banner with the toggle ON → `POST /api/banners` returned
    `lifecycleStatus: "running"`, `publishedAt` set, appeared under Активні (count 1→ correct).
    Created a second with the toggle OFF → response had `publishedAt: null`,
    `lifecycleStatus: "draft"`; confirmed it landed in Чернетки (not Активні); clicked the row's
    "Опублікувати" → `POST /{id}/publish` → 200, banner moved to Активні (Running 1→2, Drafts
    1→0).
  - Promotions: same OFF/ON check — toggle ON fired `POST /api/discounts` then
    `PUT .../approve` (as before); toggle OFF fired only the `POST` (`status: "pending"`), item
    appeared in Чернетки with both "Опублікувати" and "Зняти з акції"; clicking "Опублікувати"
    fired `PUT .../approve` → 200, moved Running 1→2, Drafts 1→0. Confirmed the
    `status=active` query param is gone from the list request (`GET /api/discounts?storeId=`
    only) and that pre-existing Минулі (Past) rows (2, unrelated legacy data) were visible from
    a single fetch.
  - Catalog page renders its own heading + `CatalogSection`.
  - Consumer-feed spot-check: `GET /api/consumer/{tenantId}/banners` (no `storeId` supplied,
    so 200/[]) — not a full re-verification of TASK-524's already-covered exclusion logic
    (`PublishedAt != null` filter, untouched by this task); relied on the create-response
    `lifecycleStatus`/`publishedAt` fields instead as the primary signal, per the brief's
    "don't break it, not re-verify from scratch" scope.
  - Cleanup: cancelled both test discounts via UI ("Зняти з акції"), deleted both test banners
    via UI (soft), then hard-purged all 4 rows via `psql` (`crmproductsystems-postgres-1`,
    user `crm`) for zero dev-DB residue — confirmed both pages back to 0/0/0 (banners) and
    0 Running/2 Past-legacy/0 Drafts (promotions).
  - Screenshot not captured — same known Browser-pane compositing limitation as TASK-522's log
    (not an app issue); verification used `read_page`/`get_page_text`, live network inspection,
    and direct API response bodies instead. A few `computer{action:"left_click"}` calls on tab
    buttons and row-action buttons didn't register (same automation quirk TASK-522 flagged) —
    worked around with direct `element.click()` calls, cross-checked against the resulting
    network requests each time.

## Deviations from the brief / judgment calls

- Row status pill inside Банери (BannersSection) now derives from `lifecycleStatus` for the
  draft case specifically (new "Чернетка" grey pill) instead of leaving the pre-existing
  isCurrentlyActive-based Active/Paused pill untouched — a draft could otherwise show "Активний"
  which reads as "live to consumers" when it never was. Running/Past rows keep the original
  isCurrentlyActive-based Active/Paused pill unchanged. Not explicitly requested, but small and
  directly serves the brief's own "make draft state clear" goal; flagged here per the
  clarify-before-implementing judgment-call carve-out (objective UX correctness, not a product
  decision).
- `LifecycleTabs.tsx` is a new small shared component (not explicitly requested) rather than
  duplicating the identical 3-tab strip inline in both sections — straightforward DRY, same
  visual language as the pre-existing `ModeTabs.tsx` pattern elsewhere in the codebase.

## Not in scope (per brief)

- No backend changes (none needed).
- No `mobile/` changes — still tracked separately per TASK-521's handoff doc.
- `.claude/docs/` not updated — consistent with TASK-520..524 precedent (deferred to a
  documentation-writer pass once the full Consumer App admin feature line closes).

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `frontend/app/(dashboard)/consumer-app/page.tsx` (trimmed to BonusProgramSection only)
- `frontend/app/(dashboard)/consumer-app/banners/page.tsx` (new)
- `frontend/app/(dashboard)/consumer-app/promotions/page.tsx` (new)
- `frontend/app/(dashboard)/consumer-app/catalog/page.tsx` (new)
- `frontend/components/layout/Sidebar.tsx` (consumer_app group: 1 → 4 items)
- `frontend/features/consumer-app/types.ts` (BannerDto/CreateBannerRequest lifecycle fields)
- `frontend/features/consumer-app/api/banners.ts` (added `publish`)
- `frontend/features/consumer-app/api/discounts.ts` (doc comment only)
- `frontend/features/consumer-app/hooks/useBanners.ts` (added `usePublishBanner`)
- `frontend/features/consumer-app/hooks/usePromoProducts.ts` (publishImmediately flag,
  `usePublishPromoProduct`, dropped `status=active` filter)
- `frontend/features/consumer-app/components/LifecycleTabs.tsx` (new, shared)
- `frontend/features/consumer-app/components/BannersSection.tsx` (tabs + publish row-action)
- `frontend/features/consumer-app/components/BannerForm.tsx` (publish toggle / publish-now button)
- `frontend/features/consumer-app/components/PromoProductsSection.tsx` (tabs + publish toggle +
  row actions)
- `frontend/messages/uk.json`, `frontend/messages/en.json` (sidebar + page + tab + publish keys)
