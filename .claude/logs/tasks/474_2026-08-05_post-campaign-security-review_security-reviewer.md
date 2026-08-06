# TASK-474: Security review — Фаза 4 (post-campaign audience analysis)

**Agent:** security-reviewer
**Date:** 2026-08-05
**Status:** done — **verdict: NOT clear to ship the import endpoint as-is.** 1 HIGH finding
(resource-exhaustion via the XLSX upload path), 1 MEDIUM (missing separate upload permission), a
few LOW/informational notes. Every other checklist item: **OK**. Read-only audit, no code changed.

## Context

Read TASK-471 (database-engineer)/472 (backend-developer)/473 (frontend-developer) logs first,
`docs/uployal/AUDIENCE_ANALYSIS.md` §32/§36.10, ADR-023 (base + both addendums), and the three
prior reviews in this series (TASK-412, 422, 431) for the established review bar — then read the
actual code directly rather than trusting the builders' own security claims. Live-verified RLS
against the real dev DB (`crmproductsystems-postgres-1`, `shelfguard_app_dev` role, not superuser).

## Verdicts on the 9 requested items

### 1. RLS on the two new tables — **OK, live-verified + structurally airtight against consumer JWTs**

`docker exec ... psql -U shelfguard_app_dev -d crm`:
- `pg_tables`: both `post_campaign_segments`/`post_campaign_segment_members` owned by
  `shelfguard_app_dev`, not a bootstrap superuser.
- `pg_class`: `relrowsecurity`/`relforcerowsecurity` = `t`/`t` on both.
- `pg_policies`: exactly 3 policies each (`tenant_isolation`/`provider_bypass`/`worker_bypass`),
  correct `qual` text — NULLIF-guarded fail-closed tenant check, `provider_bypass` as
  `= ANY(ARRAY['provider','provider_admin'])`, no `consumer_self_access`. Matches TASK-471's log
  and the migration file exactly.

Consumer-JWT reachability — traced all three layers end to end (not assumed from the ADR alone):
1. **Authorization policy**: `PostCampaignController` carries
   `[Authorize(Policy = MarketingAnalyticsViewOrCapability)]`. Read `JwtService.
   GenerateConsumerAccessToken` directly (`JwtService.cs:103-128`) — a consumer token's claims are
   `sub`/`role="consumer"`/`consumer_account_id`/`jti`(+optional `full_name`) only. No
   `tenant_id`, no `capabilities`, no `permissions` claim ever added. `RoleOrCapabilityHandler`
   requires either `AllowedRoles.Any(user.IsInRole)` (role is `"consumer"`, not in
   `CanViewAnalyticsRoles`) or a `capabilities` claim containing `marketing_analytics.view`
   (claim doesn't exist on this token type at all) — both branches fail, ASP.NET Core's
   authorization middleware rejects with 403 before the controller method ever runs.
2. **Controller**: `ResolveTenantId()` reads `User.FindFirst("tenant_id")` — always null on a
   consumer token — `Forbid()`s immediately even if (1) were somehow bypassed.
3. **RLS**: a consumer session's `TenantConnectionInterceptor` sets `app.consumer_account_id`,
   never `app.tenant_id` — `tenant_isolation`'s `NULLIF(current_setting('app.tenant_id', true),
   '')::uuid` is NULL, so the equality can never be true — fail-closed even if (1) and (2) were
   both bypassed.

Three independent, fail-closed layers — a consumer JWT cannot reach this feature by construction,
not merely by the absence of a discovered code path.

### 2. Excel/CSV formula injection (this codebase's own prior CRITICAL, TASK-412 #1) — **OK**

Read `ExcelExportService.cs` directly (not just trusted the "TASK-414 fix still applies" claim).
`SetCellValue`'s explicit `string` case AND the `.ToString()` fallback for every other type both
route through `SanitizeForSpreadsheet` (apostrophe-prefix on a leading `=`/`+`/`-`/`@`/Tab/CR) —
one centralized choke point, unchanged since TASK-414/422/431. `PostCampaignService.
ExportCustomersAsync`/`ExportUnknownTokensAsync` (`PostCampaignService.cs:610-654`) both build rows
and call `_excelExport.Export(new ExcelExportRequest(...))` — the same shared, protected service,
no second unguarded write path. Confirmed the brief's specific worry — customer names AND the raw
uploaded unknown/invalid tokens (externally-supplied free text, arguably the more direct injection
vector) — both flow through `SetCellValue`/`SanitizeForSpreadsheet` identically. No gap.

### 3. File upload handling — **NOT OK — 1 HIGH finding** (see "Additional findings" A below)

10 MB cap: `[RequestSizeLimit(10*1024*1024)]` on the action (`PostCampaignController.cs:67`) sets
`IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` via ASP.NET Core's own authorization-filter-
stage mechanism, which runs before model binding reads the body — this is genuinely enforced by
Kestrel at the transport level for the whole multipart body (file + `rawText` + other fields
together), not just declared; confirmed this app is Kestrel-behind-Nginx (Docker), not IIS
in-process, so the feature is not read-only and the attribute actually takes effect. The
`file.Length > MaxImportFileSizeBytes` check in the controller is redundant defense-in-depth on
top of that, not the real enforcement point. In-memory only confirmed: `file.CopyToAsync(memory)`
into a `MemoryStream`, `fileBytes = memory.ToArray()` — grepped the whole PostCampaign feature for
disk/temp-file writes, found none.

The extension allowlist (`.csv`/`.xlsx`/`.txt`, filename-based only) is **not** backed by any
content-sniffing, and — the real problem — **ClosedXML's `XLWorkbook` fully materializes the
entire uploaded workbook into an in-memory object graph with no row/column/size guard before or
during that materialization**, and `ExcelImportService.ParseXlsx` (`ExcelImportService.cs:16-37`)
then copies every cell into a `List<List<string?>>` with an unbounded nested loop
(`usedRange.RowsUsed()` × `usedRange.ColumnCount()`), all of this happening **before**
`SegmentImportParser.ParseDelimitedRows`/`PostCampaignService`'s `MaxAcceptedRows` (20,000) check
ever runs. See finding A for the concrete exploit shape and severity reasoning. No try/catch
anywhere around the `_excelImport.ParseXlsx(...)` call either (`PostCampaignService.cs:69-88`) —
confirmed via grep, zero `try`/`catch` in the whole file — so a malformed/mismatched-extension
file (e.g. a `.txt` renamed to `.xlsx`) throws an unhandled exception; `Program.cs` has neither
`UseDeveloperExceptionPage` nor `UseExceptionHandler` registered anywhere in the pipeline, so this
currently surfaces as a bare, generic 500 (not a stack-trace leak, but not a clean 400 either).

### 4. `SegmentImportParser` strict-parsing claim — **OK, independently re-verified against the source doc's own adversarial cases, not just the builder's tests**

Traced `Classify` (`SegmentImportParser.cs:186-212`) by hand against every case the brief and
source doc §5.3 name:
- **UUID must not split**: `Guid.TryParseExact(trimmed, "D", ...)` requires the full, exact
  standard format — a truncated/mangled UUID fragment fails outright and is never re-attempted as
  a partial match; a hex fragment containing `a`-`f` also fails the phone-shape character-class
  gate (only digit/`+`/space/`-`/`(`/`)` allowed), so it correctly falls to Invalid, never
  half-classified.
- **Decimal must not become two IDs**: `ParseTextList` only splits on `\n`/`,` — a period is not a
  delimiter, so `"12345.6789"` stays one whole token, fails the phone-shape gate (`.` not in the
  allowed set), and is rejected as ONE invalid token, never two numeric fragments.
- **Free text with embedded digits must not resolve to a phone**: the phone-shape gate is a
  character-class check that runs **before** `PhoneNormalizer.Normalize` is ever called — a token
  like `"invoice-0671234567-note"` contains letters, fails the gate immediately, and
  `PhoneNormalizer` (which strips all non-digits internally and would otherwise happily resurrect
  a phone from noise) never sees it at all.
- Read `PhoneNormalizer.cs` directly (`ShelfGuard.Application/Common/PhoneNormalizer.cs`) — it has
  no independent entry point reachable from arbitrary text; the only caller is `Classify`, gated by
  the character-class pre-check above.

Cross-checked this trace against `SegmentImportParserTests.cs` (29 tests) — every one of the above
cases has a dedicated, passing test (`...never_partially_matches_it`,
`...rejects_a_decimal_number_as_a_single_invalid_token_never_two_ids`,
`...never_extracts_a_phone_number_from_inside_arbitrary_text`,
`...rejects_a_negative_number_rather_than_silently_dropping_the_sign`), plus the
`UploadedCount == InvalidCount + DuplicateCount + UniqueValidTokens.Count` balance identity. No gap.

### 5. Raw-SQL/parametrization — **OK, confirmed by direct grep, not just the class doc comment**

`grep -rn "FromSqlRaw|SqlQueryRaw|ExecuteSqlRaw|ExecuteSqlInterpolated"` across the whole
`Features/MarketingAnalytics/PostCampaign/` app-layer tree and `PostCampaignRepository.cs`
specifically: **zero matches**. Read `PostCampaignRepository.cs` in full — every method is plain
EF Core LINQ (`Where`/`Select`/`ToListAsync`, in-memory `GroupBy` after materializing a small,
import-cap-bounded result set) against `PostCampaignSegment(s)`/`Customer`/`PosTransaction`. The
customer-table `sortBy` never reaches SQL either — `PostCampaignSortKeys.NormalizeCustomers`
allowlists to a fixed `HashSet`, and `PostCampaignService.ApplySort` is a pure in-memory LINQ
switch, not a SQL ORDER BY builder. No injection surface anywhere in this feature.

### 6. PII masking/export-capability gate parity — **OK**

`GetCustomersAsync` (`PostCampaignService.cs:391`): `canViewUnmaskedPii ? nameRow?.Phone :
PiiMasking.MaskPhone(nameRow?.Phone)` — same shared `PiiMasking` helper every sibling phase uses.
Controller (`PostCampaignController.cs:170`) computes `canViewUnmaskedPii =
MarketingAnalyticsAuthorization.CanExportPii(User)` server-side, ignoring client input on reads;
the export action ANDs the client's `UnmaskPii` flag with the same capability check — byte-for-byte
the same pattern as `PriceSegmentsController`/`AudienceBuilderController` (cleared in TASK-422/431).
No email exposure at all in this phase — `CustomerNameRow`/`PostCampaignCustomerRowDto` carry no
`Email` field, so the TASK-412 email-masking gap has no analog here (matches PriceSegments'
precedent, not Фаза 1's original miss).

Unknown-tokens export leak-check — traced `PostCampaignService.ImportAsync` precisely
(`PostCampaignService.cs:111-134`): `unknownSample`/`UnknownTokensSample` only ever receives
`token.RawText` inside the `if (resolved is null)` branch — a token that DID resolve to a real
customer always takes the `matchedIds.Add(...)` or `entityDuplicateCount++` path instead, never the
unknown-sample path. `InvalidTokensSample` comes from the parser's pre-DB classification pass, so
it definitionally never touched a real customer lookup. No matched-customer PII can leak into the
unknown-tokens export.

### 7. IDOR / cross-tenant on segment-scoped endpoints — **OK, stronger posture than the accepted KI-028 baseline**

Read every method in `PostCampaignRepository.cs`: `GetSegmentAsync`, `ListSegmentsAsync`,
`GetMemberCustomerIdsAsync`, `FindCustomersByIdsOrPhonesAsync`, `GetCustomerNamesAsync`, and the
shared `FetchRawLinesAsync` (backing both period-metrics and daily-turnover) **all** take an
explicit `tenantId` parameter and filter on it directly in the LINQ `Where` — this feature does not
rely on RLS as the sole layer for single-object reads the way KI-028 documents as an accepted
pattern elsewhere in this codebase; it has RLS **and** an explicit app-level tenant predicate on
every query, redundant defense-in-depth. `FindCustomersByIdsOrPhonesAsync` specifically confirms
the §36.2 "ID іншого tenant-а" case: a real Customer GUID belonging to a different tenant simply
never appears in the `Where (c.TenantId == tenantId && ...)` result, so it correctly falls into the
"unknown" bucket rather than cross-tenant-matching.

### 8. `PostCampaignAdvisor` (Claude/AI) path — **OK, one pre-existing-shape LOW note**

`ResolveAsync` (`PostCampaignAdvisor.cs:40-61`) only ever uses the API key server-side to construct
`AnthropicClient` — never returned to any caller. `BuildUserPrompt` (`PostCampaignAdvisor.cs:107-119`)
includes `MatchedCount`/rate percentages/counts and the already-shown template recommendation
strings — **no** raw uploaded token text (`UnknownTokensSample`/`InvalidTokensSample`) reaches the
prompt at all. The one free-text field that does: `segment.Name` (staff-typed at import time, e.g.
`"SMS блиц — серпень"`), interpolated unsanitized into `TitleUa` inside the **user**-role message
(the system prompt itself is a fixed constant, no interpolation). Same low-severity, accepted shape
as TASK-412's finding C on `MarketingAdvisor.BuildUserPrompt`'s `TopProductName` — staff-authored
text, one-way "explain this data" call with no tool access, blast radius limited to wording shown
back to the same authorized viewer. Not blocking, noting for completeness only.

### 9. 20,000-row import cap enforced before expensive work — **NOT OK — folds into finding A below**

`PostCampaignService.ImportAsync` (`PostCampaignService.cs:63-105`): for **both** the file branch
and the raw-text branch, `tokens.UploadedCount > MaxAcceptedRows` (line 87) is checked strictly
**after** `SegmentImportParser.ParseTextList`/`ParseDelimitedRows` has already tokenized and
classified every row, and — for the file branch — after `_excelImport.ParseXlsx`/`ParseCsvText`
has already read the entire payload into memory. For CSV/raw-text this is a bounded, cheap,
linear-time cost (the 10 MB request cap already bounds it) — not a real risk on its own. For XLSX
specifically, this is a genuine, unbounded resource-exhaustion gap — see finding A.

## Additional findings (beyond the 9-item list, found during independent code reading)

### A. HIGH — XLSX import path can be forced to fully materialize a very large in-memory dataset before the 20,000-row cap ever runs (zip-bomb-shaped resource exhaustion)

`ExcelImportService.ParseXlsx` (`ExcelImportService.cs:16-37`) calls `new XLWorkbook(stream)` —
ClosedXML is not a streaming reader; this call alone fully deserializes the whole workbook (every
sheet, row, and cell) into an in-memory object graph. The method then iterates
`usedRange.RowsUsed()` × `usedRange.ColumnCount()` with **no cap check of any kind** before or
during that loop, building a `List<List<string?>>` of every cell's string value. Only after this
entire structure is returned to `PostCampaignService.ImportAsync` does
`SegmentImportParser.ParseDelimitedRows` run, and only after *that* does the `MaxAcceptedRows`
(20,000) check reject the import.

Because `.xlsx` is a ZIP-compressed OOXML container, the uploaded **file size** (bounded to 10 MB,
correctly enforced — see item 3) does not bound the **decompressed/materialized** size. A workbook
with a very large number of rows that all reference the same entry in the shared-strings table
compresses extremely well (this is the well-known "XML/zip bomb" amplification pattern, not a
theoretical concern specific to this codebase — it is inherent to OOXML's shared-strings design)
and can plausibly stay under 10 MB on disk while expanding to a very large number of in-memory
.NET string objects once ClosedXML/`GetString()` materializes each cell individually. There is no
row/column count guard, no timeout, and no incremental "stop once we've seen too much" check
anywhere in the parse path — the existing `MaxAcceptedRows` constant, which this feature's own
design narrative (TASK-472's log) describes as chosen specifically to cap import cost, structurally
cannot do that job for the file-upload path because it is evaluated too late.

**Reachability**: requires an authenticated session that clears
`[Authorize(Policy = MarketingAnalyticsViewOrCapability)]` — not anonymous/public (unlike TASK-412's
CRITICAL finding). But per ADR-020's own design intent, that policy is explicitly satisfiable by
**either** `store_manager`+ **or** a delegated `marketing_analytics.view` capability grant on a
lower-ranked role — this feature's own class-level gate is a "view" floor, the same one that guards
every read-only report tab, not a stricter "manage/upload" floor (see finding B). So the population
able to trigger this is broader than "senior manager only" by this codebase's own stated
authorization design.

**Impact**: ShelfGuard is a modular monolith — one shared API process serves every tenant. A
memory-exhaustion or long-hang event triggered by one tenant's staff member uploading one crafted
file (well within the stated, correctly-enforced 10 MB limit) can degrade or crash request handling
for **all** tenants, not just the uploader's own — a materially worse blast radius than a
same-tenant-scoped bug. No global request-body-processing timeout exists anywhere in `Program.cs`
to bound this either.

This is new exposure, not a pre-existing pattern being inherited: Фаза 4 is the **first** feature
in the entire codebase that accepts an arbitrary uploaded file for parsing (Фаза 0-3's exports only
ever *produce* `.xlsx` files, via the already-hardened `ExcelExportService`; nothing before this
task ever consumed one). `IExcelImportService`'s own doc comment explicitly frames it as generic,
reusable infrastructure for "any future feature needing let the user upload an .xlsx" — so this gap
would silently propagate to every future caller if not fixed at this shared layer, not just patched
locally in `PostCampaignService`.

**Recommend fixing before shipping the import endpoint** (mirrors this exact series' own
TASK-412→TASK-414 pattern: review finds it, a scoped backend-developer task fixes it, a re-review
confirms): check `usedRange.RowCount()`/`ColumnCount()` immediately after computing `usedRange` and
reject before the cell-copying loop runs at all (a small multiple of `PostCampaignService.
MaxAcceptedRows` is a reasonable ceiling); apply the same early-exit discipline to
`SegmentImportParser.ParseCsvText`/`ParseTextList` for defense-in-depth even though CSV/text is
already bounded by the 10 MB cap; wrap the `_excelImport.ParseXlsx(...)` call in a try/catch that
returns a clean 400 instead of an unhandled exception for a malformed/mismatched-extension file.
Since `IExcelImportService` is shared, generic infrastructure, the row/column guard belongs in
`ExcelImportService.ParseXlsx` itself, not only in `PostCampaignService`'s call site.

### B. MEDIUM — No separate "upload" permission; import shares the same floor as read-only report viewing

`docs/uployal/AUDIENCE_ANALYSIS.md` §32 explicitly lists "окреме право на upload" (a separate
upload-specific permission) as a required control for this exact feature. `PostCampaignController`
applies one class-level policy (`MarketingAnalyticsViewOrCapability`) uniformly to every action —
`ListSegments`, `Import`, `Analyze`, all five report-tab GETs, `Explain`, and both exports all share
the identical floor. There is no narrower check on `Import` specifically. Combined with finding A,
this means the population that can trigger the resource-exhaustion risk is exactly the same
population that can merely *view* an already-analyzed report — not a smaller, more-trusted subset.
Not a blocker by itself (this mirrors the source doc's own §32 as an explicit ask rather than a
demonstrated exploit), but worth a follow-up: either a dedicated `marketing_analytics.import`-style
capability, or at minimum keep the class-level floor as-is only once finding A is fixed (a resource
guard makes the "who can reach Import" question much lower-stakes).

### C. LOW — Malformed/mismatched-extension `.xlsx` crashes the request instead of a clean 400

No `try`/`catch` exists anywhere around `_excelImport.ParseXlsx(...)` (confirmed by grep across
`PostCampaignService.cs`). A `.xlsx`-named file whose content isn't actually a valid OOXML zip
(or any other ClosedXML-rejected input) throws an unhandled exception. `Program.cs` registers
neither `UseDeveloperExceptionPage` nor `UseExceptionHandler` anywhere in its pipeline, so this
currently surfaces as a bare 500 with no stack-trace leak (not an information-disclosure issue) but
also no clean user-facing error. Low severity, but the fix belongs in the same pass as finding A.

### D. LOW / informational — Source doc §32 checklist: satisfied items vs. genuine gaps, for the record

- **"видалення upload після обробки" (delete upload after processing) / "шифрування тимчасових
  файлів" (temp-file encryption)**: vacuously satisfied — confirmed the raw file bytes are never
  written to disk or persisted to any DB column anywhere in this feature; they exist only as a
  transient `byte[]`/`MemoryStream` for the duration of one request, then are discarded. Nothing to
  delete or encrypt because nothing is ever stored.
- **"журнал завантажень" (upload log)**: satisfied — `ImportAsync` logs to `ActivityLog`
  (`marketing_analytics.post_campaign.import`) with `segmentId` + aggregate counts only, no raw
  tokens/PII in the log message (confirmed by reading `LogAsync`'s call sites directly — also
  satisfies the doc's separate "заборона ID у application logs" line item, interpreting "ID" as
  customer/personal identifiers, not this feature's own opaque internal `segmentId`).
- **"журнал перегляду звітів" (report-viewing log)**: genuinely absent — none of the five
  report-tab GETs write to `ActivityLog`. Checked whether this is a Фаза-4 regression: it is not —
  none of Фаза 1/2/3's own report GETs log views either (not flagged in TASK-412/422/431), so this
  is a pre-existing, systemic gap across the whole marketing-analytics module, not something this
  task introduced (Фаза 4 in fact logs strictly more than its predecessors, via the new import
  event). Noting for traceability since the source doc calls it out explicitly; not blocking this
  task specifically.
- **"антивірусна перевірка файлів" (antivirus scan)**: genuinely absent, and genuinely new exposure
  (Фаза 4 is this codebase's first file-upload feature at all, so there's no existing convention to
  compare against). Assessed impact as low despite the explicit source-doc ask: the uploaded bytes
  are parsed for text content only and immediately discarded (see above) — never persisted or
  re-served to any other user — so the classic "user A uploads a malicious file, user B downloads
  it" distribution vector does not exist in this feature's actual shape. Worth a follow-up ticket,
  not a blocker.
- **"контроль мети маркетингового профайлінгу" (purpose-limitation policy control)**: a
  legal/policy concept, not a code-expressible control; out of scope for this review, consistent
  with every prior phase in this series never attempting to encode it.

## Overall verdict

**NOT clear to ship the import endpoint as-is.**
- **HIGH — blocks shipping the import path**: Finding A (XLSX resource exhaustion; the 20,000-row
  cap is enforced too late to protect against it). Requires authenticated
  `marketing_analytics.view`-or-capability access, not anonymous — but the blast radius (shared
  multi-tenant API process) and the direct contradiction of this feature's own stated cap-timing
  design intent both warrant fixing before general rollout, same bar TASK-412 applied to its own
  CRITICAL finding on this module's very first export feature. Recommend a scoped
  backend-developer follow-up (mirrors TASK-412→TASK-414): add a row/column guard to
  `ExcelImportService.ParseXlsx` before it copies cells, and wrap the parse call in a try/catch
  returning a clean 400. Then re-review.
- **MEDIUM — should fix, not a hard blocker**: Finding B (no separate upload permission; source
  doc §32 explicit ask, currently shares the "view" floor with read-only report tabs).
- **LOW, non-blocking**: Finding C (malformed file crashes instead of clean 400 — same fix pass as
  A), Finding D's genuinely-absent items (report-view audit logging — pre-existing/systemic, not
  new; antivirus scanning — new but low actual impact given files are never stored/re-served), the
  AI-prompt segment-Name note (item 8).
- **Everything else — items 1, 2, 4, 5, 6, 7, and finding D's satisfied items: OK/CLEAR.** RLS,
  formula injection, the strict import parser, raw-SQL parametrization, PII masking parity, and
  tenant isolation are all independently verified sound, live-tested where applicable (RLS), and in
  several cases (parser, IDOR posture) stronger than this codebase's own accepted baseline
  elsewhere.

No fixes applied in this pass (audit only, per the brief). Recommendation: spawn a narrow
backend-developer task for finding A (+ C as the same-pass cleanup), re-review, then proceed to
documentation-writer + qa-tester as originally planned. Finding B can ride along with the same fix
task or be tracked separately at the user's discretion.

## Files reviewed (no changes made)

- `backend/ShelfGuard.Domain/Entities/PostCampaignSegment.cs`,
  `PostCampaignSegmentMember.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260805190701_AddPostCampaignSegmentSchema.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/PostCampaign/**` (service, DTOs,
  `SegmentImportParser.cs`, `PostCampaignSortKeys.cs`, `IPostCampaignRepository.cs`)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/PostCampaignRepository.cs`
- `backend/ShelfGuard.Infrastructure/Export/ExcelImportService.cs`,
  `ExcelExportService.cs` (re-verified, precedent comparison)
- `backend/ShelfGuard.Application/Common/IExcelImportService.cs`, `PhoneNormalizer.cs`
- `backend/ShelfGuard.Api/Controllers/PostCampaignController.cs`
- `backend/ShelfGuard.Infrastructure/AI/PostCampaignAdvisor/PostCampaignAdvisor.cs`
- `backend/ShelfGuard.Infrastructure/Authorization/AppPolicies.cs`,
  `MarketingAnalyticsAuthorization.cs`, `RoleOrCapabilityHandler.cs`, `TenantRoleAuthorization.cs`
- `backend/ShelfGuard.Infrastructure/Services/JwtService.cs` (consumer-token claim shape)
- `backend/ShelfGuard.Api/Program.cs` (request pipeline, exception handling, size limits)
- `backend/ShelfGuard.Tests/MarketingAnalytics/PostCampaign/SegmentImportParserTests.cs`,
  `PostCampaignServiceTests.cs` (cross-checked, not just read the builder's summary of them)
- `frontend/features/marketing-analytics/post-campaign/api/postCampaign.ts`,
  `components/ImportPanel.tsx`, `components/ValidationSummary.tsx`

## Git

Not committed (repo convention — main session/user commits; this is a docs-only log file).
