# Handoff: TASK-336 backend-developer → TASK-337 frontend-developer (period comparison UI)

**From:** backend-developer · **Date:** 2026-07-12
**Backend state:** merged-ready on `main` working tree. `dotnet build` 0 errors, analytics tests 15/15 pass. Worker `tsc --noEmit` clean. No frontend files touched.

All endpoints below are on `AnalyticsController` (`api/analytics/...`), guarded by `[Authorize(Policy = AppPolicies.CanViewAnalytics)]` — same auth as every other analytics call already in `frontend/features/analytics`.

## 1. Weekly KPI (new)

`GET /api/analytics/dashboard/weekly-kpi?store_id={guid?}`

- `store_id` optional (omit for network-wide across all tenant stores).

Response:
```json
{
  "sales":        { "current": 120, "previous": 98, "percentChange": 22.45 },
  "revenue":      { "current": 45230.50, "previous": 39012.00, "percentChange": 15.94 },
  "writeOffLoss": { "current": 1200.00, "previous": 1500.00, "percentChange": -20.0 }
}
```
- `sales.current`/`previous` = POS transaction count for the period (not `DailySales` manual entries).
- `revenue` = POS `TotalRevenue`.
- `writeOffLoss` = approved write-offs' `TotalLoss`.
- Windows: current = last 7 days including today (`today-6..today`), previous = the 7 days before that (`today-13..today-7`).
- `percentChange` is `null` when `previous == 0` (no baseline — render neutral/no-arrow state, not `+∞%`).

## 2. Expiry summary comparison (new)

`GET /api/analytics/expiry-summary/compare?storeId={guid?}&compareWeeksAgo={int=1}`

- Note: this endpoint uses **camelCase** `storeId` (not `store_id` like the rest of the controller) — ASP.NET binding is case-insensitive to query key casing but match the case in the brief for clarity when writing the client.
- `compareWeeksAgo` defaults to `1`, clamped to `>= 1` server-side.

Response:
```json
{
  "current": {
    "safe": 340, "warning": 20, "critical": 5, "expired": 2,
    "needsVerification": 3, "total": 370,
    "stores": [ { "storeId": "...", "storeName": "...", "safe": 340, "warning": 20, "critical": 5, "expired": 2 } ]
  },
  "previous": {
    "safe": 355, "warning": 15, "critical": 3, "expired": 1,
    "needsVerification": 0, "total": 374,
    "stores": [ { "storeId": "...", "storeName": "...", "safe": 355, "warning": 15, "critical": 3, "expired": 1 } ]
  }
}
```
- `previous` is **`null`** if no snapshot exists for that date (first 7 days after deploy, or the worker hasn't run yet for that day) — frontend must handle a missing "previous" card gracefully (e.g. hide the delta, don't crash).
- `previous.needsVerification` is always `0` — that status isn't tracked by the snapshot table. Don't compute a delta on it.
- Snapshot data only exists per-tenant, so if the caller has no `tenant_id` (provider cross-tenant view), `previous` is always `null`.
- The worker (`stock-snapshot` cron, 00:10 daily) needs at least one full day to populate a snapshot after this deploys — don't expect real `previous` data until tomorrow.

## 3. Generic compare on 4 existing endpoints

All four now accept: `compare` (bool, default `false`), `compareFrom` (date, optional), `compareTo` (date, optional).

**Backward compatibility: if `compare` is omitted or `false`, the response shape is UNCHANGED (old flat DTO) — existing frontend code keeps working as-is.** Comparison mode is strictly opt-in via `?compare=true`.

If `compare=true` and `compareFrom`/`compareTo` are omitted, the backend auto-derives the immediately-preceding period of the same length (e.g. `from=2026-07-06&to=2026-07-12` → auto compare `2026-06-29..2026-07-05`).

### `GET /api/analytics/write-offs`
`?store_id={guid?}&from={date?}&to={date?}&compare=true&compareFrom={date?}&compareTo={date?}`
- Note: unlike `pos/*`, `from`/`to` are unbounded (null = all-time) when `compare=false` (unchanged). When `compare=true`, `from`/`to` are resolved to a concrete range first (default last 30 days if omitted) before computing the comparison window — "all time" has no defined "previous period."

```json
{
  "current":               { "totalDocuments": 12, "totalLoss": 1200.00, "byReason": [...], "byDate": [...] },
  "comparison":             { "totalDocuments": 15, "totalLoss": 1500.00, "byReason": [...], "byDate": [...] },
  "totalLossPercentChange": -20.0
}
```

### `GET /api/analytics/losses`
Same query params as `write-offs`. Response:
```json
{
  "current":                { "totalLoss": 1200.00, "totalWriteOffs": 12, "averageLossPerWriteOff": 100.0, "byStore": [...] },
  "comparison":              { "totalLoss": 1500.00, "totalWriteOffs": 15, "averageLossPerWriteOff": 100.0, "byStore": [...] },
  "totalLossPercentChange":  -20.0
}
```

### `GET /api/analytics/pos/summary`
`?store_id={guid?}&from={date?}&to={date?}&compare=true&compareFrom={date?}&compareTo={date?}`
- `from`/`to` already default to last 30 days when omitted (unchanged from before).
```json
{
  "current":                       { "totalRevenue": 45230.5, "transactionCount": 120, "averageTicket": 376.9, "cashRevenue": 20000, "cardRevenue": 25230.5, "shiftCount": 14, "from": "2026-07-06", "to": "2026-07-12" },
  "comparison":                     { "totalRevenue": 39012.0, "transactionCount": 98, "averageTicket": 398.1, "cashRevenue": 18000, "cardRevenue": 21012.0, "shiftCount": 13, "from": "2026-06-29", "to": "2026-07-05" },
  "revenuePercentChange":           15.94,
  "transactionCountPercentChange":  22.45
}
```

### `GET /api/analytics/pos/revenue-trend`
`?store_id={guid?}&from={date?}&to={date?}&group_by={day|week}&compare=true&compareFrom={date?}&compareTo={date?}`
```json
{
  "current":     [ { "date": "2026-07-06", "revenue": 6000.0, "transactions": 15 }, ... ],
  "comparison":   [ { "date": "2026-06-29", "revenue": 5200.0, "transactions": 12 }, ... ],
  "groupBy":     "day",
  "from":        "2026-07-06",
  "to":          "2026-07-12",
  "compareFrom": "2026-06-29",
  "compareTo":   "2026-07-05"
}
```
- **Important:** both `current` and `comparison` point arrays are **sparse** — a day/week with zero transactions has no entry (matches the existing non-compare endpoint's behavior, unchanged). Don't assume equal array length or zip by index. To overlay two line charts, compute each point's offset as `point.date - from` (or `point.date - compareFrom` for the comparison series) in days, then align by that offset — not by array position.
- `percentChange` is `null` whenever the baseline (`previous`/`comparison` value) is `0` — treat as "no comparable baseline," not a literal 0% or infinite change.

## Types reference (for the TS client)

```ts
interface PeriodMetric { current: number; previous: number; percentChange: number | null; }
interface WeeklyKpi { sales: PeriodMetric; revenue: PeriodMetric; writeOffLoss: PeriodMetric; }
```

## Not done / out of scope for TASK-336
- No frontend changes at all — dashboard cards, charts, and the comparison toggle UI are 100% open for TASK-337.
- No new unit tests for the new `AnalyticsService` comparison methods (existing 15 analytics tests still pass unmodified; the new methods are thin delegation + arithmetic — low risk, but flag to qa-tester if coverage matters before this ships).
- `PosRevenueTrendComparisonDto` intentionally doesn't zero-fill missing days — see note above under `pos/revenue-trend`.
