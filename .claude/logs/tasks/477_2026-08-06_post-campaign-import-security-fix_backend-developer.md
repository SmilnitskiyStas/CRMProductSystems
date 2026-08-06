# TASK-477: Security remediation — Фаза 4 post-campaign import (3 findings from TASK-474)

**Agent:** backend-developer
**Date:** 2026-08-06
**Status:** done — **all 3 findings fixed and verified.** No blockers.

## Scope

Fixed exactly the 3 findings from
`.claude/logs/tasks/474_2026-08-05_post-campaign-security-review_security-reviewer.md`: HIGH
finding A (XLSX import resource exhaustion), LOW finding C (malformed file crashes to a bare 500),
MEDIUM finding B (Import shared the same floor as read-only report tabs). Same pattern as this
series' own TASK-412→TASK-414 precedent (review finds it, scoped fix task closes it). Nothing else
touched — RLS, the formula-injection sanitizer, the strict parser's `Classify`, the raw-SQL-free
repository, PII masking parity, and IDOR posture were all marked OK by the review and left exactly
as found.

## 1. HIGH — Finding A: XLSX import resource exhaustion (fixed)

Root cause was exactly as diagnosed: `ExcelImportService.ParseXlsx` copied every cell into a
`List<List<string?>>` with no size guard, and `PostCampaignService.MaxAcceptedRows` (20,000) was
only checked afterward — too late to bound a small, highly-compressible `.xlsx` exploiting OOXML's
shared-strings table.

Added `ImportLimits` (`ShelfGuard.Application/Common/ImportLimits.cs`) — `MaxRows = 25_000`,
`MaxColumns = 300` — a shared ceiling (~1.25x `MaxAcceptedRows`, "a small multiple" per the
review's own suggestion) deliberately more generous than the real 20,000 business cap, so a
submission just a bit over that real limit still reaches `PostCampaignService`'s own friendlier
"20,000 max" message instead of the generic one; only drastically oversized input hits this
ceiling. `ExcelImportService.ParseXlsx` now reads `usedRange.RowCount()`/`ColumnCount()` (cheap
bounding-box address arithmetic, already available once the range is computed) and throws a new
`ImportTooLargeException` (`ShelfGuard.Application.Common`) **before** the per-cell `GetString()`
copy loop runs at all — not after. Applied the identical early-exit discipline to
`SegmentImportParser.ParseTextList`/`ParseCsvText` per the review's explicit ask (checks raw
token/line count before the per-token `Classify()` pass or per-line `Split(',')` materialization)
— defense in depth; that path was already bounded by the controller's 10 MB request cap, but now
fails with the same clear "too many" error shape as the XLSX path instead of one path failing
early and the other late. Column-count guard is XLSX-only (documented in `ImportLimits`'s own doc
comment why): CSV/text never does a rows×columns nested materialization, so a column cap there
would guard nothing.

`PostCampaignService.ImportAsync` catches `ImportTooLargeException` from either the file branch
(XLSX or CSV) or the raw-text branch and returns the same `(null, message)` shape every other
validation failure in this method already uses, rather than letting it propagate.

Files: `backend/ShelfGuard.Application/Common/ImportLimits.cs` (new),
`backend/ShelfGuard.Application/Common/IExcelImportService.cs` (added `ImportTooLargeException`),
`backend/ShelfGuard.Infrastructure/Export/ExcelImportService.cs`,
`backend/ShelfGuard.Application/Features/MarketingAnalytics/PostCampaign/SegmentImportParser.cs`,
`backend/ShelfGuard.Application/Features/MarketingAnalytics/PostCampaign/PostCampaignService.cs`.

## 2. LOW — Finding C: malformed/mismatched-extension file crashes to a bare 500 (fixed)

Determined ClosedXML 0.105.1's *actual* thrown exception types empirically (throwaway probe
tests, same verification discipline TASK-414 used for the quote-prefix behavior) rather than
guessing, per the brief's explicit ask:

- Corrupt bytes / empty stream → `System.IO.FileFormatException`, which **is** a `FormatException`
  subtype (confirmed via the real inheritance chain, not assumed) — the existing interface doc
  comment's claim ("throws `FormatException`") turned out to already be correct for this case.
- A well-formed zip that isn't a valid OOXML spreadsheet package (e.g. a `.txt`/other file simply
  renamed to `.xlsx`, or any zip missing the expected workbook part) → a bare
  `NullReferenceException` thrown from inside ClosedXML's own `LoadSpreadsheetDocument` — a real,
  if unfortunate, ClosedXML behavior for that input shape, not something introduced here.

`PostCampaignService.ImportAsync`'s file branch now wraps the `_excelImport.ParseXlsx`/
`SegmentImportParser.ParseCsvText` call in `catch (Exception ex) when (ex is FormatException or
NullReferenceException)`, returning the same clean `(null, message)` shape — narrow and
evidence-based, not a bare `catch (Exception)`, and scoped to only that call (not the subsequent
`ParseDelimitedRows` call, which is this codebase's own well-tested pure function and stays
outside the catch so a genuine bug there would still surface rather than being mislabeled as "bad
file"). Combined naturally with finding A's fix — both are "reject this import cleanly" outcomes
in the same try/catch.

Files: `backend/ShelfGuard.Application/Features/MarketingAnalytics/PostCampaign/PostCampaignService.cs`.

## 3. MEDIUM — Finding B: Import shared the view floor with read-only report tabs (fixed)

**Decision: role-only floor, no new capability** (documenting the reasoning, per the brief's
explicit ask). Considered both options the brief laid out:

- A new `marketing_analytics.import`-style `TenantRoleCapabilities` entry (mirrors
  `MarketingAnalyticsExportPii`'s own shape), vs.
- Reusing `AppPolicies.AtLeastStoreManagerRoles` directly (matching `CanExportPii`'s own default
  floor) with no capability-widening escape hatch.

Went with the second, minimal option. Two precedents in this exact codebase point the same
direction: (1) `TenantRoleCapabilities.ReceiptsView`'s own doc comment explicitly excludes
write-heavy actions (Create/Receive/Cancel) from the capability catalog on purpose ("ADR-020 point
3") rather than growing it for every new mutating endpoint; Import is exactly that shape — it
creates DB rows and, per finding A, is this controller's single most resource-costly action. (2)
`SuppliersManageOrCapability` already establishes that a stricter *mutating* action within a
family of otherwise-view-gated actions gets a **stricter role floor**, not merely an optional
capability layered on the same floor as view. A tenant that genuinely needs a sub-store_manager
role to import segments can still grant that role `store_manager`; a dedicated capability can be
added later if that specific need actually materializes, rather than speculatively now.

Added `MarketingAnalyticsAuthorization.CanImportSegments(ClaimsPrincipal)` — same imperative,
in-body-check shape as `CanExportPii` (needed for the same reason: narrows one action within an
otherwise class-level-gated controller, so it can't be a blanket `[Authorize]` policy attribute) —
returning `AppPolicies.AtLeastStoreManagerRoles.Any(user.IsInRole)`, no capability branch.
`PostCampaignController.Import` now checks it right after resolving `tenantId`/`userId` and
returns `Forbid()` (confirmed 403 for this app's JWT bearer scheme — same pattern already used
elsewhere in this exact controller for the missing-`tenant_id` case) rather than a silent no-op.

Files: `backend/ShelfGuard.Infrastructure/Authorization/MarketingAnalyticsAuthorization.cs`,
`backend/ShelfGuard.Api/Controllers/PostCampaignController.cs`.

## Verification

`dotnet build` — 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`, not
touched here, same warning TASK-414 also noted). `dotnet test` (full suite) — **1308/1308 green**
(was 1289 after TASK-472; net +19, all new, zero regressions).

New tests:
- `ExcelImportServiceTests.cs` (new file, 6 tests): row-ceiling and column-ceiling rejection
  (built via a sparse 2-cell workbook whose bounding box crosses the ceiling, so the test stays
  fast while still proving the check reads the range's bounding box rather than needing to
  populate every cell), a within-ceiling workbook still parses correctly, and the 3
  malformed-input shapes (garbage bytes, empty stream, valid-zip-not-xlsx) pinned against their
  real, empirically-confirmed exception types.
- `PostCampaignServiceTests.cs` (+5 tests): `ImportAsync` correctly translates a mocked
  `ImportTooLargeException`/`FormatException`/`NullReferenceException` from `_excelImport.ParseXlsx`
  into the clean error shape without touching the repository; a real (unmocked) oversized raw-text
  paste and an oversized `.csv` file both reject via `SegmentImportParser`'s own new early-exit
  guards (proving the defense-in-depth path independently of the XLSX path).
- `MarketingAnalyticsAuthorizationTests.cs` (+8 tests): `CanImportSegments` true for
  store_manager/network_manager/enterprise_admin/provider, false for cashier/merchandiser, and —
  the two tests that matter most for this design decision — false even when the caller holds the
  `marketing_analytics.view` or `marketing_analytics.export_pii` capability claim, pinning that
  this permission has no capability-based bypass at all.

Did not touch anything the review marked OK (RLS, formula injection, strict parser's core
`Classify`, raw-SQL absence, PII masking, IDOR posture) or anything outside these 3 findings (no
`.claude/docs/` changes — none of these fixes change documented architecture/domain behavior, only
close a security gap in already-documented behavior). Not committed (repo convention — main
session/user commits).

## Addendum: empirical verification of finding A's fix (main session follow-up)

**Trigger:** doubt that the row/column guard above actually closes finding A, since it runs
*after* `new XLWorkbook(stream)` — if that constructor alone already fully materializes the
workbook, checking `RowCount()`/`ColumnCount()` afterward is too late. Settled empirically, not by
reasoning about ClosedXML's docs.

**Method:** a throwaway xUnit probe (deleted after use, not committed) built synthetic .xlsx files
via targeted ZIP-entry surgery on a ClosedXML-produced template — `xl/worksheets/sheet1.xml`'s
`<sheetData>` replaced with N rows, one column, every cell referencing shared-string index 0 (the
classic OOXML zip-bomb shape: tiny compressed file, large decompressed XML). Generation streamed
directly into the ZIP entry (never materialized as one big .NET string), so it stayed fast even at
N in the millions. Measured `new XLWorkbook(stream)` ALONE via `Stopwatch`, isolated from
everything after it, plus `GC.GetTotalAllocatedBytes`/`GC.GetTotalMemory(true)`/
`Process.WorkingSet64` before/after. Release build, .NET 8, ClosedXML 0.105.1.

**Results:**

| rows | file on disk (compressed) | worst ZIP entry, uncompressed | `new XLWorkbook(stream)` alone | `RangeUsed()`+`RowCount()`+`ColumnCount()` |
|---|---|---|---|---|
| 25,000 (today's `MaxRows`) | 0.12 MB | 2.0 MB | 374 ms / 41.6 MB allocated | 38 ms / 14.8 MB allocated |
| 250,000 | 1.17 MB | 20.1 MB | 4,866 ms / 410.8 MB allocated | 627 ms / 139.0 MB allocated |
| 1,000,000 | 4.63 MB | 80.9 MB | 40,094 ms / 1,724.3 MB allocated | 2,786 ms / 571.7 MB allocated |
| 1,048,576 (Excel's own hard row ceiling — the true worst case for 1 column) | 4.86 MB | 84.9 MB | 37,703 ms / 1,725.8 MB allocated (~496 MB retained live after a forced GC) | 2,263 ms / 586.1 MB allocated |

All 4 sizes passed data-integrity assertions (correct row/column counts, correct cell values at
both ends of the sheet) — not measuring an empty/truncated parse.

**Verdict: the fix above does NOT close finding A.** The security review's original claim was
correct: `new XLWorkbook(stream)` alone performs the full, expensive per-cell materialization,
entirely before `RowCount()`/`ColumnCount()` get a chance to run. A file comfortably under the
10 MB upload cap (here, ~4.9 MB) costs ~38-41 seconds of wall time and ~1.7 GB allocated *in the
constructor call by itself*, in a shared multi-tenant API process. The guard added here only ever
bounded the second-pass `GetString()` copy loop, which was never the expensive part. Cost also
scales super-linearly with row count (~10x rows from 25k→250k gave ~13-15x ctor time; ~4x rows
from 250k→1M gave ~8-11x ctor time), so the exposure gets disproportionately worse as an attacker
pushes closer to what the 10 MB cap and Excel's own 1,048,576-row ceiling allow.

**Fix applied** (this is no longer just row A's original fix — it now has two layers):

Added `ImportLimits.MaxUncompressedZipEntryBytes` (20 MB) and a new
`ExcelImportService.GuardAgainstOversizedZipEntries` check that runs *before*
`new XLWorkbook(stream)`: a .xlsx is a standard ZIP container, so
`System.IO.Compression.ZipArchive` + `ZipArchiveEntry.Length` (uncompressed size) can inspect every
part's real size without ever invoking ClosedXML. Empirically confirmed this pre-check is
genuinely cheap — 0-9 ms even against an entry that decompresses to 85 MB — because `.Length` reads
size metadata off the ZIP central directory; no decompression happens just to read it. 20 MB was
chosen with real headroom over the only known real caller's legitimate shape (`PostCampaignService`
— a handful of columns, well under the 25,000-row `MaxRows` ceiling, measured at ~2.0 MB
uncompressed at that ceiling for 1 column) while firmly rejecting the demonstrated attack sizes
(250k rows sits right at ~20.1 MB; 1M+ rows at 80-89 MB is 4x+ over). Documented as re-tunable, same
as `MaxRows`/`MaxColumns`, if a future caller needs a wider/denser legitimate workbook.

Since `ParseXlsx(Stream stream)` now needs to read the same bytes twice (ZIP pre-check, then
`XLWorkbook`), and the interface is deliberately generic (not guaranteed to always receive an
already-buffered, seekable `MemoryStream` the way today's one real caller happens to), the method
now buffers internally only when `stream.CanSeek` is false, otherwise reuses the caller's own
seekable stream directly (`Position = 0` between the two reads) — avoids an unconditional double-
buffer for the common case while staying correct for a non-seekable input.

Also verified empirically (small separate probe) that `System.IO.Compression.ZipArchive`'s
constructor throws `System.IO.InvalidDataException` — not a `FormatException` — for both malformed
shapes the existing tests already pin (garbage bytes: "End of Central Directory record could not be
found"; empty stream: "Central Directory corrupt"), and does NOT throw at all for a well-formed
zip that isn't a valid xlsx package. The new guard catches exactly `InvalidDataException` and falls
through to `XLWorkbook`'s own (already-tested) `FileFormatException`/`NullReferenceException`
handling for those cases, so finding C's existing exception contract is unchanged — confirmed by
all 3 pre-existing malformed-input tests passing unmodified.

**Files changed:**
- `backend/ShelfGuard.Application/Common/ImportLimits.cs` — added `MaxUncompressedZipEntryBytes`.
- `backend/ShelfGuard.Application/Common/IExcelImportService.cs` — doc comments corrected to
  describe the two-layer guard (ZIP pre-check, then row/column check) instead of implying the
  row/column check alone was sufficient.
- `backend/ShelfGuard.Infrastructure/Export/ExcelImportService.cs` — added
  `GuardAgainstOversizedZipEntries`, called before `new XLWorkbook(...)`; added seekable-stream
  handling.
- `backend/ShelfGuard.Tests/Infrastructure/ExcelImportServiceTests.cs` — 2 new permanent tests:
  rejects an oversized ZIP entry (fast — repeated-byte content compresses near-instantly despite a
  >20 MB declared uncompressed size), and correctly parses via a non-seekable input stream.
- Throwaway probes (ZIP-structure dump, the cost-measurement probe above, the
  `ZipArchive`-exception-type probe) were deleted after use — not committed, no permanent trace.

**Verification:** `dotnet build` (full solution) — 0 errors, same 1 pre-existing unrelated warning
(`MarketplaceServiceTests.cs`). `dotnet test` (full suite) — **1310/1310 green** (1308 baseline +
2 new; zero regressions, all 6 pre-existing `ExcelImportServiceTests` pass unmodified).
