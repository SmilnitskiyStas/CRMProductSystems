# TASK-770 — UI standard, batch A

Scope: features/{supplier-cabinet,provider,admin,tenant-roles,consumer-app} + app/(dashboard)/{supplier,provider,admin,consumer-app}.
Done: pages -> PageContainer+PageHeader (route padding wrappers removed); modals -> ui/Modal, ConsumerApp analytics overlay -> DetailDrawer; Pagination (provider logs/leads, supplier reviews); tables -> ui/Table; action buttons -> Btn; tabs -> Tabs; badges -> StatusPill; local input/label consts -> fieldStyles.
Left raw (documented): icon-only/row-click/chip buttons, inline sticky TenantDetailPanel/TenantDetailDrawer, ImpersonationBanner + SupplierClientChatPanel overlays, SupplierWeekGrid matrix, compact table in CabinetOrdersTab (eslint-disable-next-line with reason).
ESLint overrides extended with all batch-A globs.
Checks: tsc --noEmit clean; eslint 0 errors (7 pre-existing warnings). Browser: /supplier/orders|inventory|schedules show a single 28px 32px container; provider/admin/consumer-app pages redirect for the supplier test account, so not visually checked.
Note: unused i18n keys prevButton/nextButton/prev/next in provider (Pagination uses Common.*).
