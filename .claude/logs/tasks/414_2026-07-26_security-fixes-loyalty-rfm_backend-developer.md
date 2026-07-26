# TASK-414: Security remediation — Loyalty + Marketing Analytics (3 findings from TASK-412)

**Agent:** backend-developer
**Date:** 2026-07-26
**Status:** done — **all 3 findings fixed and verified.** No blockers.

## Scope

Fixed exactly the 3 findings assigned from `.claude/logs/tasks/412_2026-07-26_security-review-loyalty-rfm_security-reviewer.md`:
critical Excel/CSV formula injection, high-priority loyalty-balance TOCTOU race, and the dead
`marketing_analytics.export_pii` capability (+ unmasked email). Nothing else touched.

## 1. CRITICAL — Excel/CSV formula injection (fixed)

`ExcelExportService.SetCellValue` now routes every string it writes (headers, the truncation
banner, and every row value — both the explicit `string` case and the `.ToString()` fallback for
any other type) through one new centralized helper, `SanitizeForSpreadsheet`: if the value's first
character is `=`, `+`, `-`, `@`, Tab, or CR, the whole value is prefixed with a single apostrophe.

**Verified the actual ClosedXML 0.105.1 behavior empirically** (didn't assume) via a throwaway
probe project: `cell.Value = someString` always produces a `DataType.Text` cell with no `<f>`
formula element — ClosedXML itself never turns a leading `=` into a live formula. The real risk is
downstream (Excel/Sheets/Calc re-interpreting a properly-typed string cell's leading `=`/`+`/`-`/`@`
on render). More interesting finding: ClosedXML does NOT keep a literal `'` character in the stored
string when you prefix one — it implements the real OOXML "quote prefix" convention, stripping the
apostrophe and instead setting `cell.Style.IncludeQuotePrefix` (serialized as `quotePrefix="1"` on
the cell's `<xf>` in `styles.xml`), exactly what real Excel does when a human types `'=foo`
manually. Confirmed by inspecting the raw OOXML directly. This is a stronger, spec-native
mitigation than a raw leftover-apostrophe approach — but it also means a test asserting on cell
text alone can't observe the fix; tests assert on `cell.Style.IncludeQuotePrefix` instead.

New test file `backend/ShelfGuard.Tests/Infrastructure/ExcelExportServiceTests.cs` (9 tests, all
via a real ClosedXML round-trip of the actual `Export()` output — not mocks): the exact
`=cmd|'/c calc'!A1` payload from the brief plus `+`/`-`/`@`/Tab/CR variants all get
`IncludeQuotePrefix=true` + `HasFormula=false`; normal values (names, Cyrillic, email, a phone
starting with `+`) are verified either untouched or correctly quote-prefixed as appropriate;
headers and non-string values are covered too.

Files: `backend/ShelfGuard.Infrastructure/Export/ExcelExportService.cs`,
`backend/ShelfGuard.Tests/Infrastructure/ExcelExportServiceTests.cs` (new).

## 2. HIGH — LoyaltyMembership.Balance TOCTOU race (fixed)

Added the same `xmin`/`IsRowVersion()` optimistic-concurrency pattern `ProductStock` already uses
(TASK-356) to `LoyaltyMembership` in `AppDbContext.cs`. New EF migration
`AddLoyaltyMembershipConcurrencyToken` — a genuine no-op (xmin is a reserved Postgres system column
that already exists on every row; the scaffolder's auto-generated `AddColumn`/`DropColumn` calls
were replaced with the same no-op-with-explanation shape as the original
`AddProductStockXminConcurrencyToken` migration). Applied cleanly to the local dev DB with
`dotnet ef database update` — no errors, confirming it's safe against an already-populated table
(no backfill needed; xmin already exists on every existing row).

`LoyaltyRepository.SaveChangesAsync` now catches `DbUpdateConcurrencyException` and translates it
to `ConcurrencyConflictException`, mirroring `PosRepository`'s existing pattern exactly.
`LoyaltyService.ManualAdjustAsync` now catches that exception around its `SaveChangesAsync` call
and returns a clean 409 instead of letting it propagate. `PosService.CreateSaleAsync`'s existing
concurrency catch block (already wraps the one shared `SaveChangesAsync` that now also flushes the
loyalty balance write) needed no logic change — only its comment and user-facing message were
updated from stock-only wording to cover both stock and loyalty-balance conflicts, since a single
`SaveChangesAsync` call now protects both entities.

Tests: `LoyaltyServiceTests.ManualAdjustAsync_concurrency_conflict_returns_409` (mocked repo, pins
the service-layer translation, same shape as `PosServiceTests`' existing concurrency test). New
`backend/ShelfGuard.Tests/Pos/LoyaltyConcurrencySalesIntegrationTests.cs` — real-Postgres test
(mirrors `PosConcurrencySalesIntegrationTests`): two concurrent `PosService.CreateSaleAsync` calls,
each redeeming 40 off a shared membership starting at balance 100, using a deterministic rendezvous
gated on `GetMembershipByIdAsync` (not timing luck) and two *different* products with ample stock
so the already-covered ProductStock race can't interfere. Confirmed: exactly 1 success + 1 clean
409, and the persisted final balance is exactly 60 — not 100 (lost update) and not 20 (both
redemptions landed). Ran twice to confirm no flakiness.

Files: `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`,
`backend/ShelfGuard.Infrastructure/Migrations/20260726164058_AddLoyaltyMembershipConcurrencyToken.cs`
(+ `.Designer.cs`, + regenerated `AppDbContextModelSnapshot.cs`),
`backend/ShelfGuard.Infrastructure/Data/Repositories/LoyaltyRepository.cs`,
`backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs`,
`backend/ShelfGuard.Application/Features/Pos/PosService.cs`,
`backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs`,
`backend/ShelfGuard.Tests/Pos/LoyaltyConcurrencySalesIntegrationTests.cs` (new).

## 3. `marketing_analytics.export_pii` dead capability + unmasked email (fixed)

Root cause confirmed exactly as the review described: `MarketingAnalyticsController`'s class-level
`[Authorize(Policy = CanViewAnalytics)]` used the identical 4-role set as
`MarketingAnalyticsAuthorization.CanExportPii`'s own first branch, so nobody outside those 4 roles
could ever reach the action methods to exercise the capability — unlike `LegalEntityAuthorization`,
where the controller's floor is strictly *looser* than the check's role branch, leaving room for a
capability holder.

Applied the exact ADR-020 precedent already used for `AnalyticsController`/`AnalyticsViewOrCapability`:
new base capability `TenantRoleCapabilities.MarketingAnalyticsView` ("Маркетинг" group), new policy
`AppPolicies.MarketingAnalyticsViewOrCapability` (`RoleOrCapabilityRequirement(CanViewAnalyticsRoles,
MarketingAnalyticsView)`), and swapped the controller's class-level attribute to it. Zero behavior
change for every existing role (same base role set); a tenant that grants a "marketing specialist"
role both `marketing_analytics.view` and `marketing_analytics.export_pii` can now actually reach the
export endpoints and get the unmasked variant — which is what `MarketingAnalyticsExportPii`'s own
doc comment always claimed it did.

Email is now masked the same way phone already is: `MarketingAnalyticsService.BuildCustomerExcel`
applies a new `MaskEmail` helper (keeps first char + full domain, fixed-length mask on the local
part so the mask doesn't leak local-part length) unless `unmaskPii` is true — same default-masked
posture as phone, closing the inconsistency the review flagged.

Tests: extended `AppPoliciesTests.cs` (capability wiring, base-roles-match, a dedicated test
confirming a Cashier-rank capability holder is now admissible where it previously wasn't) and
`MarketingAnalyticsServiceTests.ExportSegmentAsync_masks_phone_and_email_by_default_and_unmasks_when_requested`
(renamed from the phone-only version, now also asserts email masked/unmasked).

Files: `backend/ShelfGuard.Domain/Constants/TenantRoleCapabilities.cs`,
`backend/ShelfGuard.Infrastructure/Authorization/AppPolicies.cs`,
`backend/ShelfGuard.Api/Controllers/MarketingAnalyticsController.cs`,
`backend/ShelfGuard.Application/Features/MarketingAnalytics/MarketingAnalyticsService.cs`,
`backend/ShelfGuard.Tests/Authorization/AppPoliciesTests.cs`,
`backend/ShelfGuard.Tests/MarketingAnalytics/MarketingAnalyticsServiceTests.cs`.

## Verification

`dotnet build` — 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`, not
touched here). `dotnet test` (full suite) — **1105/1105 green**, including all pre-existing
`PosServiceTests`/`MarketingAnalyticsServiceTests`/TASK-406 Excel-export-adjacent tests with no
regressions, plus 2 new live-Postgres integration tests (loyalty concurrency race,
`dotnet ef database update` applied cleanly). Ran the two new live-DB tests an extra time each to
rule out flakiness.

Did not touch anything outside these 3 findings — no changes to `.claude/docs/`,
`known-issues.md`, or any of the review's other (lower-priority, explicitly out-of-scope) items
(#4 consumer JWT revocation, RLS policy `FOR` clause narrowing, rls_audit_test_role gap, etc.).
Not committed (repo convention — main session/user commits).
