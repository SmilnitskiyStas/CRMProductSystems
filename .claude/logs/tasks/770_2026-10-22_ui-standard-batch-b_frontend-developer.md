# TASK-770 — UI standard migration, batch B

Branch claude/amazing-keller-e879ca (fast-forwarded onto the PR #24 kit branch).

Migrated to the shared kit: marketplace, auto-service, customers, customer-support, service-desk, notifications, chat, users, integrations, inventory, locations, stores, legal-entities, schedules, dashboard, shelf (not ItemBatchesSection), settings, ai-assistant, marketing-analytics, promotion-rules, components/layout (not Sidebar) + their route pages (cross-cutting batch C pages untouched).
- Modals -> ui/Modal, drawers -> ui/DetailDrawer, pagination -> ui/Pagination, raw tables -> ui/Table, badges -> StatusPill, tabs -> Tabs, style consts -> fieldStyles, shells -> PageContainer/PageHeader/BackLink.
- Fixed auto-service/work-orders/[id] crash (`use(params)` -> `useParams`).
- `.eslintrc.json` overrides extended (raw table / position:fixed = error) for the migrated globs.

Intentional exceptions: matrices (StoreMigrationMatrix, TransitionMatrix, ColumnPreviewPicker, WeekGrid); floating widgets/popovers (SupplierChatPanel, SupportTicketsPanel, cart pill, SupportChatWidget, CategorySelect dropdown); icon-only/chip/toggle/row-click buttons stay raw.
Modal/DetailDrawer take string titles only: icons/avatars moved into body.

Status: `tsc --noEmit` clean, `npm run lint` clean, dev server compiles all touched routes (HTTP 200, no compile errors). NOT verified: visual browser spot-check (browser navigation to localhost was denied, no auth session).
