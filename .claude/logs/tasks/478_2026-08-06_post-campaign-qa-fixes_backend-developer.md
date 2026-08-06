# TASK-478: Fix 2 confirmed Фаза 4 QA findings (phone import matching + export truncation)

**Agent:** backend-developer
**Date:** 2026-08-06
**Status:** done — both fixes implemented and verified live (real Postgres + real browser E2E),
not just unit-mocked. No blockers.

## Scope

Fixed exactly the 2 confirmed, in-scope bugs from TASK-476's QA acceptance pass (Фаза 4
post-campaign audience analysis), per their full writeups:
`.claude/logs/reviews/bug-task476-phone-import-matching-format-mismatch_2026-08-06.md`,
`.claude/logs/reviews/bug-task476-unknown-tokens-export-capped-at-20_2026-08-06.md`. The 3rd
finding from that same pass (TenantRole capabilities never reaching the JWT) is a separate,
pre-existing platform bug — explicitly out of scope, filed as KI-030, not touched here.

## 1. HIGH — phone-based import matching only worked for canonical-format stored phones (fixed)

Root cause was exactly as diagnosed: `PostCampaignRepository.FindCustomersByIdsOrPhonesAsync` did
raw string equality between `candidatePhones` (always normalized to `+380XXXXXXXXX` by
`PhoneNormalizer.Normalize` at import-parse time) and `Customer.Phone` **as stored** —
`CustomerService`'s write path never normalizes it (`.Trim()` only, intentionally permissive
regex). Live-confirmed in the QA session: 13 of 14 real customers in tenant "Свіжий Кут" have
`Phone` stored without a leading `+` (e.g. `"380501110001"`), so their correctly-typed phone
numbers silently resolved to "unknown."

**Fix, scoped entirely to `PostCampaignRepository.cs`** (per the brief — no changes to
`Customer`/`CustomerService`/`AutoServiceService`, no migration): the ID and phone candidates are
now resolved as two separate passes instead of one combined `Where`. Pass 1 (unchanged) matches by
`Customer.Id`. Pass 2 fetches every tenant `Customer` row with a non-null `Phone`, EXCLUDING rows
already matched in Pass 1 (`!candidateIds.Contains(c.Id)` — prevents a duplicate-Id row when the
same customer is submitted as both a GUID and a phone token, which would otherwise throw on
`PostCampaignService`'s own `found.ToDictionary(c => c.Id)`), normalizes each stored `Phone` in C#
via `PhoneNormalizer.Normalize` (the exact same function the import parser itself uses — zero risk
of a second, drifting implementation), and matches against the already-normalized
`candidatePhones` set. **Both passes** now return the normalized phone in `MatchedCustomerRow.Phone`
(not just the phone-matched pass) — otherwise a customer submitted as both a GUID and a
non-canonical phone in the same import would resolve via the GUID but still show the phone token as
"unknown," since `PostCampaignService`'s `byPhone` dictionary is keyed off this field. No SQL-side
regex/computed-column was added (per the brief's explicit direction); the phone-side query is
skipped entirely when `candidatePhones` is empty (no behavior change for GUID-only imports).

Also corrected `IPostCampaignRepository.FindCustomersByIdsOrPhonesAsync`'s doc comment, which
previously documented the old exact-string-match behavior as an accepted, known limitation
("same known limitation `LoyaltyService` already accepts") — now describes the actual
client-side-normalization behavior.

Files: `backend/ShelfGuard.Infrastructure/Data/Repositories/PostCampaignRepository.cs`,
`backend/ShelfGuard.Application/Features/MarketingAnalytics/PostCampaign/IPostCampaignRepository.cs`.

**New test:** `backend/ShelfGuard.Tests/Infrastructure/PostCampaignRepositoryIntegrationTests.cs`
(new file, 3 tests, live Postgres — the exact missing test category the QA report flagged, since
every existing PostCampaign test mocks the repository). Seeds one tenant with customers whose
`Phone` is stored in 5 different valid formats (canonical `+380...`, no leading `+`, local
10-digit, dashed, bare 9-digit) plus a null-phone and an unparseable-phone customer, and proves:
all 5 stored formats now match when searched via their (differently-formatted) normalized
candidate phone; a customer submitted via both GUID and phone in one call collapses to exactly one
row (no duplicate-key throw); an empty `candidatePhones` list leaves GUID-only matching unchanged.

## 2. MEDIUM-HIGH — unknown/invalid-tokens export silently capped at 20+20, no truncation signal (fixed)

Root cause was exactly as diagnosed: `PostCampaignSegment.UnknownTokensSample`/`InvalidTokensSample`
are the ONLY place these tokens are ever stored, both capped at 20 **at import time**
(`SegmentImportParser.InvalidSampleCap`, `PostCampaignService.ImportAsync`'s inline unknown-sample
cap) — permanently, with no way to recover anything past the first 20 later. The export just
dumped whatever was in these capped fields with zero indication the file might be incomplete.

**Fix, three parts:**

1. **Raised both caps 20 → 500** (`SegmentImportParser.InvalidSampleCap`, and a new
   `PostCampaignService.UnknownSampleCap` replacing the previous inline `20` literal). Chose 500
   (not a shared constant — the two conceptually cap different things, invalid-format vs.
   well-formed-but-unmatched tokens, kept at the same value by convention/doc-comment cross-
   reference): still a trivially cheap JSONB `List<string>` column even at the import's own
   20,000-row ceiling, and covers the overwhelming majority of realistic real-world counts with
   zero truncation at all.
2. **Made the export response honest about whatever cap remains.** Added a new
   `PostCampaignUnknownTokensExportResult` DTO (`TotalUnknownCount`/`TotalInvalidCount` — the
   segment's real, uncapped counts — and `Truncated`), replacing the generic
   `PostCampaignExportResult` for this one export method only (that generic type's own `Truncated`
   field means something unrelated — `IExcelExportService`'s own 50k-row ceiling — and would have
   been silently wrong here). `IPostCampaignService.ExportUnknownTokensAsync`'s signature updated
   to match.
3. **Reflected truncation in the actual exported file**, not just an API field nobody could see:
   when `sampleTruncated` (`UnknownTokensSample.Count < UnknownCount || InvalidTokensSample.Count <
   InvalidCount`), `PostCampaignService.ExportUnknownTokensAsync` now inserts a note row as the
   FIRST data row (mirrors `IExcelExportService.Export`'s own truncation-banner placement —
   immediately after the header — since that shared service's own banner logic only ever fires on
   its unrelated 50k-row ceiling and can't know about this feature's own, much-lower sample cap).
   Also set 3 new response headers on the controller action
   (`X-Total-Unknown-Count`/`X-Total-Invalid-Count`/`X-Sample-Truncated`) — directly answers the
   QA report's own literal repro complaint ("no X-Total-Rows/X-Truncated/any other signal... via
   curl -D -"); not yet in `Program.cs`'s CORS `ExposedHeaders` (out of this task's file scope), so
   only a same-origin/server/curl caller can read them today, not browser JS cross-origin — the
   frontend fix below is deliberately self-sufficient and doesn't depend on these headers.

**Frontend** (`ValidationSummary.tsx`, `en.json`/`uk.json` — only files touched): replaced the old
static `exportErrorsHint` ("Contains up to 20 unknown + 20 invalid tokens") with two conditional
messages computed **client-side from data the import response already has** — no new request, no
header-reading needed: `unknownTokensSample.length < unknownCount || invalidTokensSample.length <
invalidCount` decides between "Contains all N unknown + M invalid tokens" and "Showing X of N
unknown + Y of M invalid tokens — the rest weren't saved at import time," matching this feature's
existing "never imply completeness" standard (`CustomerTable`'s own total-count footer). **Side
effect of raising the backend cap 20→500 that needed handling**: the on-screen chip list would
otherwise render up to 500+500 badges inline (it previously rendered the whole, always-≤20 sample
directly). Capped the ON-SCREEN preview at the old 20 regardless of how large the underlying
sample now is, with a small "+N more — see the exported file" note when the persisted sample
exceeds what's shown — the export button/file still uses the full (up to 500) sample.

Files: `backend/ShelfGuard.Application/Features/MarketingAnalytics/PostCampaign/SegmentImportParser.cs`,
`.../PostCampaignService.cs`, `.../Dtos/PostCampaignDtos.cs`, `.../IPostCampaignService.cs`,
`backend/ShelfGuard.Api/Controllers/PostCampaignController.cs`,
`frontend/features/marketing-analytics/post-campaign/components/ValidationSummary.tsx`,
`frontend/messages/en.json`, `frontend/messages/uk.json`.

**New/updated tests:**
- `SegmentImportParserTests.cs` — updated `InvalidTokensSample_is_capped_at_500_...` (was pinned
  to the old 20; now proves 520 submitted → 500 sampled, 520 counted).
- `PostCampaignServiceTests.cs` — updated the existing export test with realistic
  count-matches-sample fixture data + new-field assertions (`Truncated=false`,
  `TotalUnknownCount`/`TotalInvalidCount`); added a new test proving `Truncated=true` and the
  inserted note row when the real count exceeds the sample.

## Verification

`dotnet build` — 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`, not
touched here). `dotnet test` (full suite) — **1314/1314 green** (1310 baseline + 4 new: 3
integration + 1 unit; zero regressions). `npx tsc --noEmit` — 0 errors. `npm run lint` — 0
warnings. `npm run build` — exit 0, `/marketing-analytics/post-campaign` 15.6 kB / 257 kB First
Load JS (was 15.4 kB — expected small bump from the new logic/i18n keys); only non-zero "Error"
text in the log is the pre-existing, already-flagged-harmless `ENVIRONMENT_FALLBACK` noise.

**Live E2E re-verification** (beyond the unit/integration suite), same dev stack and tenant
("Свіжий Кут") the QA session used, backend + frontend dev servers started and stopped cleanly for
this session only:
- Browser: imported `050-111-00-01` (a real customer's phone, stored as `380501110001` — a
  *different* textual format than what's stored) alongside the already-working canonical control
  case, one genuinely-unknown phone, and one invalid token. Result: `Matched: 2` (previously would
  have been `Matched: 1, Unknown: 2`) — the exact bug live-reproduced-then-fixed, not just asserted
  in a test. `ValidationSummary` correctly rendered "Contains all 1 unknown + 1 invalid tokens."
  (non-truncated case).
- curl (bypassing browser CORS, confirming the headers are genuinely sent): exported that same
  segment → `X-Total-Unknown-Count: 1`, `X-Total-Invalid-Count: 1`, `X-Sample-Truncated: false`.
  Also re-exported the QA session's own leftover `QA476-many-unknown-invalid` segment (real
  `UnknownCount=25, InvalidCount=24`, sample frozen at 20/20 from before this fix existed) →
  `X-Total-Unknown-Count: 25`, `X-Total-Invalid-Count: 24`, `X-Sample-Truncated: true`; unzipped the
  resulting `.xlsx` and confirmed the note row's exact text is present in `sharedStrings.xml`
  ("Показано 20 з 25 невідомих та 20 з 24 некоректних токенів...").

Did not touch `Customer.cs`, `CustomerService.cs`, `AutoServiceService.cs`, or add any migration —
both fixes stayed entirely inside the PostCampaign feature's own files, per the brief. Not
committed (repo convention — main session/user commits).
