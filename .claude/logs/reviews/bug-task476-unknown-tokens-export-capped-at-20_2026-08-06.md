# TASK-476 defect — post-campaign "unknown/invalid tokens" export is silently capped at 20+20, not the complete list

**Date:** 2026-08-06
**Severity:** medium-high (silent, undetectable data loss in a feature whose whole point is a
complete, downloadable error report — no truncation indicator anywhere in the response)
**Task:** TASK-476 (found during E2E acceptance of Фаза 4 post-campaign audience analysis)
**Status:** open — not fixed (QA reports, does not fix, per this task's brief)

## Bug

`POST /api/marketing-analytics/post-campaign/segments/{id}/exports/unknown-tokens` — the
"downloadable error report" source doc §8.2 explicitly calls for — never contains more than 20
unknown tokens + 20 invalid tokens (40 data rows total), **regardless of the segment's real
`UnknownCount`/`InvalidCount`**, and gives the caller no signal that anything was cut. This directly
contradicts the brief's own item 3 requirement ("the full error-report export downloads and
contains the complete lists, not just the first-20 UI sample") and the source doc's own repeated
theme (§23.3/§35.1) that a full, uncapped export is the explicit fix over the competitor's
Top-200-no-export limitation — Фаза 4 was specifically supposed to not repeat this class of cap.

## Root cause

`PostCampaignSegment.UnknownTokensSample`/`InvalidTokensSample` are the **only** place these tokens
are ever stored, and both are capped to 20 at **import time**, permanently:
- `SegmentImportParser.cs:55,178` — `InvalidSampleCap = 20`, enforced while parsing.
- `PostCampaignService.cs` `ImportAsync` — `if (unknownSample.Count < 20) unknownSample.Add(...)`
  while resolving customers.

`ExportUnknownTokensAsync` (`PostCampaignService.cs:669-690`) builds its export rows directly from
`segment.UnknownTokensSample`/`segment.InvalidTokensSample` — there is no other storage anywhere
(matched customers get a full, uncapped row per `PostCampaignSegmentMember`; unknown/invalid raw
tokens get none). By the time a user clicks "export," the tokens beyond the first 20 of each kind
were **already discarded at import time** and cannot be reconstructed — not a bug in the export
method's own logic so much as a data-modeling gap one layer up (this was already documented,
apparently without flagging the export consequence, in TASK-471's own schema log: "capped ~20 by
the service layer, not enforced in schema").

## Live repro (2026-08-06, dev stack)

Segment `QA476-many-unknown-invalid` (created by an earlier session in this same task):
`UploadedCount=49, UnknownCount=25, InvalidCount=24`. Downloaded
`POST .../exports/unknown-tokens` → parsed the returned `.xlsx` byte-for-byte (ZIP → shared strings
+ row count, same technique this series' TASK-433 used):

- `<x:row>` count: **41** (1 header + 40 data rows) — not 50 (1 header + 49 data rows).
- Shared-strings dump: token values run `999110000`...`999110019` (exactly 20 distinct unknown
  tokens) and `invalid-token-001-abc`...`invalid-token-020-abc` (exactly 20 distinct invalid
  tokens) — confirms exactly 20+20, matching the cap theory precisely, not the real 25+24 counts.
- Response headers (`curl -D -`): no `X-Total-Rows`/`X-Truncated`/any other signal that the file is
  incomplete — filename is a plain `post_campaign_import_errors_<timestamp>.xlsx` with nothing
  indicating a sample. `PostCampaignExportResult.Truncated` (the field that DOES exist on this DTO)
  reflects only `IExcelExportService`'s own unrelated 50,000-row ceiling — irrelevant here since 40
  rows is nowhere near it — so even that field reports `false` on a file that is, in fact, missing
  9 real tokens (5 unknown + 4 invalid) in this specific test, and would silently miss far more on
  a realistic large campaign list (e.g. thousands of unknown IDs).

## Expected

Either the export genuinely contains every unknown/invalid token from the import (source doc's own
ask), or — if a hard cap is intentionally kept for cost/size reasons — the response should say so
explicitly (a `truncated`/`totalUnknownCount`/`totalInvalidCount` field the frontend can render as
"showing 20 of 25 — re-import to see the rest" or similar), not silently produce a file that looks
complete.

## Note: the customer-table export does NOT share this gap

`ExportCustomersAsync` reads from `GetCustomersAsync` with `pageSize=ExportMaxRows` (50,000),
sourced from `PostCampaignSegmentMember` — a table that stores every matched customer without any
20-item cap. Only the unknown/invalid-token side of the import is affected. Not stress-tested at
real scale in this session (only 14 real customers exist in the dev tenant), so this claim rests on
code reading, not a live 50+-row proof — noting explicitly rather than overclaiming.

## Suggested fix directions (not applied — flagging for a scoped follow-up)

1. Store the full unknown/invalid token lists somewhere durable (JSONB column, no hard cap, or a
   size-bounded cap high enough to cover the real 20,000-row `MaxAcceptedRows` ceiling) and export
   from that instead of the display-only 20-item sample. Needs a decision on worst-case row-size
   impact (a segment where ALL 20,000 uploaded tokens are unknown would need to store 20,000
   strings on the segment row).
2. Cheaper alternative: keep the 20-item in-app preview sample as-is (fine for on-screen display),
   but make the EXPORT re-derive the full lists from `UploadedCount - MatchedCount - DuplicateCount`
   token identity at export time — not possible today without re-parsing the original upload, which
   is deliberately never persisted (source doc's own "delete upload after processing" requirement,
   already satisfied and confirmed by TASK-474's review) — so this option would need the original
   token classification to be persisted in full at import time regardless, converging on option 1.
3. Minimal/short-term: at least make the export response honest — add a real
   `totalUnknownCount`/`totalInvalidCount` alongside the existing capped rows, and have the
   frontend show "перші 20 з N" / "перші 20 з N" wording on the button, matching the honesty
   standard this series already applies to the customer table's own "showing page X of Y."

Needs a product decision on the storage tradeoff before implementation, not a one-line patch.
