# TASK-433: E2E acceptance/regression — Фаза 3 (AudienceBuilder)

**Agent:** qa-tester
**Date:** 2026-07-27
**Status:** done — **verdict: SHIP.** No blocker, no crash, no data-integrity failure, no PII leak
anywhere in the naskrizny scenario. 2 non-blocking observations noted below (neither is a bug in
this feature).

## Scope

Read TASK-428 (database-engineer)/429 (backend-developer)/430 (frontend-developer)/431
(security-reviewer) logs, then the actual code directly (controller/service/repository/DTOs/all
frontend components). Also found and read TASK-432 (documentation-writer, docs-only, appeared
mid-session) — no code changes there, doesn't affect this pass. Tested against the real dev stack:
`dotnet run` backend on :5000, `npm run dev` frontend on :3000, live Postgres
`crmproductsystems-postgres-1:5435`, real seeded tenant "Свіжий Кут" (13 customers, 127 POS
transactions, 15 items — confirmed unchanged before/after, see Cleanup). Session was already
logged in as `ea@demo.local` (enterprise_admin) via a leftover browser token from a prior task's
session.

**Environment note:** the Browser pane did not composite frames in this session
(`screenshot`/coordinate-click both failed with "pane is not displayed"). Drove all UI interaction
via direct DOM/event dispatch instead — setting React-controlled input values through the native
`value` setter (via `Object.getPrototypeOf(el)`, not `window.HTMLInputElement.prototype`, which
throws "Illegal invocation" across the CDP realm boundary) then dispatching real `input`/`keydown`
events, and `.click()` for buttons/checkboxes/tabs. Confirmed this exercises the exact same React
state → React Query → network path as a real click (cross-checked against `read_network_requests`
throughout, and the on-screen text always matched the wire response) — not a shortcut around the
app's own logic.

## Scenario coverage (all 13 steps)

1. **Page opens** cleanly at `/marketing-analytics/audience-builder` — empty state, "Сформувати
   список" correctly disabled with 0 terms.
2. **Single text term "Молоко"** → Enter → chip appeared, placeholder flipped to "ще товар…",
   build button became enabled.
3. **Second term "Кефір"** → `CombineModeToggle` appeared (absent with 1 term, per code). **OR**
   ("Будь-який товар", default) → 7 participants / 23 units / 710 ₴. **AND** ("Усі товари") → 0
   participants — genuinely different (no customer in this real dataset bought both products in
   the period), 0-result state renders cleanly, no crash. This incidentally also covers step 12's
   "empty result doesn't break the page" once already.
4. **minQuantity=3 + minAmount=100** (OR mode) → narrowed 7 → 3 (Champion One/Two/Three, 17
   units, 563 ₴). Hand-verified this is a genuine **AND** of both thresholds, not either alone:
   Loyal One (qty=3, amount=75₴) satisfies the quantity threshold but is correctly excluded by the
   amount threshold — status line showed "2 умов · будь-який · від 3 шт · від 100 ₴" throughout.
5. **Matched items tab**: unchecked "Молоко" (the only item with real sales in this window; 23
   sold/23 receipts/7 buyers — "Кефір" legitimately shows 0/0/0, zero-sales-still-listed
   requirement, live) → status line instantly "вилучено 1 товар(ів)", "Обрано 1 з 2". Switched to
   "Покупці товару" tab → KPI cards (0 participants/1 item in selection/0 units/0 ₴) **and** the
   buyers table (0 rows, clean "Немає покупців…" empty state) were **already both correct
   together** the moment the tab rendered — no partial/stale mismatch observed (this is the
   mandatory core requirement).
6. **"Обрати всі"** → full restore, "Обрано 2 з 2", status line back to "2 умов · будь-який" (no
   "вилучено" suffix).
7. **Competitor tab**: added "Вода" as competitor term. **InPeriod** and **AllTime** both showed 1
   new-audience customer ("TASK-410 Live Check Customer" — a real, pre-existing fixture row in
   this tenant, not created by me) — same *count* coincidentally, but confirmed the wire payload is
   genuinely different: `filtersHash` was `367d5a2aad2575c3` (InPeriod) vs `693a7c678ff78293`
   (AllTime) — independently reproducing the exact same two hash values TASK-430's own session
   already found, confirming this is the same known real-data coincidence, not newly broken.
8. **Export main audience** — **verified at the byte level**, not just clicked-and-trusted: fetched
   the export directly, manually parsed the returned XLSX (ZIP central directory → local file
   header → raw-deflate inflate via `DecompressionStream('deflate-raw')`, all in-page JS, no file
   ever touched disk) and counted `<x:row>` elements in `xl/worksheets/sheet1.xml`. Result: **24
   rows = 1 header + 23 data rows**, for the same 7 customers whose receipt counts sum to exactly
   23 (7+5+5+3+1+1+1) — definitive proof this is **receipt-level**, not the 7-row buyer-level
   count. Column shape confirmed via `sharedStrings.xml`: Ім'я/Телефон/№ чека/Дата/Заклад/Куплено
   шт/Сума ₴ (7 columns, matches `BuildBuyersExcel`). Cross-confirmed by re-running the dedicated
   repository test `GetBuyerReceiptsAsync_returns_one_row_per_receipt_scoped_to_matched_skus_only`
   (controlled fixture: a customer with 2 receipts on file correctly produces 2 export rows).
9. **Export competitor audience** — same byte-level technique: **2 rows = 1 header + 1 data row**,
   **5 columns** (Ім'я/Телефон/Куплено шт/Чеків/Сума ₴ — no receipt number/date/store columns,
   structurally different shape from the 7-column receipt export) confirming customer-level grain.
   Only 1 real competitor buyer exists in this tenant's data, so a live multi-receipt-collapsing-
   to-1-row demonstration wasn't possible here — the column-shape proof plus the repository code
   (`CompetitorBuyerRowRaw` carries `ReceiptCount` as an aggregated `COUNT(DISTINCT txn_id)`, never
   a per-receipt row) closes this gap architecturally.
10. **PII masking, checked explicitly for the TASK-424-class bug** (Фаза 2's tables never masked
    on-screen phone at all, regardless of role — the "reveal" checkbox only tagged the export
    flag). For AudienceBuilder: full phone numbers ARE shown on-screen in my own session, but this
    is **correct-by-design**, not the same bug — `enterprise_admin` is in
    `AppPolicies.AtLeastStoreManagerRoles`, which is byte-for-byte the same role array
    `MarketingAnalyticsAuthorization.CanExportPii` checks first, so my role legitimately qualifies
    for unmasked view server-side. Verified the actual masking mechanism directly, three ways: (a)
    **live byte-level check** — called `/exports/buyers` with `unmaskPii:false` and inspected
    `sharedStrings.xml`: produced real masked strings (`+380 50 *** ** 01`, `+380 50 *** ** 12`,
    etc.), proving `PiiMasking.MaskPhone` genuinely masks in the running server, not just in a
    mock; (b) re-ran the existing service unit tests
    `GetBuyersAsync_masks_phone_by_default_and_unmasks_when_authorized` and
    `GetCompetitorBuyersAsync_masks_phone_by_default` — both green, both assert
    `canViewUnmaskedPii:false` (the DTO's own default) masks and `true` unmasks; (c) confirmed by
    direct code reading that `AudienceBuilderController` **unconditionally overwrites**
    `CanViewUnmaskedPii`/`UnmaskPii` from `MarketingAnalyticsAuthorization.CanExportPii(User)` on
    every read/export action — a client-sent `true` is never trusted, matching TASK-425's fix
    pattern applied here from day 0 (per the DTO's own doc comment), not patched in after a finding
    like Фаза 2 needed. Also confirmed `AudienceOverviewDto`/`MatchedItemRowDto` carry **no phone
    field at all** — no third silent-exposure surface, unlike Фаза 2's 3-tables-all-forgot case.
    **Residual, not fully closeable in this environment** (same structural note TASK-424 already
    made for Фаза 2, not new to this feature): every demo role that can even view this page
    (`Provider`/`EnterpriseAdmin`/`NetworkManager`/`StoreManager`) is *also* automatically in
    `AtLeastStoreManagerRoles`, so no live browser session in this tenant can show a genuinely
    masked on-screen phone — that only becomes reachable if a tenant ever grants
    `marketing_analytics.view` to a sub-store_manager role via capability without also granting
    `marketing_analytics.export_pii`. Did not fabricate that capability grant live (would mutate
    shared tenant config beyond this task's read-mostly scope) — the unit-level proof above is the
    correct-weight verification for that specific branch, same standard TASK-424 itself applied.
11. **Real server-side pagination**, all three tables, proven via direct API calls with a small
    `pageSize` against real broader data (a generic Cyrillic-vowel term "а" to get more than one
    page's worth):
    - **Matched items**: 11 total items, `pageSize=3` → 4 pages; page 1/page 2 fully disjoint;
      last page (4) correctly had exactly 2 rows (11 − 3×3); **descending name sort returned a
      genuinely different, correctly-reversed-and-resorted set** (not a reversed slice of page 1).
    - **Buyers**: 8 total buyers, `pageSize=3` → page 1/page 2 disjoint and alphabetically
      continuous; descending sort likewise genuinely re-sorted from the full set.
    - **Competitor buyers**: only 1 real row exists tenant-wide even with the broad "а" term, so a
      live multi-page demonstration wasn't possible. Read `GetCompetitorBuyersAsync`'s SQL directly
      instead: it uses the **identical** `NormalizeLimitOffset`/`COUNT(*) OVER()`/`ORDER BY
      {SORT_COLUMN} {SORT_DIRECTION}, cust_id ASC`/`LIMIT/OFFSET` shape as `GetBuyersAsync`, just
      proven live above — same mechanism, textually near-identical query tail. No dedicated
      pagination integration test exists for this one method either (see Findings).
12. **Empty result** tested three separate ways, all clean, zero console errors: a term matching
    zero items at all ("ZZZNOTHINGMATCHES123") → all KPIs zeroed, "Покупці товару" and "Знайдені
    товари" tabs both show correct empty states ("Немає покупців…" / "Немає товарів за поточними
    умовами пошуку."); a competitor term matching nothing → competitor tab shows its own correct
    empty state. Page never crashed, no error boundary triggered, no console error at any point in
    the whole session (`read_console_messages` checked repeatedly).
13. **Build/test** — see below.

## Findings (non-blocking)

- **Coverage gap, low risk**: unlike `GetBuyersAsync` (`GetBuyersAsync_pagination_and_sort_by_name_are_stable`)
  and the matched-items path, there is no dedicated integration test asserting
  `GetCompetitorBuyersAsync`'s pagination specifically. The SQL is textually near-identical to the
  already-tested buyers query (confirmed by direct comparison), and this tenant's real data
  couldn't produce a 2nd page live either — flagging for awareness, not spawning a follow-up (too
  narrow on its own to justify one, and the shared mechanism is already proven elsewhere).
- **Pre-existing fixture, not mine**: "TASK-410 Live Check Customer" (`+380991110410`) is a real
  leftover customer row from a much older task's own live verification, still present in this
  tenant — it's what the competitor-tab "Вода" scenario above actually exercised. Awareness only,
  not a regression, not created or touched by this session.
- **Tangential observation, not Фаза-3-specific**: partway through this long interactive session
  the 15-minute dev JWT access token expired, and both my direct API calls and the real UI's own
  export button then got `401`. A full page reload silently obtained a fresh token and everything
  continued working normally. This is shared, pre-existing auth infrastructure (not anything this
  feature introduced) — noting only in case it's useful context for a future session that runs long
  against this dev stack.

## Cleanup

- Confirmed tenant "Свіжий Кут" business-data row counts are **unchanged**: 13 customers, 127
  `pos_transactions`, 15 items — byte-for-byte the same as this session's own opening state and
  matches TASK-424's own previously-reported baseline for the same tenant. No customers,
  transactions, settings, or tenants were created, modified, or deleted — every check in this
  session was a read-only GET/POST against real pre-existing data.
- This session's exports produced **8 `activity_logs` rows**
  (`marketing_analytics.audience_builder.export_buyers` / `...export_competitor_buyers`) — real
  audit-trail rows of the export calls actually made, left in place. Same precedent TASK-424 set
  for Фаза 2 (export-generated audit-log rows from QA testing are not treated as "test data" to
  delete).
- Reset the on-screen builder state via "Скинути" before finishing (cosmetic only — the Zustand
  store isn't persisted, so a reload would have cleared it anyway).
- Stopped both dev-preview servers (backend :5000, frontend :3000) cleanly at the end.

## Build/test — full run after all Фаза 3 agents (428–432)

- `dotnet test` (full suite): **1213/1213 green**, 0 failed, 0 skipped — matches TASK-429's own
  reported count exactly, zero regressions from this session's live-data testing either.
- Filtered re-run, `--filter "FullyQualifiedName~AudienceBuilder"`: **27/27 green** (13 service unit
  + 14 repository integration) — explicit isolated confirmation, including both PII-masking tests
  and the receipt-level export test cited above.
- `npx tsc --noEmit`: 0 errors.
- `npm run build`: exit 0, all 55 routes generated, `/marketing-analytics/audience-builder` —
  **11.2 kB / 135 kB First Load JS**, byte-identical to TASK-430's own reported figure (zero
  bundle-size drift). Same pre-existing `ENVIRONMENT_FALLBACK` stderr noise every prior agent
  already flagged as harmless (appears across dozens of unrelated routes during static generation,
  exit code 0, no `Failed to compile`/`Type error`/`Module not found` anywhere in the output).
- Zero console errors or warnings at any point in the live browser session.

## Overall verdict

**Ship.** Every mandatory item in the наскрізний scenario held up under live, sometimes
byte-level, re-verification: OR/AND semantics genuinely differ; both thresholds apply as a real
AND; matched-items exclusion instantly and correctly updates the KPI cards and buyers table
together on the other tab; competitor horizon toggle sends a genuinely different request even when
this tenant's data coincidentally returns the same count; the main export is real receipt-level
(24 rows for a 7-buyer/23-receipt audience, confirmed by parsing the actual XLSX bytes) and the
competitor export is real customer-level (5 columns, no receipt granularity); PII is masked by
default in the running server (confirmed live, not just via test) and never trusts client input on
any read/export path, with no residual phone-field exposure on Overview/MatchedItems; all three
tables paginate and sort genuinely server-side (two fully live-proven, the third structurally
identical in code); empty-match results never break the page in any of the three tabs; and
`dotnet test`/`tsc`/`npm run build` are all clean with zero accumulated regressions across the
whole Фаза 3 agent chain. The two items under Findings are awareness-only, neither blocks release.
