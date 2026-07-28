# TASK-431: Security review — Фаза 3 AudienceBuilder

**Agent:** security-reviewer
**Date:** 2026-07-27
**Status:** done — **verdict: CLEAR TO SHIP**, no blocker, no risk-level finding · all 7 mandatory
checklist items verdict **OK** · read-only audit, no code changed

## Context

Read TASK-428 (database-engineer)/TASK-429 (backend-developer)/TASK-430 (frontend-developer) logs
first, then the code directly (didn't trust the logs' own security claims). Focus: `backend/ShelfGuard.Infrastructure/Data/Repositories/AudienceBuilderRepository.cs`
(third raw-SQL repository, first one where free-text user input — not just GUID/date/enum values —
reaches SQL), `AudienceBuilderController.cs`, `AudienceBuilderService.cs`, `AudienceBuilderSortKeys.cs`,
`IAudienceBuilderRepository.cs`, `AudienceBuilderDtos.cs`. Cross-checked against precedent
(`PriceSegmentsRepository.cs`, cleared in TASK-422) and shared infra (`ExcelExportService.cs`,
`MarketingAnalyticsAuthorization.cs`, `PiiMasking.cs`). `dotnet build ShelfGuard.sln` reconfirmed
clean (0 warnings/0 errors) before writing this — no code touched, so the 1213/1213 `dotnet test`
figure already reported in TASK-429's log stands.

## Verdict table

### 1. Parameterization of raw SQL / UNNEST search-term arrays — **OK**

Traced every one of the 7 raw-SQL methods (`SearchCategoriesAsync`, `GetOverviewAsync`,
`GetBuyersAsync`, `GetBuyerReceiptsAsync`, `GetMatchedItemsAsync`, `GetCompetitorOverviewAsync`,
`GetCompetitorBuyersAsync`). Every user-controllable value — free-text search terms, category ids,
excluded-item ids, store ids, dates, thresholds — is bound as a genuine positional `SqlQueryRaw`
parameter (`{0}`, `{1}`, ...), never string-concatenated or interpolated into the SQL text. The
free-text terms specifically travel as a typed C# `string[]` (`ResolvedAudienceQuery.TextTermValues`)
bound to `{2}::text[]`, consumed server-side via `UNNEST({1}::int[], {2}::text[]) AS tt(term_index,
value)` — the `'%' || tt.value || '%'` concatenation building the `ILIKE` pattern happens **inside
Postgres** against the already-parameter-bound value, not in C# against the query string. No
call site builds the `sql`/`sqlTemplate` variable via `$"...{term}..."` or `string.Concat`/`+` with
a user value; the only `.Replace(...)` calls target `{SORT_COLUMN}`/`{SORT_DIRECTION}` tokens, which
are restricted to hardcoded literals (see #2). `SearchCategoriesAsync`'s `term` (the raw
`search` query param) is likewise bound as `{1}`, never spliced into the `WHERE` clause.
No test submits a literal SQL metacharacter (`'`, `;`, `--`) as a search term, so there's no
executable proof beyond code review — but the mechanism (EF Core's `SqlQueryRaw` positional
parameters going through Npgsql's typed array binding) makes injection structurally impossible
regardless of what characters are inside the bound values.
**Recommendation:** none blocking. Optional, low-priority: add one adversarial-input integration
test (a term like `Молоко'; DROP TABLE items; --`) to `AudienceBuilderRepositoryIntegrationTests.cs`
purely as a documented regression guard against a future accidental refactor toward string
concatenation — not needed to trust the current code, since the parameterization is unambiguous.

### 2. `sortBy` allowlist for the new server-side pagination — **OK**

`AudienceBuilderSortKeys` (`AudienceBuilderSortKeys.cs`) defines 2 fixed `HashSet<string>`s
(`BuyerKeys = ["name","qty","receipts","amount"]`, `MatchedItemKeys =
["name","sold","receipts","buyers"]`, the latter reused for competitor buyers) and normalizes any
input to one of those literals or a hardcoded default — same shape as `PriceSegmentSortKeys`
(cleared in TASK-422). The repository's `BuyerSortColumn`/`MatchedItemSortColumn`/
`CompetitorBuyerSortColumn` switch that already-normalized key to a **second, independent** hardcoded
SQL-column-name literal (`"name"`, `"receipt_count"`, `"total_amount"`, `"total_qty"`, etc. —
never the raw string itself) before it's substituted into `{SORT_COLUMN}`. `Direction(bool
descending)` similarly only ever returns the literal `"ASC"`/`"DESC"`. The raw `sortBy` string a
caller sends can therefore never reach the SQL text in any form — it only ever selects among a
closed, hardcoded set of column-name literals. Unrecognized/malicious values silently fall back to
the table's default rather than erroring, consistent with the rest of this codebase's convention.

### 3. Explicit `::timestamptz` casts on date-range parameters — **OK, applied consistently**

Checked every `t."CreatedAt"` comparison across all 6 query methods that touch `pos_transactions`
(`GetOverviewAsync`, `GetBuyersAsync`, `GetBuyerReceiptsAsync`, `GetMatchedItemsAsync`'s `sales` CTE,
`GetCompetitorOverviewAsync`'s `competitor_period_lines` **and** `own_buyers_to_exclude` CTEs,
`GetCompetitorBuyersAsync`'s same two CTEs) — every single occurrence casts both bounds explicitly:
`t."CreatedAt" >= {n}::timestamptz AND t."CreatedAt" <= {n+1}::timestamptz`. No exception found; the
backend-developer's claim in the TASK-429 log ("done even though the C# side already converts…")
holds up under direct inspection, applied uniformly, not just in the first/most-obvious query.
Confirmed the C# side also does its part correctly: `AudienceBuilderService.ToUtcRange` produces
`DateTime` with `DateTimeKind.Utc` (mirrors `AnalyticsRepository`/`PriceSegmentsRepository`'s own
helper), so both layers reinforce each other rather than relying on either alone.

### 4. PII masking from day 0 in `buyers`/`competitor/buyers` — **OK, genuinely day-0, not post-hoc**

- `AudienceBuilderService.MaskPhoneUnlessAuthorized` calls the **same shared**
  `PiiMasking.MaskPhone` (`Features/MarketingAnalytics/PiiMasking.cs`) already used by
  `PriceSegmentsService`'s own identically-named private wrapper — not a second, forked copy of the
  masking rule.
- Applied at DTO-mapping time in both `GetBuyersAsync` and `GetCompetitorBuyersAsync`, and again in
  both `BuildBuyersExcel`/`BuildCompetitorExcel` (the export path uses `PiiMasking.MaskPhone`
  directly, gated on `unmaskPii`).
- `CanViewUnmaskedPii`/`UnmaskPii` are **never** taken from client input on the read paths: the
  controller's `GetBuyers`/`GetCompetitorBuyers` actions unconditionally overwrite whatever the
  client sent with `request with { CanViewUnmaskedPii = MarketingAnalyticsAuthorization.CanExportPii(User) }`.
  On the export paths the client's `UnmaskPii` flag is **ANDed** with the same capability check
  (`request.UnmaskPii && MarketingAnalyticsAuthorization.CanExportPii(User)`) — a caller without the
  capability requesting unmask silently gets masked data, never a 403 (matches the documented
  contract) and never an actual unmask.
- `AudienceOverviewDto`/`MatchedItemRowDto` carry no phone field at all — zero exposure surface on
  those two endpoints regardless of masking logic.
- `MarketingAnalyticsAuthorization.CanExportPii` itself is the identical shared helper (store_manager+
  role OR `marketing_analytics.export_pii` capability) used by Фаза 1/2 — not reimplemented.

### 5. Capability gate reused literally on `AudienceBuilderController` — **OK**

Confirmed via direct grep comparison: `AudienceBuilderController` carries
`[Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)]` +
`[RequireModule("marketing_analytics")]` at class level — byte-for-byte the same two attributes as
`PriceSegmentsController` and `MarketingAnalyticsController`. No new `TenantRoleCapabilities` entry,
no new module key, no new `AppPolicies` policy was introduced for this feature. `tenantId`/`userId`
are resolved exclusively from JWT claims (`User.FindFirst("tenant_id")`,
`ClaimTypes.NameIdentifier`/`"sub"`) in every action, never from the request body — every action
`Forbid()`s cleanly when either claim is missing/malformed, before any service call.

### 6. Excel export goes through the protected `ExcelExportService` — **OK, no new path**

`AudienceBuilderService.BuildBuyersExcel`/`BuildCompetitorExcel` both call the injected
`IExcelExportService.Export(new ExcelExportRequest(...))` — the same `ExcelExportService`
(`Infrastructure/Export/ExcelExportService.cs`, ClosedXML-backed) every other MarketingAnalytics
export uses. Confirmed no direct `ClosedXML`/`XLWorkbook` usage anywhere in
`AudienceBuilderService.cs` (only `Application.Common`/`Domain.Entities`/`Domain.Interfaces` usings)
— there is no second, unguarded export path. `SetCellValue`/`SanitizeForSpreadsheet` (TASK-414's
formula-injection fix: leading `=`/`+`/`-`/`@`/Tab/CR gets apostrophe-prefixed via
`IXLCell.Style.IncludeQuotePrefix`) therefore applies uniformly to every string this feature writes
into a cell — customer names, phone strings, receipt numbers, store names — with nothing to
duplicate or risk drifting out of sync.

### 7. Documented "Seq Scan accepted, no LEAKPROOF/SECURITY DEFINER" decision — **OK, explicit, not silent**

The rationale is not just present but present redundantly, at 3 levels: (a) a full class-level XML
doc comment on `IAudienceBuilderRepository` (`IAudienceBuilderRepository.cs`) with an explicit
"IMPORTANT — read before touching any `i."Name" ILIKE '%...%'` predicate" header; (b) the same
explanation repeated atop the concrete `AudienceBuilderRepository` class; (c) an inline comment on
`SearchCategoriesAsync`'s own categories-`ILIKE` query pointing back to the same tradeoff. All three
cite the actual TASK-428 live measurement (Seq Scan ~1085ms vs Bitmap Index Scan ~2ms, same index/
data) and name the two possible future fixes (mark `texticlike` `LEAKPROOF`, or a `SECURITY DEFINER`
search function) as **out-of-scope, needing their own dedicated security review** — this is the
correct posture, not a unilateral decision.
**Independently verified this is a performance-only tradeoff, not a tenant-isolation bypass**: the
TASK-428 log's own `EXPLAIN ANALYZE` output shows the RLS predicate is still applied as a `Filter`
(`(current_setting('app.role')='worker') OR (TenantId = ...) OR ... AND (Name ILIKE ...)`) — the
non-leakproof `ILIKE` only blocks the **index** path, it does not disable or bypass the tenant-scoping
predicate itself. Every AudienceBuilder CTE additionally carries its own explicit, redundant
`i."TenantId" = {0}` / `t."TenantId" = {0}` application-level filter on top of whatever RLS does —
defense-in-depth, consistent with existing repository convention. Correctness/isolation is intact;
only query latency at large multi-tenant catalog sizes is the accepted cost, exactly as documented.

## Additional observations (non-blocking, beyond the 7-item checklist)

- **Cross-tenant IDOR via `ExcludedItemIds`/`CategoryTermIds`/`StoreIds` — not exploitable.** All
  three arrays are only ever used to filter/exclude *within* a CTE that is already scoped to
  `TenantId = {0}` (the JWT-resolved tenant). Supplying another tenant's GUIDs in any of these three
  arrays can only remove rows from or fail to match the caller's own tenant-scoped result set — it
  cannot surface another tenant's rows, since the join/exclusion always happens after the tenant
  filter, never instead of it.
- **`customers`/`locations` joins downstream of an already-scoped CTE don't re-filter by `TenantId`
  explicitly** (e.g. `JOIN customers c ON c."Id" = q.cust_id`, `LEFT JOIN locations loc ON loc."Id"
  = t."LocationId"`). Confirmed this is identical, pre-existing convention already present in
  `PriceSegmentsRepository.cs` (cleared in TASK-422) — not a new regression introduced by this
  feature, and safe under the same FK-integrity assumption (a `pos_transactions` row's
  `CustomerId`/`LocationId` always belongs to that transaction's own tenant).
- **No explicit cap on `Terms`/`StoreIds`/`ExcludedItemIds` array length** on the request DTOs — a
  caller could in principle submit a very large array, enlarging the `UNNEST`/join workload
  somewhat. This is a systemic, pre-existing pattern shared with `PriceSegments`' own request DTOs
  (not something this task introduced), still bounded by ASP.NET Core's default request-body/JSON-
  depth limits. Not a blocker for this feature specifically; worth a systemic follow-up someday if
  ever revisited, not urgent enough to spawn on its own.

## Not in scope / not re-verified

- Did not re-run `dotnet test` (read-only review, no code changed; TASK-429's own log already
  reports 1213/1213 green for these exact paths). Re-ran `dotnet build ShelfGuard.sln` only, to
  confirm the tree still compiles clean (0 warnings/0 errors) before writing this verdict.
- Did not independently re-verify RLS wiring/session-variable plumbing itself (which connection
  sets `app.tenant_id` per request) — that is pre-existing, shared infrastructure inherited from
  earlier phases, not something this feature touches or could break.
- Frontend (`frontend/features/marketing-analytics/audience-builder/`) not audited in this pass —
  the brief scoped this review to the backend raw-SQL/PII/capability surface; the backend already
  resolves `CanViewUnmaskedPii` server-side regardless of what the frontend sends, so the frontend
  has no independent trust boundary to violate here.

## Overall verdict

**CLEAR TO SHIP.** All 7 mandatory checklist items: **OK**. No risk-level or critical finding. No
code changed (audit only, per brief).

## Git

Not committed (repo convention — main session/user commits; this is a docs-only log file).
