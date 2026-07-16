# i18n Rollout Plan — Authenticated Dashboard (uk/en)

**Owner:** project-architect
**Status:** research/plan, no implementation yet
**Created:** 2026-07-16

## Context

Block 0 (done) added `next-intl` bilingual support to the public landing only:
`frontend/i18n/{routing,request,navigation}.ts`, `frontend/messages/{uk,en}.json`
(namespace `Landing.*`), `frontend/app/[locale]/{page,layout}.tsx`. The landing's
`routing.ts` uses `uk` unprefixed as default, `en` under `/en`, `localePrefix: "as-needed"`.
`frontend/middleware.ts` only routes `/` and `/en*` through the next-intl middleware;
everything else (`(dashboard)`, `(auth)`) keeps its existing session-cookie redirect logic,
untouched.

The rest of the app — `(dashboard)` and `(auth)` route groups, 43 pages, 36
`frontend/features/*` directories — is fully hardcoded Ukrainian, outside the `[locale]`
segment. This plan breaks the remaining rollout into sequential blocks, each sized to be
one `frontend-developer` TASK (or occasionally an explicit two-part TASK where a block is
large).

Volume was measured by counting lines containing Cyrillic characters per file/directory
(ripgrep, not a perfect proxy for "UI strings" but a consistent relative-size signal) —
figures below are feature-file counts / feature-directory line counts, plus the matching
`app/(dashboard)/*` page file where relevant.

## Locale strategy recommendation for the dashboard: cookie/user-preference, not URL-prefixed

**Recommendation: do NOT move `(dashboard)`/`(auth)` under `[locale]`. Use a cookie (or
persisted user preference) to pick the locale, keep URLs unprefixed.**

Reasoning:
- `(dashboard)/layout.tsx` is `"use client"` and 100% CSR already (per CLAUDE.md: "CSR for
  all authenticated/dashboard views") — there is no SSR/SEO requirement pulling it toward
  next-intl's URL-segment routing, which exists mainly to make locale crawlable/shareable
  for public pages. Authenticated users don't need a shareable localized URL for
  `/dashboard`.
- next-intl 4.x (confirmed in `frontend/package.json`, `next-intl: ^4.13.2`) explicitly
  supports "internationalization without i18n routing": `getRequestConfig` can resolve
  `locale` from any source (cookie, user record) instead of from `routing`/middleware. No
  `[locale]` segment, no `generateStaticParams`, no `notFound()` guard needed.
  `NextIntlClientProvider` just needs a `locale` + `messages` bundle; because the dashboard
  is CSR, this can be as simple as a client-side provider fed by a value read from
  `localStorage`/cookie at mount, mirroring how `frontend/features/auth/store.ts` already
  caches `AuthUserDto` in `localStorage` outside React state.
- Moving `(dashboard)` and `(auth)` under `[locale]` would force every one of the 43 pages'
  route paths to shift (`/dashboard` → `/uk/dashboard` or add prefix logic), touch the
  session-redirect logic in `middleware.ts` that currently special-cases landing vs.
  everything-else, and reintroduce `generateStaticParams`/static rendering concerns for
  pages that are deliberately CSR. All cost, no benefit for this audience.
- Tradeoff being accepted: no deep-linkable "share this dashboard page in English" URL.
  Acceptable — nobody shares authenticated CRM URLs across locales; the working precedent
  (theme, sidebar-collapsed state) is already client/localStorage-only, not URL-encoded.

Mechanics for Block 1: add `User.PreferredLocale` (nullable, defaults to `null` = browser
`navigator.language` fallback) → new profile-update endpoint field → stored in
`AuthUserDto`/`sg_user` localStorage cache alongside the existing pattern → a dashboard-only
`i18n/dashboard-request.ts` (or similar) resolves locale client-side and feeds
`NextIntlClientProvider`, independent of the landing's `routing.ts`/`request.ts` (those stay
landing-only, as their own comments already state).

## Block 0 — Landing (done)

`frontend/app/[locale]/*`, `frontend/messages/{uk,en}.json` (`Landing.*` namespace). No
further action.

## Block 1 — Foundation: shared chrome, auth, locale plumbing

**Scope:**
- `frontend/components/ui/*` + `frontend/components/layout/*` (22 files, ~119 Cyrillic
  lines). Heaviest: `components/layout/Sidebar.tsx` (61 — nav-item labels seen on every
  page), `components/layout/SupportChatWidget.tsx` (31), `components/layout/TopBar.tsx` (5),
  `components/layout/UserMenu.tsx` (5), `components/ui/DateRangePicker.tsx` /
  `ReasonModal.tsx` (5 each).
- `frontend/features/auth/*` (6 files, 18 lines) + `frontend/app/(auth)/{layout,login/page}.tsx`
  (2 files, 2 lines) + `frontend/app/(dashboard)/layout.tsx` (1 file, "Завантаження…" state).
- New plumbing (not translation, net-new code): `User.PreferredLocale` field + migration
  (`database-engineer`), profile-update endpoint field (`backend-developer`), dashboard-only
  `NextIntlClientProvider` wiring + a language switcher in Settings/Profile
  (`frontend-developer`).

**Size:** ~31 files to translate + net-new locale-preference plumbing.
**Depends on:** nothing (first real block after landing).
**Why first:** Sidebar/TopBar/UserMenu are visible on literally every dashboard screen —
translating feature content before the shell would leave the frame around it Ukrainian
regardless of which feature block ships next. The locale-persistence mechanism must also
exist before any feature block can be verified in English.

## Block 2 — Core Inventory & Warehouse Ops

**Scope:** `inventory` (8f/151), `shelf` (7f/67), `catalog` (3f/0), `locations` (8f/55),
`stores` (6f/15), `receipts` (4f/5), `transfers` (3f/4), `write-offs` (3f/9) — 42 feature
files, ~306 lines — plus pages `app/(dashboard)/{inventory,inventory/[id],receipts,
receipts/[id],transfers,write-offs,locations,locations/[id]/floor-plan,
locations/[id]/zones/[zoneId]/shelves,floor-plan,stores/[id]/floor-plan,stock}` (12 pages,
~208 lines). Largest single files: `receipts/[id]/page.tsx` (36), `inventory/[id]/page.tsx`
(34), `write-offs/page.tsx` (29), `receipts/page.tsx` (29), `transfers/page.tsx` (27).

**Size:** ~54 files, ~514 Cyrillic lines — the largest "core" block; consider splitting into
2a (Inventory/Shelf/Stock/Catalog) and 2b (Receipts/Transfers/Write-offs/Locations/Stores)
if the frontend-developer agent's context budget is a concern.
**Depends on:** Block 1 (shell + locale plumbing).
**Why this order:** FEFO/stock tracking is the product's core domain (CLAUDE.md: "FEFO is
sacred") — the first thing a bilingual customer demo would show.

## Block 3 — Sales & POS Flow

**Scope:** `sales` (6f/39), `orders` (5f/12), `pos` (10f/61), `ai-orders` (4f/22) — 25
feature files, 134 lines — plus the 5 POS-specific components living inside
`features/analytics/components/` (`PosTopProductsTable`, `PosSummaryCards`,
`PosRevenueTrendChart`, `PosPaymentPieChart`, `PosCashierStatsTable` — 33 lines) — plus pages
`app/(dashboard)/{sales,orders,pos,ai-orders,analytics/pos}` (5 pages, 60 lines).

**Size:** ~35 files, ~227 lines.
**Depends on:** Block 1. Independent of Block 2 (can run in parallel with it if two agents
are available).
**Why grouped:** Sales → Orders → POS is one continuous commercial flow; splitting the POS
analytics components out of `features/analytics/` and into this block avoids a half-English
POS dashboard when Block 4 (general analytics) ships later.

## Block 4 — Analytics & Home Dashboard

**Scope:** remaining `features/analytics/*` components not covered in Block 3 (~9 files,
26 lines: `CategoryStatusChart`, `ExpiryDonut`, `LossesByReasonChart`, `LossesByStoreChart`,
etc.) + `features/dashboard/*` (8f/81, heaviest: `QuickActions.tsx` 67) + pages
`app/(dashboard)/{analytics,dashboard}` (2 pages, 56 lines — `analytics/page.tsx` alone is
54).

**Size:** ~19 files, ~163 lines.
**Depends on:** Block 3 (shares the `analytics` directory — sequencing avoids two agents
editing the same files).

## Block 5 — Vertical Modules: Auto-Service, Production, Customers

**Scope:** `auto-service` (12f/122 — heaviest single feature per-file spread:
`ServiceCatalogTable.tsx` 27, `WorkOrderLineForm.tsx` 19), `production` (8f/92, heaviest
`RecipeForm.tsx` 26), `customers` (6f/43) — 26 feature files, 257 lines — plus pages
`app/(dashboard)/auto-service/*` (4 pages), `production/{recipes,orders,orders/[id]}` (3
pages), `customers` (1 page) — 8 pages, 21 lines.

**Size:** ~34 files, ~278 lines.
**Depends on:** Block 1 only — fully independent of Blocks 2–4, safe to parallelize.
**Why grouped:** these three are the "industry vertical" modules (auto-service, manufacturing,
customer master) that share no code with core retail flow but are each self-contained.

## Block 6 — Marketplace (B2B buyer side)

**Scope:** `marketplace` (24f/219 — largest single feature directory; heaviest:
`SupplierItemExtraFields.tsx` 22, `SupplierProfileForm.tsx` 21, `CooperationRequestModal.tsx`
11) + pages `app/(dashboard)/marketplace/{page,[id],orders}` (3 pages, 71 lines —
`marketplace/orders/page.tsx` alone is 33).

**Size:** ~27 files, ~290 lines.
**Depends on:** Block 1 only.
**Why separate from Block 7:** marketplace (buyer/tenant side) and supplier-cabinet
(supplier side) are two distinct user-facing surfaces of the same B2B feature — big enough
individually to warrant their own blocks rather than one oversized combined block.

## Block 7 — Supplier Cabinet (B2B supplier side)

**Scope:** `supplier-cabinet` (20f/288 — the single largest feature directory by Cyrillic
line count; heaviest: `CooperationRequestsTab.tsx` 44, `TasksBoard.tsx` 29,
`ContractSettingsForm.tsx` 33) + pages `app/(dashboard)/supplier/{items,reviews,profile,
team,tasks,requests,contract-settings,orders,support,messages,clients}` (10 small pages,
~33 lines total).

**Size:** ~30 files, ~321 lines — the single largest block.
**Depends on:** Block 1 only. Independent of Block 6, can run in parallel.

## Block 8 — Provider & Platform Admin

**Scope:** `provider` (25f/275 — second-largest feature dir; heaviest:
`TenantDetailPanel.tsx` 30, `ProviderSupportTab.tsx` 33, `CreateTenantWizard.tsx` 14),
`admin` (6f/65), `modules` (3f/18), `tenant-roles` (6f/36), `legal-entities` (5f/28),
`settings` (5f/29), `integrations` (8f/62) — 58 feature files, 513 lines — plus pages
`app/(dashboard)/{provider,provider/team,admin,settings,settings-user,
settings/legal-entities}` (6 pages, 67 lines).

**Size:** ~64 files, ~580 lines — largest block by file count; recommend splitting into
8a (Provider + Admin, the SaaS-operator surface) and 8b (Settings + Integrations +
TenantRoles + LegalEntities, the tenant-configuration surface) if run as a single TASK would
be too large for one agent pass.
**Depends on:** Block 1. Low priority for the *end customer* bilingual experience (provider
panel is internal/SaaS-ops-facing) but note `settings`/`legal-entities`/`integrations` are
tenant-facing and could be pulled earlier if customer demand requires it before Block 8's
provider-panel portion.

## Block 9 — People Ops: Users, Schedules, Profile, Notifications

**Scope:** `users` (8f/114, heaviest `UserPermissionsEditor.tsx` 32, `InviteUserModal.tsx`
26), `schedules` (9f/70), `profile` (8f/102, heaviest `TwoFactorSection.tsx` 39 — note:
`profile`'s `ProfileTab.tsx` container itself was already touched in Block 1 for the
language-switcher UI; the remaining profile sub-components ship here), `notifications`
(7f/80) — 32 feature files, 366 lines — plus pages `users`, `schedules`, `notifications`
(3 pages, 21 lines).

**Size:** ~35 files, ~387 lines.
**Depends on:** Block 1 (and lightly on Block 1's `ProfileTab.tsx` edit for the language
switcher, to avoid merge conflicts on the same file).

## Block 10 — Support, Chat & Misc

**Scope:** `service-desk` (12f/57), `chat` (5f/27), `ai-assistant` (4f/13), `events`
(5f/36), `iot` (6f/37) — 32 feature files, 170 lines — plus pages `service-desk`, `events`,
`iot` (3 pages, 28 lines).

**Size:** ~35 files, ~198 lines.
**Depends on:** Block 1 only. Lowest priority by usage frequency — safe to ship last or
interleave for parallelization slack.

---

## Block 11 (future, not this wave) — Non-UI text sources

These are real translation debt but explicitly deferred — they don't block a bilingual
dashboard UI and each needs its own design pass (message catalogs, per-tenant locale, etc.)
rather than a mechanical component swap:

| Source | Volume | Notes |
|---|---|---|
| Backend `Application` layer error/validation strings | 92 Cyrillic string literals across 16 files (heaviest: `SupplierAgreementService.cs` 25, `MarketplaceOrderService.cs` 13, `SupplierSupportService.cs` 9, `EventService.cs` 11) | Confirmed hardcoded UA sentences returned directly as `{ error }` in API responses (e.g. `UsersController.cs`: `BadRequest(new { error })`, `UserService.cs:143`: `"Вказана юридична особа не належить цьому тенанту."`) — **not** error codes the frontend maps to text. Real backend work: introduce error-code contract + frontend translation, or a `Accept-Language`-aware message resolver. Non-trivial, its own ADR. |
| `worker/src/jobs/*` (notification dispatch, telegram listener, permission-grant-expiry) | 19 Cyrillic literals across 4 files (`notification-dispatch.job.ts` 3, `notification.job.ts` 6, `permission-grant-expiry.job.ts` 9, `telegram-listener.ts` 1) | Telegram bot commands (`/status`, `/critical`, `/tasks`) and push/telegram notification bodies. Needs the same user-locale field from Block 1 to pick a template per recipient. |
| Checkbox ПРРО fiscal receipts (`backend/ShelfGuard.Infrastructure/Integrations/Prro/CheckboxFiscalClient.cs`) | Only 2 Cyrillic literals found (likely log/comment text, not receipt line content) | Fiscal receipt content itself is mostly user-entered product/price data passed through to Checkbox, not hardcoded UI strings — low risk, but worth a dedicated check when this block is picked up, since fiscal receipts are a legal document and may have jurisdiction-specific requirements independent of UI locale. |
| Email templates (`worker/src/services/email.ts`) | 0 Cyrillic lines found | Per `external-services-status` memory, Resend is in an unknown/blocked state (domain not verified) — templates may be minimal/unbuilt rather than already English. Re-check when Resend is actually wired up. |

## Mobile app (Expo) — explicitly out of scope for this wave

Per the user's framing, mobile stays untranslated for now. Flagging for the record: Expo/React
Native cannot reuse `next-intl` (Next.js-specific) — mobile i18n would need a separate
library (`i18next`/`react-i18next` + `expo-localization`, or similar), a separate message
catalog, and its own rollout plan across the 15 `mobile/features/*` directories. Treat as an
independent future initiative, not a Block 12 of this plan.
