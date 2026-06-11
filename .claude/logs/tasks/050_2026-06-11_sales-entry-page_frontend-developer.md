---
task_id: TASK-050
date: 2026-06-11
agent: frontend-developer
status: done
---

# TASK-050 — Web: sales entry page (/sales)

## Files
```
app/(dashboard)/sales/page.tsx              — page: filters (store, date range), modals
features/sales/
  types.ts                                  — DailySale, UpsertDailySalePayload, CsvImportResult
  api/sales.ts                              — getAll, upsert, importCsv (multipart), markAnomaly
  hooks/useSales.ts                         — useDailySales, useUpsertSale, useImportCsv, useMarkAnomaly
  components/SalesTable.tsx                 — table + promo/anomaly badges + anomaly toggle
  components/SaleEntryForm.tsx              — zod+react-hook-form modal (store, product, date, qty, EOD, promo)
  components/CsvImportDialog.tsx            — file upload + per-row error report
lib/api.ts                                  — + postForm; apiFetch now skips Content-Type for FormData
components/layout/Sidebar.tsx               — + "Продажі" item (AT_LEAST_STORE_MANAGER, TrendingUp icon)
```

## UX decisions
- Default range: last 30 days (matches the tightest ADU window)
- Anomaly rows render at 45% opacity with red badge; one-click toggle with explanatory tooltip
- CSV dialog shows created/updated/skipped + scrollable per-line error list from the API
- Date inputs capped at today (server rejects future dates anyway)

## Rules followed
React Query for all server state ✓ · zod + react-hook-form ✓ · feature-based structure ✓ ·
"use client" only where needed ✓ · reused Modal/Btn + existing useStores/useProducts hooks ✓

## Incident during work
A PowerShell regex replace mangled Ukrainian text (UTF-8 mojibake) in two components —
rewritten cleanly via Write tool. Lesson: never edit source files with
Get-Content/-replace/Set-Content; use proper editing tools.

## Verification
- `tsc --noEmit` clean, `npm run build` passes
- Deployed: GET /sales → 200 on production web container

## Sprint v2.1 — COMPLETE
TASK-046 ✅ 047 ✅ 048 ✅ 049 ✅ 050 ✅ → next: Phase 2 (Buffer & Formula, TASK-051..053)
