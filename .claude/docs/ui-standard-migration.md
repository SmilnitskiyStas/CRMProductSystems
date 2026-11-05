# UI standard migration plan (rest of the app)

Source of truth for the standard: `.claude/docs/frontend-structure.md` → "UI standard (single source of truth)".
Shared kit: `frontend/components/ui` — `Btn` (variants primary/danger/success/ghost/dangerOutline/link, sizes md/sm/xs), `PageContainer`, `PageHeader`, `BackLink`, `Tabs`, `StatusPill`, `Field` + `fieldStyles.ts`, `Table`, `Modal`, `Pagination`, `ActionMenu`, `DetailDrawer`, `ReasonModal`.
Already fully migrated + guarded by ESLint (`frontend/.eslintrc.json` overrides): production and procurement-orders block
(`app/(dashboard)/production/**`, `features/production/**`, `app/(dashboard)/orders/**`, `features/orders/**`, `features/order-tracking/**`).
Migrate other areas the same way and ADD their globs to the ESLint overrides when an area is done.

Audit (2026-10-22, counts ±10%): ~500 raw `<button>` in ~208 files (≈140 action buttons that should be `Btn`, 19 DIY pagination,
12 back links, ≈134 tabs/chips, ≈131 icon-only, ≈68 row/card click targets — the last four groups stay raw but must use standard colours);
21 real raw `<table>` files; 54 hand-rolled `position:fixed` modal files + 7 hand-rolled drawers; ≈70–80 off-standard pills;
≈86 off-standard input/select style blocks in ≈80 files (≈100 files define their own inputStyle/labelStyle).

## What to convert (rules)
- Action-like `<button>` (primary/secondary/danger/cancel/submit, inline-styled) → `Btn` (blue solid/other → `primary`; delete → `danger`/`dangerOutline`; text actions → `link`; cancel → `ghost`). Disabled state via `disabled`.
- Tabs/segmented → `Tabs`; filter chips may stay custom but use standard colours (active = primary tokens).
- Hand-rolled modal (fixed backdrop) → `ui/Modal` (+ `Btn` footer: ghost cancel, primary submit); right drawers → `ui/DetailDrawer`; keep special overlays (floating chat widget, popovers, banners).
- DIY prev/next pagination → `ui/Pagination`.
- Raw `<table>` → `ui/Table` (keep matrices/grids: TransitionMatrix, StoreMigrationMatrix, ColumnPreviewPicker, WeekGrid, SupplierWeekGrid; compact embedded drawer item tables may stay).
- Page shell → `PageContainer` + `PageHeader` (+ `BackLink` on detail pages); h1 22/700, subtitle 13px #4B5563; remove route-level padding wrappers (avoid double padding!).
- Status/source badges → `StatusPill` (keep colour maps); inputs/selects/labels → `fieldStyles.ts` (toolbar selects use `filterInputStyle`).
- Behavior, hooks, API, translation keys must NOT change. Verify in the browser: single 28px 32px padding, gap 20, no console errors.

## Batches (by directory; do not run two sessions on the same directory)
A) supplier-cabinet (12 modal files, 6 button files, pagination x2), provider (9 modal files, 7 button files), admin, tenant-roles, consumer-app (minified one-liners + hard-coded Ukrainian strings — hand-edit carefully, do not reformat), plus `app/(dashboard)/supplier/**`, `provider/**`, `admin/**`, `consumer-app/**` pages.
B) marketplace (7 modals, 8 button files, pagination), auto-service (5 modals, 9 button files; solid #1D4ED8 buttons → primary), customers, customer-support, service-desk, notifications, chat, users, integrations, inventory, locations/stores (FloorPlanSidePanel duplicate), legal-entities, schedules, dashboard, shelf, settings, ai-assistant, marketing-analytics, promotion-rules, layout, plus their `app/(dashboard)` pages. NOTE: `app/(dashboard)/auto-service/work-orders/[id]/page.tsx` uses Next-15 `use(params)` on Next 14 and crashes — switch to `useParams`.
C) Cross-cutting pages/headers not covered above: `app/(dashboard)/customers/[id]`, `inventory/new|[id]/edit|[id]`, `receipts/[id]`, `settings-user`, `marketplace/[id]` (+metrics), `locations/[id]/**`, `receipts/[id]` back links/h1 (h1 sizes 18/20 → 22), raw tables in `registers/StatsTab`, `shelf/ItemBatchesSection`, `analytics/PosTopProductsTable`, `supplier-cabinet/CabinetOrdersTab`, `marketplace/orders/page`, `customers/[id]/page`.

Done criteria per batch: `npx tsc --noEmit` + `npm run lint` clean; extend `.eslintrc.json` overrides to the migrated globs (raw `<table>` and `position:'fixed'` as errors); browser spot-check of every touched page.
