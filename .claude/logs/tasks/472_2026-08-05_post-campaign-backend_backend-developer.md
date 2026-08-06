# TASK-472: Post-campaign analysis engine (Фаза 4 marketing analytics)

**Agent:** backend-developer (interrupted mid-run by a session-limit error; completed by the
orchestrating main session — see "Interruption and recovery" below)
**Date:** 2026-08-05
**Status:** done — `dotnet build` 0 warnings/0 errors, `dotnet test` **1289/1289 green** (was
1222 after TASK-471; net +67, all new). No blocker.

## Context

Фаза 4 of `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` (§"Фази 2-4", post-campaign
audience analysis). Full spec source: `docs/uployal/AUDIENCE_ANALYSIS.md`. Builds the analysis
engine on top of TASK-471's schema (`PostCampaignSegment`/`PostCampaignSegmentMember`).

## Done

- `Features/MarketingAnalytics/PostCampaign/` — `IPostCampaignService`/`PostCampaignService`,
  `IPostCampaignRepository` (Infrastructure impl: `PostCampaignRepository.cs`), `Dtos/`,
  `PostCampaignBehaviorStatus` (Domain/Constants — reactivated/retained/dropped/not_returned),
  `PostCampaignBehaviorClassifier` (pure: classification + rate/delta helpers, all division-by-zero
  paths return `null`, never `0%`), `PostCampaignRecommendationTemplates`, `SegmentImportParser`
  (pure, no DB dependency), `PostCampaignSegmentHash`.
- `IPostCampaignAdvisor`/`Infrastructure/AI/PostCampaignAdvisor/PostCampaignAdvisor.cs` — a
  **separate** advisor interface, not a reuse of `IMarketingAdvisor` as this task's original brief
  asked for. Deliberate, documented correction: `IMarketingAdvisor`'s context DTO is shaped around
  `RfmSegmentKey`, which doesn't fit a post-campaign segment (an externally-sourced customer list,
  not an RFM segment); this exactly mirrors the established precedent `IPriceSegmentAdvisor`
  (TASK-420) already set for the identical reason. Key-resolution plumbing (tenant
  `integration_configs` → env fallback) is still a byte-for-byte copy of the shared pattern — only
  the context DTO shape differs. System prompt explicitly instructs the model to avoid causal
  language ("це узгоджується з...", never "кампанія спричинила...") matching this feature's
  no-control-group scope.
- `IExcelImportService`/`Infrastructure/Export/ExcelImportService.cs` (new — ClosedXML-backed XLSX
  reading; CSV/text parsing has no library dependency, handled directly in `SegmentImportParser`).
- `PostCampaignController` (`api/marketing-analytics/post-campaign`) — same
  `MarketingAnalyticsViewOrCapability` + `[RequireModule("marketing_analytics")]` gate as the other
  3 controllers in this module. 10 endpoints: `GET segments` (list), `POST segments/import`
  (multipart, file XOR rawText, 10 MB cap, `.csv`/`.xlsx`/`.txt` allowlist), `POST
  segments/{id}/analyze`, `GET {id}/summary`, `GET {id}/daily-turnover`, `GET {id}/rfm-activity`,
  `GET {id}/customers` (full server pagination, no Top-200 cap), `GET {id}/migration`, `POST
  {id}/explain`, `POST {id}/exports/customers`, `POST {id}/exports/unknown-tokens`.
- Import parsing (`SegmentImportParser`) — strict, whole-token-only classification (GUID via
  `Guid.TryParseExact(x, "D", ...)`, phone via character-class pre-check + the existing
  `PhoneNormalizer` reused from Фаза 0) — never extracts substrings from arbitrary text, the
  source doc's §5.3/§5.4/§31.2/§38 critical fix over the competitor's own broken parser. CSV column
  auto-detection tries `id`/`customer_id`/`guid`/`phone`/`телефон`/`номер` (case-insensitive),
  falls back to column 0, echoes the choice + a 10-row preview back for a confirm/override
  resubmit via `columnIndex`. Row cap 20,000 (brief's explicit, more conservative choice over the
  competitor's unconfirmed 50,000).
- RFM migration matrix (`GetMigrationAsync`) reuses `IMarketingAnalyticsRepository
  .GetScoredCustomersAsync` and the existing `RfmSegmentClassifier` **unchanged** — no second RFM
  implementation. Calls it **three** times (before-window, after-window, and an all-time
  `DateOnly.MinValue`..`afterEnd` call, new to this feature) so a customer absent from a given
  window's scored rows can be correctly told apart as either (a) zero purchases ever → Фаза 1's
  "Без покупок" null bucket, or (b) real all-time history but none in this specific window → an
  "ordinary low-R" real classification, fed through the SAME classifier with sentinel worst-case
  R=F=M=1 and the customer's real lifetime facts. Verified by hand + by test: case (b) resolves to
  `Hibernating` (R≤2 ∧ F≤2 ∧ M≤2), never null.
- `PostCampaignRepository` — **zero raw SQL**, plain EF Core LINQ throughout (flagged explicitly
  for security-reviewer: unlike Фаза 1/2's NTILE/PERCENTILE_CONT, this feature's aggregates
  — purchase_count/turnover/last_purchase_date per customer — have a straightforward LINQ
  translation). Excludes `PosTransaction.Status == "fiscalization_failed"` from all turnover/check
  aggregates. Bulk customer resolution (`FindCustomersByIdsOrPhonesAsync`) uses `list.Contains(x)`
  → `= ANY(@p)`, the same translation already established at
  `MarketingAnalyticsRepository.GetExportCustomersAsync`.
- Customer table: name + phone (masked by default, unmasked only via the existing
  `MarketingAnalyticsAuthorization.CanExportPii` role-or-capability gate) — follows this
  codebase's own established PII convention, not the competitor's anonymous-ID-only table.
- Exports: `IExcelExportService` (ClosedXML) reused as-is; both export actions log to
  `ActivityLog` (`marketing_analytics.post_campaign.export_customers` /
  `..._export_unknown_tokens`); the unknown-tokens export has no PII gate (uploader-supplied raw
  tokens, never resolved Customer PII) but is still audited for consistency.
- Every KPI-bearing response carries `SegmentHash`/`CalculatedAt` + all four window dates —
  matches Фаза 1's `FiltersHash`/`CalculatedAt` transparency convention.
- New unit tests: `SegmentImportParserTests` (GUID/phone/garbage/decimal/UUID-must-not-split/
  duplicate-after-normalization cases), `PostCampaignBehaviorClassifierTests` (balance identities,
  null-not-zero division-by-zero cases), `PostCampaignServiceTests` (67 tests total covering
  import/analyze/summary/daily-turnover/rfm-activity/customers/migration/explain/exports),
  migration-matrix marginal-sum identity assertions (row sums = before donut, column sums = after
  donut, total = MatchedCount).

## Interruption and recovery

The spawned backend-developer agent hit a session-limit error mid-run, while polishing its own
final test file (`PostCampaignServiceTests.cs`) — all production code (service, repository,
controller, advisor, parser, DTOs) was already written and structurally complete at that point.
The orchestrating main session verified and finished the task directly (CLAUDE.md's "quick
isolated fix in a single well-known file" exception, not a new agent spawn) rather than re-running
a fresh agent from scratch:

1. `dotnet build` — main projects (Domain/Application/Infrastructure/Api) compiled clean; only
   `ShelfGuard.Tests` failed, with exactly **one** compile error at the exact line the agent's own
   last message ("let me double check and simplify the last CSV test...") pointed at: a stray
   `await` on `IExcelExportService.Export(...)`, which is synchronous (`ExcelExportResult`, not
   `Task<ExcelExportResult>`). Fixed by removing the `await`.
2. `dotnet test` then surfaced 3 failures, all pre-existing test-authoring bugs in the
   not-yet-finished test file, none in production code:
   - Two `NSubstitute.Exceptions.AmbiguousArgumentsException` on
     `GetCustomerPeriodMetricsAsync(...)` calls — the test mixed a literal `null` for the
     `storeIds` parameter with `Arg.Any<IReadOnlyList<Guid>>()` for the adjacent same-typed
     `customerIds` parameter; NSubstitute requires matchers for *all* parameters of a type once
     any one of them uses a matcher. Fixed by changing the three `null` literals to
     `Arg.Any<IReadOnlyList<Guid>?>()`.
   - One genuine test-logic bug in `GetMigrationAsync_marginal_sums_equal_MatchedCount_and_
     no_purchase_customers_get_the_null_bucket`: the test's own fixture data
     (`afterOnlyRow`: R=4/F=2/M=2) does not actually classify as `RfmSegmentKey.Champions` under
     the real `RfmSegmentClassifier` rules (Champions requires F≥4 **and** M≥4; this row fails
     both) — it classifies as `PotentialLoyalist` (R≥3, F∈[2,3], M≥2). Manually traced the
     classifier's real rule order to confirm. The test's assertion (and its now-stale comment)
     assumed "Champions-shaped" without tracing the actual thresholds. Fixed by correcting the
     assertion target to `PotentialLoyalist` and rewriting the comment to explain the real
     classification path (including the `Hibernating` "before" outcome via the R=F=M=1 sentinel
     branch) instead of the mistaken one. Production code (`ClassifyForWindow` in
     `PostCampaignService.cs`) was correct as written; only the test's expectation was wrong.
3. Re-ran the full suite: **1289/1289 green**, confirmed no regressions beyond the fixes above.

No production logic was changed during recovery — only the test file.

## Not in scope (per brief, unchanged)

- Frontend (TASK-473, next).
- Causal/control-group analysis — explicitly out of scope, matching the plan's own Фаза 1 scoping
  and the source doc's §28/33.6 framing as a stretch goal.
- No changes to Фаза 0-3 code beyond calling their existing public interfaces
  (`IMarketingAnalyticsRepository`, `RfmSegmentClassifier`, `IExcelExportService`).

## Flagged explicitly for TASK-474 (security-reviewer)

1. File-upload handling: 10 MB `RequestSizeLimit`, `.csv`/`.xlsx`/`.txt` extension allowlist,
   in-memory only (`MemoryStream`, never written to disk) — confirm this is sufficient; no
   content-sniffing beyond extension check today.
2. Confirmed zero raw-SQL string interpolation anywhere in this task's new code —
   `PostCampaignRepository` is 100% LINQ (see its own class doc comment for why, vs. Фаза 1/2's
   NTILE/PERCENTILE_CONT raw SQL).
3. `SegmentImportParser`'s strict-parsing test coverage (GUID-must-not-split, decimal-not-two-IDs,
   free-text-must-not-yield-a-phone) — review against `docs/uployal/AUDIENCE_ANALYSIS.md` §5.3's
   documented competitor failure modes to confirm all are actually covered.
4. `consumer_account`/RLS surface: none new here (no consumer-JWT code path touches this feature
   at all — staff-only, same posture as `PriceSegmentSettings`).

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `backend/ShelfGuard.Application/Features/MarketingAnalytics/PostCampaign/**` (new — service,
  repository interface, DTOs, classifier, recommendation templates, import parser, segment hash)
- `backend/ShelfGuard.Domain/Constants/PostCampaignBehaviorStatus.cs` (new)
- `backend/ShelfGuard.Domain/Interfaces/IPostCampaignAdvisor.cs` (new)
- `backend/ShelfGuard.Infrastructure/AI/PostCampaignAdvisor/PostCampaignAdvisor.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/PostCampaignRepository.cs` (new)
- `backend/ShelfGuard.Application/Common/IExcelImportService.cs` (new)
- `backend/ShelfGuard.Infrastructure/Export/ExcelImportService.cs` (new)
- `backend/ShelfGuard.Api/Controllers/PostCampaignController.cs` (new)
- `backend/ShelfGuard.Tests/MarketingAnalytics/PostCampaign/**` (new — see test file names above)
- `backend/ShelfGuard.Application/DependencyInjection.cs`,
  `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` (new service/repository/advisor/
  import-service registrations)
