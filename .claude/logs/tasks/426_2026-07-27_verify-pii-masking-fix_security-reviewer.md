# TASK-426: Re-check — TASK-425 on-screen PII masking fix

**Agent:** security-reviewer
**Date:** 2026-07-27
**Status:** done — **verdict: OK.** Narrow re-check of TASK-425's fix (commit `92101ec7`), no
issues found.

## Scope

Verified `PriceSegmentsController.cs` + `PriceSegmentsService.cs` directly (not just the task
logs) against TASK-424's finding (on-screen `Phone` never masked in the 3 Фаза 2 GET tables).

## Verified directly

1. **Server-side, not client-controlled.** All 3 GET-table actions (`GetAudienceTable`,
   `GetAllTimeCustomerTable`, `GetFrequencyAudienceTable`) construct their request record with
   `MarketingAnalyticsAuthorization.CanExportPii(User)` passed as `CanViewUnmaskedPii` —
   read the full controller file: none of the 3 request records (`PriceAudienceTableRequest`,
   `AllTimeCustomerTableRequest`, `FrequencyAudienceTableRequest`) are ever bound wholesale from
   `[FromQuery]`/`[FromBody]`; each action only binds named scalar query params (`period`, `from`,
   `to`, `storeIds`, `page`, `pageSize`, `sortBy`, `sortDescending`) — there is no query-string
   name a client could guess to set this field. `CanExportPii` itself
   (`MarketingAnalyticsAuthorization.cs`) checks `user.IsInRole(...)` (JWT role claims) OR
   `TenantRoleAuthorization.HasCapability` which reads the `"capabilities"` claim off the same
   `ClaimsPrincipal` — both server-issued, nothing derived from request body/query. All 3 request
   records default `CanViewUnmaskedPii = false`, so any future call site that forgets to pass it
   fails closed (masked).
2. **Service-side wiring.** All 3 table-building methods route `Phone` through the new
   `MaskPhoneUnlessAuthorized(phone, canViewUnmaskedPii)` helper (confirmed at
   `PriceSegmentsService.cs:132,229,361`) instead of passing the raw repository value — read the
   whole 688-line file; these are the only 3 read paths that expose `Phone` to a GET response, no
   leftover raw pass-through anywhere else.
3. **Export path unaffected.** The 3 Excel builders (`BuildPriceAudienceExcel`,
   `BuildAllTimeExcel`, `BuildFrequencyExcel`) are untouched — still
   `unmaskPii ? r.Phone : PiiMasking.MaskPhone(r.Phone)`, `unmaskPii` still derived the exact same
   pre-existing way in the controller's `Export*` actions
   (`request with { UnmaskPii = request.UnmaskPii && CanExportPii(User) }`). `git show --stat
   92101ec7` confirms the diff only touches the 3 GET-table call sites in the controller/service,
   never the `Export*` actions.
4. **TASK-414 formula-injection guard intact.** `ExcelExportService.SetCellValue` (untouched,
   not part of this commit) still centrally prefixes any cell value starting with
   `= + - @ \t \r` with `'` for every exported string — confirmed by direct read, not assumption.
5. **New tests match the claim.** 3 of the 6 new `PriceSegmentsServiceTests.cs` cases read
   directly (`GetAudienceTableAsync_masks_phone_on_screen_by_default`,
   `..._reveals_full_phone_when_caller_can_view_unmasked_pii`,
   `GetAllTimeCustomerTableAsync_masks_phone_on_screen_by_default`) assert the exact masked shape
   (`+380 67 *** ** 33`) vs. the raw value, not just inequality.
6. **Git state.** `92101ec7` is the current tip of `main`, working tree clean on these files —
   the compile break TASK-425 flagged (mid-edit race with a concurrent commit) is resolved; this
   commit's diff is exactly the 5 files the task log described (controller, service, tests, docs,
   task log).

## Build/test

- `dotnet build`: 0 errors, 0 warnings.
- `dotnet test` (full suite, live Postgres `crmproductsystems-postgres-1:5435`): **1186/1186
  passed**, 0 failed, 0 skipped — matches TASK-425's own reported count exactly, no regressions.

## Verdict

**OK.** Fix is complete, server-derived (not client-bindable), consistent with the pre-existing
export-masking pattern, doesn't touch the TASK-414 formula-injection guard, and is covered by
passing tests. No further action needed on this path.
