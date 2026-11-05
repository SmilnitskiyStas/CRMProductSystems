# TASK-772 — UI standard migration, batch C (cross-cutting pages / raw tables)

Plan: `.claude/docs/ui-standard-migration.md` (batch C). Base: PR #24 kit merged into this branch.

## Changed
- PageContainer + PageHeader (+ BackLink) on: customers/[id], inventory/new, inventory/[id], inventory/[id]/edit, receipts/[id] (h1 18→22), settings-user (profile card h1 18→22, root → PageContainer), marketplace/[id] (h1 20→22, tabs → `Tabs`, add-item → `Btn`), marketplace/[id]/metrics, locations/[id]/staff (local inputStyle → `filterInputStyle`), locations/[id]/zones/[zoneId]/shelves, locations/[id]/floor-plan, stores/[id]/floor-plan (redirect stub).
- Action buttons → `Btn` (receipts confirm/cancel, shelves scan/save/add/resize, floor-plan save/toggle, StatsTab period chips).
- Raw `<table>` → `ui/Table`: StatsTab (register+operator rows flattened, no totals row existed), ItemBatchesSection, PosTopProductsTable, CabinetOrdersTab + marketplace/orders (nested item/receipt sub-tables, dropped local th/td consts), customers/[id] receipt lines.
- Kept: receipts/[id] compact in-table qty/date inputs (local compact input style), icon-only/row-click buttons.

## Verification
`npx tsc --noEmit` and `npm run lint` clean. Browser (dev server, seeded ea@demo.local): all pages above render, single 28px 32px padding, h1 22px, no new console errors (only unrelated 404 resource). CabinetOrdersTab (supplier login) verified by tsc only.

## Notes
- ESLint overrides not extended (batches A/B still migrating the same dirs).
- Dev CORS only allows :3000/:3001 → used a scratch CORS proxy; nothing committed.
