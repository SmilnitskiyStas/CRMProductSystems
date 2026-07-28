# TASK-429: AudienceBuilder backend (Фаза 3)

**Agent:** backend-developer
**Date:** 2026-07-27
**Status:** done — `dotnet build`/`dotnet test` clean (1213/1213, was 1186; +27), no blocker.

## Context

Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фази 2-4" (Фаза 3 roadmap item).
Design doc: scratchpad `phase3-audience-builder-design.md` (full §1-§11). Competitive analysis:
`docs/uployal/AUDIENCE_PREPARATION_ANALYSIS.md`. Prep task: TASK-428 (`idx_items_name_trgm`,
already migrated — see "Accepted Seq Scan" note below, no code from this task changes that
decision). This is a from-scratch continuation of a session that hit its usage limit before
writing any code — no `AudienceBuilder/` files existed at start.

## Done

New `Features/MarketingAnalytics/AudienceBuilder/` (Application) + `AudienceBuilderRepository.cs`
(Infrastructure, third raw-SQL repository after `MarketingAnalyticsRepository`/
`PriceSegmentsRepository`) + `AudienceBuilderController.cs` (Api) + DI in both
`Application/DependencyInjection.cs` and `Infrastructure/DependencyInjection.cs`. No AI advisor —
design doc's endpoint table (§7) has no `/explain` action for this feature, confirmed deliberately
absent, not an oversight.

### Files
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/AudienceBuilder/Dtos/AudienceBuilderDtos.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/AudienceBuilder/AudienceBuilderSortKeys.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/AudienceBuilder/AudienceBuilderFilterHash.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/AudienceBuilder/IAudienceBuilderRepository.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/AudienceBuilder/IAudienceBuilderService.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/AudienceBuilder/AudienceBuilderService.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/AudienceBuilderRepository.cs`
- `backend/ShelfGuard.Api/Controllers/AudienceBuilderController.cs`
- `backend/ShelfGuard.Application/DependencyInjection.cs` (+service registration)
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` (+repository registration)
- `backend/ShelfGuard.Tests/MarketingAnalytics/AudienceBuilder/AudienceBuilderServiceTests.cs` (13 tests)
- `backend/ShelfGuard.Tests/Infrastructure/AudienceBuilderRepositoryIntegrationTests.cs` (14 live-Postgres tests)

Gating: `[Route("api/marketing-analytics/audience-builder")]`,
`[Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)]`,
`[RequireModule("marketing_analytics")]` — same module/capability as Фаза 1/2, nothing new added
to `AppPolicies`/`TenantRoleCapabilities`.

## Two real bugs found and fixed in the design doc's own SQL sketch (not present in my final code)

The design doc's §3/§5/§8 SQL was a rough sketch, not vetted against an edge case that turned out
to matter. Both are covered by dedicated regression tests (see below) — verified live against
Postgres, not just reasoned about.

1. **Double-counting when one item matches more than one term.** The sketch's `customer_line_items`
   joined `pos_transaction_items` directly against `selected` (term_index, item_id pairs) —
   `selected` legitimately has MULTIPLE rows for the same item_id when that item matches more than
   one term (e.g. a text term AND a category term both matching the same product, a very plausible
   AND-mode combination). That join fans out and double-counts the purchase's quantity/amount once
   per matching term_index, while still (correctly) satisfying AND-mode's "covered every term"
   requirement. Fixed by splitting into two separate aggregates over the SAME underlying
   `customer_lines` (one join against `pos_transaction_items`, not two): `customer_totals`
   (deduplicated SUM/COUNT per customer, joined against `selected_items` — item ids only, term
   info dropped) and `customer_term_coverage` (which term_indexes each customer covered, derived
   by re-joining the already-computed `customer_lines` back to the small in-memory `selected` set,
   NOT a second scan of `pos_transaction_items`/`pos_transactions`). Regression test:
   `GetOverviewAsync_item_matching_two_terms_counts_once_toward_totals_but_covers_both_terms` — a
   customer buys 3 units of an item matching BOTH `Text("Пепсі")` and `Category(Напої)`
   simultaneously; AND mode correctly includes them (covered_terms=2=termCount) AND
   `UnitsPurchased`/`TotalSpend` are the real 3/30, never the double-counted 6/60.
2. **Line amount was `PriceFinal` alone, not `PriceFinal * Quantity`.** `PriceFinal` is a
   PER-UNIT price (confirmed against `PriceSegmentsRepository.GetAverageUnitPriceAsync` and
   `AnalyticsRepository`'s own `TotalRevenue: g.Sum(i => i.PriceFinal * i.Quantity)`) — using it
   bare would under-report spend on any line with `Quantity != 1`. Fixed throughout (every
   `amount`/`total_amount` computation is `ti."PriceFinal" * ti."Quantity"`).

Both fixes apply consistently across all 4 "own product" queries (Overview/Buyers/BuyerReceipts/
MatchedItems) and both competitor queries — same shared CTE shape everywhere, documented in the
class-level doc comment on `AudienceBuilderRepository`.

## TASK-428's critical finding — handled exactly per orchestrator's decision

- **No LEAKPROOF, no SECURITY DEFINER.** Accepted the Seq Scan as documented v1 behavior. A
  detailed comment sits on `IAudienceBuilderRepository` (class-level, read-before-editing) and is
  repeated at the top of `AudienceBuilderRepository` explaining the RLS/non-leakproof-ILIKE
  mechanism, the live 1085ms-vs-2ms measurement from TASK-428's log, and the two possible future
  fixes (each needing its own security review) — exact wording per the brief.
- **Every `t."CreatedAt"` date-range comparison casts its parameter explicitly as
  `{n}::timestamptz`** in every new query in `AudienceBuilderRepository.cs` — done even though the
  C# side already converts `DateOnly` → `DateTime(Kind=Utc)` before binding (which Npgsql would
  likely already type as `timestamptz` on its own, same as every pre-existing MarketingAnalytics/
  PriceSegments query that has never hit this issue) — the explicit cast removes any ambiguity
  regardless of Npgsql's own inference, per the brief's "Обов'язково" instruction.
- Categories typeahead (`categories.Name ILIKE`) gets the identical accepted-tradeoff comment —
  same non-leakproof-under-RLS situation, smaller table (typical per-tenant category counts),
  same v1 acceptance.

## Design doc SQL sketch — one further clarification, applied

`GetOverviewAsync` is a single lightweight aggregate query (`COUNT`/`SUM` scalar subqueries, one
row, never fetches the per-customer population into C#) — matches design doc §7's explicit
performance requirement, same shape as Фаза 1's `GetCustomerBaseCountsAsync`.

## Deliberate deviations from the design doc's literal DTO sketch (all judgment calls, not product decisions)

1. **`CompetitorAudienceRequest` omits `OwnMode`/`OwnMinQuantity`/`OwnMinAmount`.** Design doc §5's
   own SQL for `own_buyers_to_exclude` is a plain "bought ANY unit of ANY own-matching item,
   ever/in-period" existence check — never gated by AND/OR mode or thresholds (those only filter
   the MAIN audience's customer list). Including unused fields on the DTO would be dead API
   surface; the frontend should send the SAME shared term-builder state (`terms`/`excludedItemIds`)
   as `ownTerms`/`ownExcludedItemIds` for the competitor tab, but does not need to send
   `mode`/`minQuantity`/`minAmount` there.
2. **`OwnExcludedItemIds` IS applied to `own_matched`** (design doc's sketch didn't parameterize
   this at all for the competitor query, likely an oversight — §5 itself says "той самий для
   ownTerms", implying reuse of the same matching+curation logic as the main tab). Verified via
   `GetCompetitorOverviewAsync_excluding_the_only_own_matched_item_disables_the_exclusion_entirely`:
   excluding the sole own-matching item makes `own_matched` empty, so nobody gets excluded from the
   competitor audience — a real, intentional behavior difference from the design doc's literal SQL.
3. **`ExcludedItemIds`/`StoreIds` are nullable (`IReadOnlyList<Guid>?`)** on the request DTOs
   (design doc showed them non-nullable) — lets the client omit the field entirely instead of
   sending `[]`, same convention `StoreIds` already uses everywhere else in this codebase.
4. **Export request DTOs (`ExportAudienceBuyersRequest`/`ExportCompetitorBuyersRequest`) are their
   own flat records**, not a reuse of `AudienceBuildRequest`/`CompetitorAudienceRequest` with ignored
   paging fields — matches the actual established precedent (`ExportPriceAudienceRequest` etc. in
   Фаза 2), not just the design doc's abbreviated endpoint-table shorthand.
5. **`AudienceBuilderFilterHash` is its own small copy**, not a reach into
   `PriceSegments.PriceSegmentFilterHash` (which is public and technically reusable) — avoids an
   odd, undocumented cross-feature dependency; `PiiMasking` (declared one namespace up, in the
   common parent `MarketingAnalytics` namespace) remains the only intentional shared dependency.

## PII masking — day 0, per design doc §9

`MaskPhoneUnlessAuthorized` in `AudienceBuilderService` calls the SAME (not duplicated)
`PiiMasking.MaskPhone` already used by RFM/PriceSegments (`internal static`, same assembly).
`CanViewUnmaskedPii` is resolved server-side in the controller via
`MarketingAnalyticsAuthorization.CanExportPii(User)` for both `buyers`/`competitor/buyers` reads —
never a client-supplied flag on those two GETs-as-POST. Exports accept a client `UnmaskPii` flag
that the controller ANDs with the same capability check before it reaches the service. Every export
writes an `ActivityLog` row (Action + filter snapshot + row count + `truncated`/`piiMasked` flags),
same audit contract as Фаза 1/2, matching the competitive analysis §25 journal requirement.

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning, same one every
  recent task log reports, `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln` — **1213/1213 green** (was 1186; +27: 13 service unit tests +
  14 live-Postgres repository integration tests), zero regressions.
- Live-Postgres integration tests (`crmproductsystems-postgres-1`, port 5435, `crm` superuser
  connection — SQL correctness under test, not RLS/tenant isolation, same convention as
  `PriceSegmentsRepositoryIntegrationTests`) cover: categories typeahead; OR vs AND semantics with
  real overlapping/non-overlapping term matches; the double-counting regression case; manual SKU
  exclusion removing a buyer whose only match was excluded; min-quantity threshold; period
  boundary exclusion; pagination/sort stability; matched-items zero-sales inclusion + exclusion
  flag + barcode-less items; receipt-level export scoping (only matched SKUs' amount/qty per
  receipt, not the receipt's full total); competitor InPeriod vs AllTime horizon producing
  genuinely different audiences; competitor own-side exclusion. Dev DB confirmed left clean after
  the run (`SELECT count(*) FROM tenants WHERE "Name" LIKE '%AudienceBuilder%'` → 0).
- **Unrelated, pre-existing note (not from this task):** found 3 leftover `Loyalty Repo Test *`
  tenant rows in the dev DB dated 2026-07-26, from a prior `LoyaltyRepositoryIntegrationTests` run
  whose cleanup apparently didn't run to completion at some point. Harmless (3 rows, dev-only, no
  security implication), did not fix — out of this task's scope, flagging for awareness only.

## API contract for frontend-developer

Base route: `api/marketing-analytics/audience-builder`. Same auth/module gate as
`price-segments`/RFM. Every read endpoint is **POST** (design doc §1's explicit decision — filter
shape doesn't fit a query string). All DTO fields are camelCase on the wire (default ASP.NET Core
`System.Text.Json` casing — no custom attributes needed). Enums serialize as PascalCase **strings**
(`JsonStringEnumConverter`, no naming-policy override — matches `PriceAudienceKey` etc.'s existing
wire format, e.g. `"kind": "Text"`, not `"text"`).

### `AudienceTermRequest`
```
{ kind: "Text" | "Category", text: string | null, categoryId: string(guid) | null }
```
Only `text` (Text) or `categoryId` (Category) needs to be set for the matching kind — the other is
ignored. A term missing its own kind's value is silently dropped server-side.

### `GET /categories?search=&limit=`
→ `AudienceCategoryOptionDto[]`: `{ categoryId, name, itemCount }`. `limit` <= 0 falls back to 20
server-side (no hard cap enforced client-side needed, service clamps to [1,100]).

### `POST /overview` — body `AudienceBuildRequest` → `AudienceOverviewDto`
```
AudienceBuildRequest = {
  from: "yyyy-MM-dd", to: "yyyy-MM-dd", storeIds: string(guid)[] | null,
  terms: AudienceTermRequest[], mode: "Any" | "All",
  minQuantity: number | null, minAmount: number | null,
  excludedItemIds: string(guid)[] | null,
  page: number, pageSize: number, sortBy: string | null, sortDescending: boolean,
  canViewUnmaskedPii: boolean   // IGNORED server-side on overview/buyers/matched-items reads —
                                  // send false/omit, the controller always recomputes it
}
AudienceOverviewDto = {
  participantsCount, itemsInSelectionCount, unitsPurchased, totalSpend,
  filtersHash, calculatedAt
}
```
An empty (or all-malformed) `terms` list never touches the database — returns a zeroed DTO with a
valid `filtersHash` (mirrors "formation button disabled until a term exists").

### `POST /buyers` — body `AudienceBuildRequest` → `AudienceBuyerTableDto`
```
AudienceBuyerRowDto = { customerId, name, phone, quantityPurchased, receiptCount, totalAmount }
AudienceBuyerTableDto = {
  totalCount, withPhoneCount, rows: AudienceBuyerRowDto[],
  page, pageSize, totalPages, sortBy, sortDescending, filtersHash, calculatedAt
}
```
`sortBy` allowlist: `"name" | "qty" | "receipts" | "amount"` (default `"qty"` descending — matches
the competitor's own confirmed base-state sort). Unrecognized values silently fall back to default.

### `POST /matched-items` — body `AudienceBuildRequest` → `MatchedItemsTableDto`
```
MatchedItemRowDto = { itemId, name, barcodesJoined, isExcluded, quantitySold, receiptCount, buyerCount }
MatchedItemsTableDto = { totalCount, rows: MatchedItemRowDto[], page, pageSize, totalPages, sortBy, sortDescending, filtersHash, calculatedAt }
```
`sortBy` allowlist: `"name" | "sold" | "receipts" | "buyers"` (default `"sold"` descending).
`barcodesJoined` is `null` when the item has no barcodes (never an empty string). Zero-sales SKUs
ARE included (all three sales fields `0`, `isExcluded` reflects whatever `excludedItemIds` was sent
in the SAME request — toggling a checkbox and re-calling this endpoint is how the UI refreshes it).

### `POST /exports/buyers` — body `ExportAudienceBuyersRequest` → XLSX file (`Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`)
```
ExportAudienceBuyersRequest = {
  from, to, storeIds, terms, mode, minQuantity, minAmount, excludedItemIds,
  unmaskPii: boolean   // real client flag, ANDed server-side with the caller's actual capability —
                         // requesting unmask without permission silently falls back to masked,
                         // never a 403
}
```
**Receipt-level**, not customer-level (one row per receipt, so one participant can produce several
rows) — columns: Ім'я, Телефон, № чека, Дата, Заклад, Куплено (шт — only the matched/selected SKUs
on THAT receipt, not the receipt's full total), Сума (₴ — same restriction). Capped at 50,000 rows
server-side (same convention as every other MarketingAnalytics export).

### `POST /competitor/overview` — body `CompetitorAudienceRequest` → `CompetitorOverviewDto`
```
CompetitorAudienceRequest = {
  from, to, storeIds,
  ownTerms: AudienceTermRequest[], ownExcludedItemIds: string(guid)[] | null,
  competitorTerms: AudienceTermRequest[], horizon: "InPeriod" | "AllTime",
  page, pageSize, sortBy, sortDescending, canViewUnmaskedPii
}
CompetitorOverviewDto = { newAudienceCount, competitorItemsCount, unitsPurchased, totalSpend, filtersHash, calculatedAt }
```
`ownTerms`/`ownExcludedItemIds` should be the SAME state as the main term-builder tab (design doc
§5/§10 — one shared form; the competitor tab only adds its own `competitorTerms` chips). Both
`ownTerms` and `competitorTerms` must resolve to at least 1 valid term or the whole request
short-circuits to a zeroed result without touching the database.
`unitsPurchased`/`totalSpend` are always period-scoped (the horizon only affects who counts as
"new", never the KPI window) — matches analysis doc §5's explicit rule.

### `POST /competitor/buyers` — body `CompetitorAudienceRequest` → `CompetitorBuyerTableDto`
Same row/table shape as `/buyers` (`CompetitorBuyerRowDto`/`CompetitorBuyerTableDto` — identical
fields, different type names). Same `sortBy` allowlist as `/buyers`, default `"qty"` descending.

### `POST /exports/competitor-buyers` — body `ExportCompetitorBuyersRequest` → XLSX file
Same shape as `CompetitorAudienceRequest` minus paging, plus `unmaskPii`. **Customer-level**, not
receipt-level (columns: Ім'я, Телефон, Куплено шт, Чеків, Сума ₴) — the competitor tab is not a
raffle/draw scenario, so receipt granularity isn't needed.

## Not in scope (per brief)

- No LEAKPROOF/SECURITY DEFINER (see above).
- Domain entities/DbContext/migrations untouched beyond reading.
- `Features/Loyalty/`, `Features/ConsumerAuth/`, existing RFM/PriceSegments code untouched.
- No "saved named audiences" (explicitly out of scope per design doc §11).
- No frontend/mobile changes.

## Git

Not committed (repo convention — main session/user commits).
