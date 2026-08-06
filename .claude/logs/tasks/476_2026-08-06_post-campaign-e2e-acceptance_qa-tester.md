# TASK-476: E2E acceptance — post-campaign audience analysis (Фаза 4)

**Agent:** qa-tester
**Date:** 2026-08-06
**Status:** done — **verdict: SHIP WITH FOLLOW-UP.** No crash, no security hole, no cross-tenant
leak. 2 real, confirmed functional bugs found in Фаза 4's own code (both silent-wrong-output, not
crashes) plus 1 real, confirmed pre-existing platform bug (unrelated to Фаза 4) discovered as a
byproduct of testing item 13. All three reported, none fixed, per this task's brief.

## Restart note

A prior attempt at this task stalled with no artifact (confirmed via git status at start: no task
log, no doc changes). The live dev DB showed it had already run a large, well-organized batch of
API-level import tests (segments named `QA476-*`, a `QA476 Marketing Viewer` TenantRole, a
`QA476 Never Purchased` zero-purchase customer — all dated today, 13:32-13:46 UTC) before stalling,
apparently on a browser interaction, per the brief's own note. This session started fresh per
instructions, but reused that prior session's setup where it was still valid (see below) instead of
re-doing it blind.

## Scope

Read TASK-471/472/473/474/477 logs, `docs/uployal/AUDIENCE_ANALYSIS.md` (full, esp. §36), and
TASK-433 (Фаза 3's own acceptance pass) as the procedural template. Then read the actual code
directly: `PostCampaignController.cs`, `PostCampaignService.cs` (full, 748 lines),
`SegmentImportParser.cs`, `PostCampaignDtos.cs`, `ImportLimits.cs`, `PostCampaignSortKeys.cs`,
`MarketingAnalyticsAuthorization.cs`, `PhoneNormalizer.cs`, `CustomerService.cs`,
`TenantConnectionInterceptor.cs`, `AuthService.cs` — not just the task-log prose.

## Environment

Dev stack: `dotnet run --project backend/ShelfGuard.Api` (port 5000) + Next.js `frontend-dev`
(auto-assigned port 50063 — port 3000 was already occupied by an unrelated `website-web-1` docker
container), live `crmproductsystems-postgres-1:5435`, real tenant "Свіжий Кут"
(`8abfbbb5-3190-4de9-9f91-f4de59101bca`, `marketing_analytics` module enabled) — 14 real customers,
127 `pos_transactions` spanning 2026-01-07 to 2026-07-26. Demo accounts, password `password`
(KI-005 fallback): `manager@demo.local` (store_manager, used for nearly all API testing — same
account the prior stalled session had already used), `merch1@demo.local` (merchandiser),
`keeper@demo.local` (storekeeper), `ea@demo.local` (enterprise_admin).

**Verification split**: most functional/security items verified via direct `curl` against the API
(fast, precise, scriptable ground-truth comparison) per the brief's own guidance to prefer this
where a UI click isn't strictly necessary. A focused, time-boxed browser pass (see below) covered
the items that genuinely need rendered UI: the draft/analyzed banner, the 5 KPI cards, the empty
segment state, the transition matrix, the customer table. No interaction hung — one real
environment issue was hit and fixed (see "Browser session" below), not a repeat of the prior
stall.

## Ground truth used for independent verification (not just re-reading the app's own output)

Queried the real DB directly (`docker exec ... psql`) for a hand-computable 7-day window
(after `2026-07-20..07-26`, before `2026-07-13..07-19`, formula-derived per source doc §10.1) across
all 14 real customers — per-customer before/after transaction counts and turnover — **before**
calling any API, so every number below was checked against independently-derived ground truth, not
just internal self-consistency.

## A. Functional acceptance

1. **Parser edge cases** — one self-designed raw-text submission covering every case in the brief
   at once: one-ID-per-line, comma-split, an exact-text duplicate, a duplicate that only collapses
   after phone normalization (`"380 50 111 00 08"` vs `"+380501110008"`), 4 real phone numbers in
   4 different formats (`+380...`, spaced, local `0`-prefixed, dashed), a well-formed-but-unknown
   GUID, a decimal (`12345.6789`), free text with embedded digits
   (`invoice-0501234567-note`), a negative number, a short leading-zero number, a huge digit run,
   and a blank line. Result: `uploaded=15, invalid=5, duplicate=2, unknown=5, matched=3` —
   `15 = 5+2+8` and `8 = 3+5` both hold exactly. **Found here**: the 4 phone tokens all came back
   `unknown` even though they belong to real customers — traced to a real bug, see Findings §1.
2. **CSV/XLSX import + column auto-detect/override** — hand-built a real binary `.xlsx` (Python
   `zipfile`, valid OOXML, no library) with a `customer_id` header: auto-detect correctly picked
   column 0, 2/3 rows matched, 1 unknown (a well-formed all-zero GUID). Ambiguous-header CSV
   (`foo,bar`, real GUIDs in column `bar`): default auto-detect fell back to column 0 → both
   invalid; resubmitting with `columnIndex=1` → both matched. Both paths confirmed working, live,
   not just via the prior session's own opaque test segments.
3. **Validation summary + unknown-tokens export** — confirmed the first-20-sample shape is what the
   API actually returns (`unknownTokensSample`/`invalidTokensSample`, capped at 20 each,
   `ImportResultDto`). Byte-level export check (same ZIP-parse technique TASK-433 used): a segment
   with real `UnknownCount=25, InvalidCount=24` exported only **20+20=40** rows, not 49. **Found
   here**: the export is silently capped at the same 20-sample shown in the UI — a real bug, see
   Findings §2.
4. **Draft-vs-analyzed banner** — live in the browser: importing a new 14-ID list while an older
   analyzed segment was still loaded immediately showed "Unapplied audience changes... the report
   below still reflects the previously analyzed segment" with an "Analyze now" button; clicking it
   made the banner disappear and the "Analyze segment" button become "Refresh". Exactly per source
   doc §7.
5. **Period picker formula** — 7-day case verified against hand-computed ground truth via API
   (`after 07-20..07-26` → `before 07-13..07-19`, exact match). 30-day case verified live in the UI:
   `after 07/08/2026–08/06/2026` → auto-computed, read-only "Before window (auto): 2026-06-08 –
   2026-07-07" — `DayNumber(08-06)-DayNumber(07-08)+1=30`, `beforeEnd=07-07`,
   `beforeStart=07-07-29=06-08`, both correct. Not editable in the UI (read-only text, no input).
6. **Top KPIs** — all 5 cards render, including **Not Returned as a real, visibly distinct 5th
   card** ("no purchases before or after"), both in a populated segment and in a genuinely empty
   one. Zero-denominator handling confirmed at both API and UI layers: `retentionRatePercent`/
   `churnRatePercent` are `null` (never `0`) when `activeBefore=0`; `reactivationRatePercent` is
   `null` when `inactiveBefore=0` **and** the recommendation text switches to the dedicated "all
   were already active before" copy (source doc §11.2) rather than a generic 0%. Live UI renders
   these as `n/a` / contextual text, never a bare "0%".
7. **Behavioral balance identity** — `reactivated+retained+dropped+notReturned == matchedCount`
   verified two ways against real data: API (7-day window, hand-derived: `5+3+1+5=14`, matched the
   API exactly) and live UI (30-day window: `8+1+2+3=14`, matched the on-screen cards exactly).
8. **RFM migration matrix** — `GET .../migration` for the 14-customer segment: row sums = before
   distribution, column sums = after distribution, total matrix sum = 14 = matchedCount, all
   confirmed by direct summation of the JSON. **"Без покупок" (no-purchase) null bucket**: exactly
   one customer (`QA476 Never Purchased`, genuinely zero all-time transactions) got `segmentBefore:
   null, segmentAfter: null`. **Case (b), real history but zero in-window**: 4 independent customers
   (`Lost One`/`AtRisk One`/`CannotLose One`/`Hibernating One`, each with real but old/sparse
   purchase history entirely outside both narrow windows) all got a real, non-null `Hibernating`
   label in both slots — never confused with the null bucket. A second flavor of case (b): 6
   customers whose real purchase history is recent enough to fall entirely inside the *after*
   window (zero in the *before* window specifically) also got a real non-null `New` label for their
   before-slot, not null — proving the distinguishing logic works on genuine near-miss cases, not
   just the obvious one. Also confirmed live in the UI: the full 12×12 transition matrix renders
   with dots for empty cells and green/red/neutral coloring; both donuts show a real "Без покупок"
   slice.
9. **Customer table** — server pagination proven live: `pageSize=5` over 14 real customers → 3
   pages (5/5/4), all IDs disjoint across pages, `totalPages=3`. Sorting tested for
   `checksbefore`/`transition` (ties broken by CustomerId, matching the code's `ThenBy`). PII
   masking: `manager@demo.local` (store_manager, `CanExportPii`-eligible by role) correctly sees
   real phone digits; server ignores client input for the GET path (verified by reading the
   controller — `canViewUnmaskedPii` is computed server-side from the JWT, never from a query
   param). **Could not** produce a live session that is authorized to *view* this report but sees a
   *masked* phone — every role in this tenant that can reach the page at all is also automatically
   `CanExportPii`-eligible by role. This is the exact same structural gap TASK-433 already
   documented for Фаза 3 (not new here) — relying on the same unit-level proof
   (`PiiMasking.MaskPhone` + the server-side-only capability check, both read directly in code) as
   the correct-weight substitute, per that same precedent.
10. **Empty/small segment** — a genuinely empty segment (0 matched, from the prior session's own
    `QA476-empty-segment`) was exercised on **every** endpoint (summary/migration/customers/
    rfm-activity/daily-turnover) via API (all 200, all null-safe/zero-safe JSON, a dedicated
    "check your ID list, some customers may be unknown to the system" recommendation instead of a
    generic zero-result message) **and** live in the browser (switched to it via the segment
    picker): all 5 KPI cards render cleanly at 0/n/a, tabs don't crash, no console error.
11. **AI explain** — no Claude API key is configured in this dev environment (checked
    `appsettings.{,Development.}json`, no backend `.env`, no per-tenant `integration_configs` row
    for `claude`/`anthropic`). `POST .../explain` returned a clean `503` with
    `"Claude API key is not configured. Add it in Налаштування → Інтеграції → Claude AI."` — the
    documented not-configured UX, not a crash. **Did not** test the configured/real-response branch
    — no key available in this environment, stated explicitly rather than assumed.

## B. Security-fix acceptance (TASK-474/477)

12. **Import row cap** — 20,001 raw-text rows (just over `MaxAcceptedRows=20,000`) rejected with a
    clean `400` ("Максимум 20 000 ID... Отримано 20 001") in **0.27 seconds** — fast-fail confirmed,
    not a hang. Did not additionally reproduce the secondary 25,000-row `ImportLimits` infra
    ceiling live (a retry of that specific request got blocked by this environment's own
    auto-mode safety classifier, treated as a signal to stop rather than force through) — relying
    instead on `ExcelImportServiceTests.cs`'s own passing, permanent tests for that boundary,
    which is the brief's own explicitly-stated acceptable fallback for exactly this class of case.
13. **Import permission (finding B)** — role-floor boundaries confirmed live and unambiguously:
    `keeper@demo.local` (storekeeper, below both floors) got `403` on **both**
    `GET .../segments` and `POST .../import`; `manager@demo.local` (store_manager, at the floor)
    got `200`/success on both, throughout this whole session. **Could not** construct the exact
    "view via capability, not role" scenario the item asks for — traced this precisely to a real,
    separate, pre-existing platform bug (not Фаза 4's own code): `TenantRole` capabilities never
    reach the JWT on login/refresh at all, for any user, tenant-wide (see Findings §3). Verified
    `CanImportSegments`'s own logic is correct via direct code reading (role-only, no capability
    branch, matching its doc comment) and via the full test suite — `dotnet test` 96/96 green on a
    filter covering `MarketingAnalyticsAuthorizationTests` specifically, including TASK-477's own
    8 new tests that pin "false even with the `marketing_analytics.view`/`export_pii` capability
    claim." Stating this gap explicitly per the brief's own instruction, not skipping silently.
14. **Malformed/oversized file (findings A+C)** — 3 real malformed `.xlsx` uploads, all hand-built
    (Python `zipfile`), all produced a clean `400` with the documented message, never a `500`:
    plain text renamed to `.xlsx` (not a zip at all), a well-formed ZIP that isn't a valid OOXML
    package, and a genuinely empty (0-byte) file (routes to a different, also-clean 400 branch —
    "Provide exactly one of a file or rawText"). Did not attempt a live giant zip-bomb repro of
    finding A's specific adversarial case — TASK-477's own addendum already did this rigorously
    (real measured timings/allocations up to 1M+ rows) and has permanent passing tests; a repeat
    wasn't needed for sign-off per the brief's own explicit allowance.

## Findings (3 real bugs — reported, not fixed)

Full writeups: `.claude/logs/reviews/bug-task476-phone-import-matching-format-mismatch_2026-08-06.md`,
`.claude/logs/reviews/bug-task476-unknown-tokens-export-capped-at-20_2026-08-06.md`,
`.claude/logs/reviews/bug-task476-tenantrole-capabilities-never-reach-jwt_2026-08-06.md`.

1. **HIGH, Фаза 4's own code** — phone-based import matching
   (`PostCampaignRepository.FindCustomersByIdsOrPhonesAsync`) only works when the customer's stored
   `Phone` is already in the exact canonical `+380XXXXXXXXX` form; `CustomerService`'s own write
   path never normalizes it (intentionally permissive regex, `.Trim()` only). Live-confirmed: 4 of
   4 real customers' phones, correctly formatted, all came back "unknown." This is a silent,
   confidently-wrong result in the exact "existence validation" promise this whole feature exists
   to deliver — no crash, no error, just quietly wrong KPIs. GUID-based matching unaffected.
2. **MEDIUM-HIGH, Фаза 4's own code** — the unknown/invalid-tokens error-report export
   (`ExportUnknownTokensAsync`) is silently capped at 20+20=40 rows because that's all
   `PostCampaignSegment` ever stores (the sample fields, not a full list) — with no truncation
   indicator anywhere in the response. Live-confirmed by byte-parsing a real export: 40 rows
   returned for a segment with 49 real unknown+invalid tokens.
3. **HIGH, pre-existing platform bug, NOT Фаза 4's own code** — found as a direct byproduct of
   testing item 13. `TenantRole.Capabilities` (ADR-020/TASK-345/346) never reach the JWT's
   `capabilities` claim on login or refresh, for any user, ever — root cause is
   `TenantConnectionInterceptor` correctly RESETting `app.tenant_id` for the unauthenticated login
   request (needed for the `users` table's own RLS carve-out), but `tenant_roles`' own RLS policy
   has no equivalent carve-out, so it's invisible to that same connection mid-login. Live-confirmed
   for 2 real users with real non-empty `TenantRole` capability grants — both get `"capabilities":
   []` in the login response body itself, not just a missing JWT claim. This silently disables the
   capability-widening half of every `RoleOrCapability`-gated controller tenant-wide (7+ policies
   per `AppPolicies.cs`'s own summary), not just this one endpoint. Flagging for a dedicated
   backend-developer + security-reviewer follow-up given the auth-boundary sensitivity — well
   outside this task's own scope to fix.

All three share the same root cause shape: the codebase's unit tests mock the repository/data
layer at exactly the point where a real RLS/data-format interaction actually matters, so a
scenario that's fine against idealized mocked data silently breaks against realistic stored data —
noting this as a process observation, not just three unrelated bugs.

## Browser session note (relevant given the prior attempt's stall)

Hit and fixed one real environment issue, not a repeat of the prior stall: the auto-assigned
frontend dev port (50063, since 3000 was taken by an unrelated docker container) wasn't in the
backend's `Cors:Origins` allowlist, so every API call from the browser failed with a CORS error and
the app hung on "Loading...". Diagnosed via `read_console_messages` (not a blind wait), fixed by
restarting the backend with `Cors__Origins` including the actual port, then all interaction
completed normally with no hangs. Separately, an initial navigation to `/en/marketing-analytics/
post-campaign` rendered the generic "Сторінка в розробці" placeholder — traced this to a wrong URL
on my own part (this app's dashboard routes have no locale-prefix segment; TASK-473's own log
mentioning `/en/...` was apparently imprecise/environment-specific) — the correct
`/marketing-analytics/post-campaign` (no prefix) renders the real, fully-built page correctly. Not
a product bug; noting only so a future session doesn't waste time on the same false start.

## Build/test

- `dotnet build` (full backend) — 0 warnings, 0 errors.
- `dotnet test` (full suite) — **1310/1310 green**, matches TASK-477's own final addendum count
  exactly, zero regressions from this session's live testing.
- `dotnet test --filter "PostCampaign|ExcelImportServiceTests|MarketingAnalyticsAuthorizationTests"`
  — **96/96 green**, isolated confirmation.
- `npx tsc --noEmit` — 0 errors. `npm run lint` — 0 warnings.
- `npm run build` — exit 0, `/marketing-analytics/post-campaign` 15.4 kB / 257 kB First Load JS,
  byte-identical to TASK-473's own reported figure (zero bundle drift). Only non-zero "Error" text
  in the build log is the pre-existing, already-flagged-harmless `ENVIRONMENT_FALLBACK` noise.

## Cleanup

Stopped both dev servers cleanly at the end (backend PID killed, port 5000 confirmed free;
frontend preview server stopped via `preview_stop`). Did **not** delete the `QA476-*` test segments
this session (and the prior stalled one) created in the dev DB, or the `QA476 Marketing Viewer`
TenantRole / its assignment to `merch1@demo.local` — same precedent TASK-433/424 already set for
this series (QA-generated residue in a shared dev DB is left in place, not treated as something to
scrub), and the TenantRole assignment is inert anyway given Finding 3. No real customer/transaction/
tenant data was created, modified, or deleted — every mutation this session made was either a new
`post_campaign_segments` test row or a read.

## Overall verdict

**SHIP WITH FOLLOW-UP.** The core of Фаза 4 is solid: every documented identity (behavioral
balance, RFM migration marginal sums, money/daily-turnover sums, zero-denominator null-not-zero
handling), the full report UI (5 KPI cards, banner, all 3 tabs, transition matrix, customer table
pagination/sort/masking), the period-picker formula (both 7-day and 30-day), the empty-segment
path, and all 3 of TASK-477's security fixes (row cap, malformed-file handling, the
`CanImportSegments` role floor) held up under live, independently-verified re-testing — much of it
against hand-computed ground truth, not just the app's own self-report. `dotnet test`/`tsc`/
`npm run build` are all clean. But 2 real bugs in Фаза 4's own code should be fixed before this
ships to a tenant who will actually paste phone numbers or hit a large unknown/invalid count:
phone-based import matching silently fails for any customer whose phone isn't already stored in
canonical form (likely common in real data), and the error-report export silently truncates past
20+20 with no indication. Neither is a crash or a security hole, both are silent-wrong-output in
this feature's own headline "existence validation" promise — worth a scoped backend-developer
follow-up before general rollout, mirroring this series' own TASK-412→414 and TASK-474→477
review-then-fix pattern. The third finding (TenantRole capabilities never reaching the JWT) is a
separate, pre-existing, higher-severity platform issue unrelated to Фаза 4 — flagging for its own
dedicated follow-up, not a blocker for this feature specifically.
