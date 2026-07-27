# TASK-425: Fix — on-screen phone PII never masked in Фаза 2 GET tables

**Agent:** backend-developer
**Date:** 2026-07-27
**Status:** done — fixed, tested, `dotnet build`/`dotnet test` clean, no regressions, in my working
tree. **URGENT — read "Git note" below: pushed `origin/main` (HEAD `ddb1e0ac`) currently does NOT
compile** — an unrelated commit landed mid-session while this file was mid-edit and captured a
half-applied state. My local working tree already has the complete fix; it just isn't committed
yet (repo convention — I don't commit as a spawned agent).

## Context

QA (TASK-424) found `PriceSegmentsController`'s 3 paginated GET-table endpoints (comparison
`audiences/{audience}`, `all-time/customers`, `frequency/audiences/{audience}`) returned
`Phone` raw and unconditionally. Masking (`PiiMasking.MaskPhone`) previously existed only inside
the 3 Excel export builders in `PriceSegmentsService.cs`. Not exploitable today (view floor ==
export-capability floor), but a real gap the moment a tenant grants `marketing_analytics.view`
without `marketing_analytics.export_pii` (ADR-020 capability split this codebase explicitly
supports) — the export mask would become theater since the same phone is already visible one
click earlier, on-screen.

## Fix

Reused the exact existing export-path gate end-to-end, no new capability, no new client
parameter:

- **`Dtos/PriceSegmentDtos.cs` / `Dtos/FrequencyDtos.cs`**: added `bool CanViewUnmaskedPii = false`
  to `PriceAudienceTableRequest`, `AllTimeCustomerTableRequest`, `FrequencyAudienceTableRequest`.
  Defaults closed. Not client-bindable — these records are hand-constructed by the controller from
  named `[FromQuery]` scalars, never bound wholesale from the query string, so there is no way for
  a client to set this field even by guessing a query-param name.
- **`PriceSegmentsController.cs`**: the 3 GET-table actions now pass
  `MarketingAnalyticsAuthorization.CanExportPii(User)` into the request constructor — same
  Infrastructure-layer check the export endpoints already call. (Application has no project
  reference to Infrastructure, so the service itself cannot call this directly — same reason the
  export path already threads its `UnmaskPii` decision through the controller instead of checking
  inline in the service.)
- **`PriceSegmentsService.cs`**: new private helper `MaskPhoneUnlessAuthorized(phone,
  canViewUnmaskedPii)`; all 3 table-building methods (`GetAudienceTableAsync`,
  `GetAllTimeCustomerTableAsync`, `GetFrequencyAudienceTableAsync`) now route `Phone` through it
  instead of passing the raw repository value straight into the row DTO. The 3 export builders
  were already correct — untouched.

Frontend/mobile untouched, as scoped — the "Показати повний номер телефону" checkbox still only
affects the export POST body; the on-screen table now simply reflects whatever the server decides
based on the caller's actual role/capability, automatically.

## Tests

Added 6 tests to `PriceSegmentsServiceTests.cs` (masked-by-default / revealed-with-capability, one
pair per table method), mirroring the existing `ExportAudienceAsync_masks_phone_by_default...`
pattern (NSubstitute-mocked repo). All assert the exact masked shape (`+380 67 *** ** 33`) and the
unmasked raw value, not just "not equal".

## Build/test status

`dotnet build` — 0 errors (1 pre-existing unrelated warning in `MarketplaceServiceTests.cs`, not
touched by this change). `dotnet test` full suite — **1186/1186 green** (was 1180 per TASK-424's
own baseline; +6, exactly the new tests, no regressions). Ran against live Postgres
(`crmproductsystems-postgres-1:5435`, already running) so the `PriceSegmentsRepositoryIntegrationTests`
included in that count are real, not skipped.

## Docs

Added a short "On-screen `phone` masking" note to `.claude/docs/api-contracts.md` under the Фаза 2
section, since this changes the documented response semantics of the 3 GET endpoints (not just an
internal refactor).

## Git note — IMPORTANT, please read before doing anything else with this branch

While I was mid-edit on this task, `origin/main` moved 3 commits (`4ca40fbe`/`8f6b982f`/`ddb1e0ac`
— the whole Фаза 2 feature, TASK-419..424, backend+frontend+docs) — pushed by someone/something
else working in this same shared working tree concurrently with me, not by me. `4ca40fbe`'s own
message already flagged this exact PII gap as a "known follow-up... fix incoming separately," so
the commit itself is legitimate — but its snapshot landed at an inconsistent instant relative to my
own in-flight edits to `PriceSegmentsService.cs`, and the result is a **real, verified compile
break on pushed `main` right now**:

- `git show HEAD:.../PriceSegmentDtos.cs` and `FrequencyDtos.cs` — already contain my full
  `CanViewUnmaskedPii` field additions (my edits landed on disk before that commit ran; zero diff
  against my working copy). Harmless alone (defaulted `false`, unused).
- `git show HEAD:.../PriceSegmentsService.cs` — contains my 3 edited call sites
  (`MaskPhoneUnlessAuthorized(r.Phone, request.CanViewUnmaskedPii)` at lines 132/229/361) **but not
  the `MaskPhoneUnlessAuthorized` method definition itself** (that edit landed on disk moments
  later). `PriceSegmentsService` is a `sealed`, non-partial class in a single file — there is
  nowhere else the definition could be. Confirmed by exact string count: the method name appears
  exactly 3 times in HEAD's copy of the file (the 3 calls), zero definitions. **This will not
  compile** (`CS0103`) — verified via static analysis of the actual committed blob (`git show
  HEAD:<path>`), not assumed.
- `git show HEAD:.../PriceSegmentsController.cs` and the test file — both entirely unmodified at
  HEAD (fully pre-TASK-425), so they aren't part of the break, just missing the fix.

**My local working tree already has the complete, correct, tested version of all 4 files** (this
is what `dotnet build`/`dotnet test` above ran against — 0 errors, 1186/1186 green). Committing my
current working-tree state of these 4 files on top of `HEAD` fixes the break as a side effect of
landing TASK-425 normally — no separate remediation needed, just don't delay it:
- `backend/ShelfGuard.Api/Controllers/PriceSegmentsController.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/PriceSegments/PriceSegmentsService.cs`
- `backend/ShelfGuard.Tests/MarketingAnalytics/PriceSegments/PriceSegmentsServiceTests.cs`
- `.claude/docs/api-contracts.md`

**Not committed by me** — repo convention (every prior task log: "main session/user commits") and
I have no standing authorization to push. Recommend whoever picks this up commits+pushes promptly
given `main` is broken until then. Also worth checking whether a concurrent agent/session is still
active in this exact working tree (`C:\Users\stass\source\CRMProductSystems`, not one of the
`.claude/worktrees/agent-*` paths) before making further changes here, to avoid a repeat.

## Not in scope (per brief, unchanged)

Frontend/mobile, Фаза 1 RFM (no per-customer table there per QA's own check), no new capability
constant, no new query parameter.
