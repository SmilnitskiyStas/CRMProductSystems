# TASK-073 (frontend half) — POS Analytics Web Dashboard

**Agent:** frontend-developer  
**Date:** 2026-06-14  
**Status:** done

## What was built

### New files in `frontend/features/analytics/`

| File | Description |
|---|---|
| `api/pos-analytics.ts` | `posAnalyticsApi` — 4 fetch functions for POS endpoints |
| `hooks/usePosAnalytics.ts` | React Query hooks: `usePosSummary`, `usePosRevenueTrend`, `usePosTopProducts`, `usePosCashiers` |
| `components/PosSummaryCards.tsx` | 4 KPI cards: Виручка / Транзакції / Середній чек / Зміни |
| `components/PosRevenueTrendChart.tsx` | recharts AreaChart — revenue by day/week |
| `components/PosTopProductsTable.tsx` | Top-10 products table with hover highlight |
| `components/PosCashierStatsTable.tsx` | Cashier stats table (revenue, tickets, avg ticket, shifts) |
| `components/PosPaymentPieChart.tsx` | recharts PieChart — Cash vs Card split with % breakdown |

### Updated files

| File | Change |
|---|---|
| `features/analytics/types.ts` | Added 6 POS DTO interfaces: `PosAnalyticsSummaryDto`, `PosRevenueTrendDto`, `PosRevenueTrendPoint`, `PosTopProductsDto`, `PosTopProductItem`, `PosCashierStatsDto`, `PosCashierStat` |
| `components/layout/Sidebar.tsx` | Added `BarChart3` import + `POS Аналітика` nav item (`/analytics/pos`, `CAN_VIEW_ANALYTICS`); added `exact` flag to prevent `/analytics` being active on `/analytics/pos` |

### New page

`frontend/app/(dashboard)/analytics/pos/page.tsx`
- Date range filter (default: last 30 days) with `<input type="date">`
- Store selector via `GET /api/stores`
- Day/Week toggle for revenue trend grouping
- Layout: KPI cards → Revenue trend → [Top products | Cashiers] side by side → Payment pie (shown only when revenue > 0)
- Role guard: `CAN_VIEW_ANALYTICS` (same as existing analytics page)

## TypeScript

`npx tsc --noEmit` — clean (0 errors)

## Patterns followed

- Same dark theme tokens as existing analytics page (`#161B26 / #0D1117 / #1F2937 / #E8EDF5`)
- `"use client"` on all interactive components
- React Query for all server state (no Zustand)
- Feature-based structure: all files inside `frontend/features/analytics/`
- No inline duplicated types — all in `types.ts`
