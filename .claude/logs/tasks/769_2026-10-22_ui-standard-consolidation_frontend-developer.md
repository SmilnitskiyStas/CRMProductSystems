# TASK-769 UI standard consolidation (frontend)

- Btn extended: variants dangerOutline/link, size xs, props title/aria-label/className/fullWidth/form, onClick receives the event (old `() => void` callers still compile).
- New ui/: PageContainer, PageHeader, BackLink, StatusPill, Tabs, Field, fieldStyles.ts. ReasonModal got Esc-to-close + Modal backdrop/panel colours + shared field styles.
- Applied to production + procurement-orders block (all pages -> PageContainer/PageHeader, detail pages BackLink, tabs -> Tabs, badges -> StatusPill, local field styles -> fieldStyles, nested raw tables -> ui/Table, hand-rolled dialogs -> Modal).
- Left on purpose: 2 print-sheet `<table>` (ProductionShipments, OrderCard; styled by @media print CSS, eslint-disabled), card/row-click and picker-row raw buttons, module-gate placeholder screens.
- Guard: `.eslintrc.json` overrides (no-restricted-syntax: raw table, position:fixed) for the block. Docs: frontend-structure.md "UI standard", CLAUDE.md ui/ line.
- Verify: `npx tsc --noEmit` and `npm run lint` clean.
