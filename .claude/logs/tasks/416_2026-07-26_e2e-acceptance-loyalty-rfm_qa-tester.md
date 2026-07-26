# TASK-416: E2E acceptance/regression — Loyalty (Фаза 0) + Marketing Analytics/RFM (Фаза 1)

**Agent:** qa-tester
**Date:** 2026-07-26
**Status:** done — **verdict: NOT clear to ship. 1 CRITICAL blocker found** (core consumer-join flow
is 100% broken). Everything downstream of a manual workaround for that one blocker passed cleanly,
including TASK-414's three security fixes, all live-verified against real adversarial input.

## Scope

Independent E2E pass over TASK-404..414 (loyalty program + RFM marketing analytics), per
`C:\Users\stass\.claude\plans\deep-cooking-nygaard.md`. Read all 11 task logs first, then read the
actual controllers/services/DTOs/migrations directly (not just the logs) before testing, per brief.
Tested against the real dev stack (`dotnet run` backend on :5000, `npm run dev` frontend, live
Postgres `crmproductsystems-postgres-1:5435`) — real HTTP calls (curl) for the API-heavy parts of
the flow, real browser interaction (Next.js dev server) for the POS drawer and RFM dashboard UI.

## CRITICAL BUG — `POST /api/consumer/loyalty/{tenantId}/join` fails 100% of the time for every consumer

**Severity: critical.** This is step 2 of the plan's own primary use case — a brand-new consumer
joining a tenant's loyalty program — and it cannot succeed even once, for anyone, as currently
shipped. It is not an edge case; every one of my 3 independent attempts (3 different consumers,
2 different backend process instances, one with completely fresh/never-touched data) failed
identically.

**Repro (minimal, reproduced 3× independently):**
1. `POST /api/consumer-auth/register` with any new phone/password/fullName → 200, get consumer JWT
   (role=`consumer`, claim `consumer_account_id`, **no** `tenant_id` claim — by design, confirmed
   by decoding the JWT).
2. Ensure the target tenant has the `"loyalty"` module enabled.
3. `POST /api/consumer/loyalty/{tenantId}/join` with that JWT →
   **`500`**, body:
   ```
   Microsoft.EntityFrameworkCore.DbUpdateException: An error occurred while saving the entity changes.
    ---> Npgsql.PostgresException (0x80004005): 42501: new row violates row-level security policy for table "customers"
   ```

**Root cause (traced through actual code, not guessed):**
- `TenantConnectionInterceptor.BuildSetSql` (`backend/ShelfGuard.Infrastructure/Interceptors/TenantConnectionInterceptor.cs:90-131`)
  sets Postgres session var `app.tenant_id` **once per HTTP request**, from the JWT's `tenant_id`
  claim — falling back to the null-UUID when absent. A consumer JWT **never** carries `tenant_id`
  (intentional — plan: "без tenant_id", consumer sessions are cross-tenant), so `app.tenant_id` is
  the null-UUID for the *entire* request.
- `LoyaltyService.JoinAsync` (`backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs:64-101`)
  calls `FindOrCreateCustomerAsync` → `ICustomerRepository.FindByPhoneAsync`/`CreateAsync`
  (`backend/ShelfGuard.Infrastructure/Data/Repositories/CustomerRepository.cs`), which run plain EF
  queries against `customers` with **no** manual `SET app.tenant_id` override for the target
  tenant (the tenantId is only a C# parameter, never pushed into the Postgres session).
- `customers`' RLS policy set is the canonical triad only — `tenant_isolation` (needs
  `app.tenant_id` to match), `provider_bypass`, `worker_bypass` — confirmed live via `\d+ customers`.
  **No identity-based policy exists for `customers`** (unlike `loyalty_memberships`/
  `loyalty_ledger_entries`, which got the new `consumer_self_access` policy in TASK-404 — that
  policy was deliberately scoped to only those two tables per the plan, and correctly so; it just
  means `customers` was never given an equivalent, and nothing else fills the gap).
- Net effect: under a consumer session, `FindByPhoneAsync` silently returns 0 rows (RLS hides
  everything, `app.tenant_id` matches nothing) — so it *always* takes the "not found → create"
  branch — and then `CreateAsync`'s INSERT is rejected by `tenant_isolation`'s WITH-CHECK, because
  the new row's real `TenantId` can never equal the null-UUID session variable. This is fail-closed
  RLS working exactly as designed against an access pattern the code never actually supports for a
  consumer session — the join flow's `Customer`-side eviction was never RLS-provisioned for the
  identity type that has to perform it.

**Why no prior agent's tests caught this:** TASK-405's own tests are NSubstitute-mocked repos (no
real RLS). TASK-404's live-Postgres RLS tests exercise `loyalty_memberships`/`loyalty_ledger_entries`
in isolation, not the join flow's dependency on `customers`. TASK-407 (mobile) explicitly did
contract-only verification, no emulator. TASK-409 (frontend RFM) seeded its 12 synthetic customers
directly via SQL, bypassing the join endpoint entirely (its own log says so). TASK-410/411/412/413/414
all either tested other endpoints live or read code without hitting this exact endpoint with a real
consumer JWT. This is precisely the "stitch point between parts built by different agents separately"
my brief asked me to look for.

**Impact:** blocks the entire Фаза 0 core journey end-to-end (register → join → QR → POS scan →
sale). Everything I verified past this point (steps 3-9 below) required a manual Postgres fixture
standing in for what `JoinAsync` should have produced, to unblock testing of the rest of the chain.

**Not fixed** (out of scope per brief — testing only). Flagging for the user to route to
backend-developer or database-engineer: needs either (a) a `customers`-scoped identity-based RLS
policy mirroring `consumer_self_access` restricted to `FOR INSERT` (matching the security review's
own recommendation in TASK-412 finding #2 to narrow that policy's `FOR` clause anyway), or (b) the
join flow explicitly elevates/sets `app.tenant_id` for its own `Customer` lookup/creation before
touching that table. `JoinAsStaffAsync` (Кейс 2, staff joining their own employer's program) was
**not** independently live-tested here (would have required editing a seeded user's phone number,
which the permission system correctly declined as touching shared seed data rather than my own test
fixtures) — but it runs under a normal staff session (real `tenant_id` claim already set), so it is
very unlikely to hit the same RLS gap; flagging as unverified rather than assumed-safe.

## What I verified past the blocker (via a manual DB fixture bridging step 2)

All of the following passed with real, live-traced evidence — not incidental UI glances.

**Steps 3-6 (QR code, staff resolve, POS sale, ledger):**
- `GET /consumer/loyalty/{tenantId}/code` → live rotating `SGLOY1.{id}.{code}` + balance. PASS.
- `POST /loyalty/resolve-code` as store_manager → correct membership/customer/masked-phone/balance.
  Replaying the **same** code a second time → `409` "already used" (anti-replay confirmed). PASS.
- `POST /pos/sales` with `customerId`+`loyaltyMembershipId`: accrual-only sale (140.00 → 3%
  = 4.20 accrual) and a second sale with `redeemAmount=4.00` (redemption computed first, accrual
  computed on the **net** post-redemption amount: 10.00 net × 3% = 0.30, final balance exactly
  0.50) — arithmetic verified byte-for-byte against `GET /consumer/loyalty/{tenantId}/history`'s
  ledger (2 separate ledger rows per mixed sale, correct `BalanceAfter` chaining). PASS.
- **Balance/cap guards** (explicit ask in the brief): redeeming more than the redemption cap
  (50% of sale) → `400 "Redeem amount exceeds the redemption cap..."`; redeeming within cap but
  more than the actual balance → `400 "Insufficient loyalty balance."` — two distinct, correct
  error paths, balance never goes negative, no transaction created on either rejection. PASS.

**Step 7 (web sales view shows customer + bonuses — the TASK-408→410 stitch point):**
- `GET /api/pos/sales?shiftId=...` returns real `customerId`/`customerName`/`loyaltyAccrued`/
  `loyaltyRedeemed`/`loyaltyBalance` for both loyalty sales, `null`s for a plain anonymous sale in
  the same shift — verified via raw-byte UTF-8 decode (not just console print) to rule out a
  false-positive from my own tooling.
- **Live browser confirmation**, not just API: logged in as `manager@demo.local` through the real
  login form, opened `/pos`, confirmed the Gift-icon loyalty indicator on both rows, clicked a row
  and confirmed `SaleDetailDrawer`'s "Лояльність" section renders "НАРАХОВАНО БОНУСІВ +0.30 ₴ /
  СПИСАНО БОНУСІВ -4.00 ₴ / БАЛАНС ПІСЛЯ ЧЕКА 0.50 ₴" exactly matching the API. This is the first
  live confirmation that TASK-408's frontend (built when the backend contract returned only nulls)
  and TASK-410's later backend fix actually integrate correctly — nobody had checked this live
  before. PASS.

**Step 8 (RFM dashboard, new customer lands in the right segment):**
- Overview loaded live, 11 segments + "no purchase" card. My fresh test customer's single purchase
  ("Вода Моршинська 1,5л") showed up ranked #1 by coverage inside the "Нові" (New) segment's
  top-products panel — direct confirmation the newly-loyalty-joined customer was correctly
  classified. PASS.
- Segment share% sums to exactly 100.0 and revenue share% to 99.99 (rounding) across all 11
  segments for the live population — checklist item "sum of segment shares = 100%" confirmed with
  real numbers, not assumed.
- Affinity vs. "Разом у чеку" (basket) tabs for the same anchor product returned genuinely
  different numbers (lift ×1.5/×1/×1 vs. 40%/30%/10% co-occurrence) — checklist item confirmed.
- Empty segment (0 customers, e.g. "Lost" at the time of testing): API returns clean zeroed DTO,
  UI renders "У цьому сегменті немає клієнтів..." with export controls correctly hidden, zero
  console errors. PASS.
- Noteworthy (not a bug): adding 2 new customers to the population shifted the NTILE-quintile
  boundaries enough that a pre-existing seeded customer moved into "Loyal" (0→2) between two of my
  checks. This is the plan's own explicitly-specified behavior ("quintiles recomputed fresh over
  the current population, never cached") working as designed — flagging only so it isn't mistaken
  for a bug if someone else notices segment counts shifting during/after this session.

**Step 9 (Excel export — the highest-severity item in the brief):**
- File opens as a well-formed OOXML zip (verified via direct `zipfile`/XML parsing, not just "did
  Excel open it").
- **Formula-injection defense (TASK-414 fix), tested with real dangerous input, not assumed:**
  registered a genuine consumer via the real, unmodified `POST /api/consumer-auth/register` with
  `fullName = "=cmd|' /c calc'!A1"` (accepted verbatim — confirms TASK-412's finding that
  registration validates only non-empty), gave them a real purchase, exported their segment.
  Inspected the raw XLSX XML directly: the cell has **no** `<f>` (formula) element anywhere, stores
  the string as plain shared-string text, and — confirmed in `styles.xml` — its cell style has
  `quotePrefix="1"` explicitly set (the same style index Excel already uses for every phone-number
  cell, since phone numbers legitimately start with `+`, one of the sanitized characters). This is
  the correct, spec-native OOXML mitigation; a real Excel/LibreOffice/Sheets client will render this
  cell as literal text and never evaluate it as a formula. PASS — the critical fix holds under
  direct adversarial testing, not just code review.
- **PII masking:** default export (`unmaskPii:false`) masked both phone (`+380 XX *** ** NN`) and
  **email** (`q***@example.test`) — confirms TASK-414's email-masking fix (TASK-412 had found email
  was never masked). Re-exporting with `unmaskPii:true` as `store_manager` (a qualifying role)
  correctly returned real, unmasked phone/email. A `storekeeper` (below store_manager, no granted
  capability) got a clean `403` on the marketing-analytics endpoints entirely, confirming
  TASK-414's policy widening did not accidentally over-loosen the class-level gate. All 3 PASS.

**Additional regression checklist:**
- Anonymous sale (no `customerId`/`loyaltyMembershipId`/`redeemAmount` at all) behaves byte-for-byte
  as before this whole feature existed — full subtotal charged, all loyalty fields `null` — and
  coexists correctly in the same shift/list alongside loyalty-linked sales. PASS.
- `dotnet test` (full suite, fresh run after all 11 agents' changes): **1105/1105 passed**, 0
  failed, 0 skipped — matches TASK-414's own reported count exactly, confirms no regressions
  accumulated. My own E2E session's extra dev-DB data did not destabilize the suite.
- `npx tsc --noEmit` (frontend): clean, 0 errors, 0 output.
- `npm run build` (frontend): exit 0, full route table generated including `/marketing-analytics`
  (13.1 kB, matches TASK-409's reported size). The repeating `ENVIRONMENT_FALLBACK` stack traces
  during static generation are the same pre-existing, unrelated noise every prior agent in this
  chain already flagged (build still succeeds, exit 0).

## Environment notes (not product bugs, methodology only)

- Windows Git Bash mangled Cyrillic text passed as inline `curl -d '...'` / `psql -c "..."`
  arguments on the command line (matches the known PowerShell/Windows-shell UTF-8 mojibake pattern
  already in project memory) — worked around by writing all Cyrillic payloads to files first
  (`Write` tool, proper UTF-8) and feeding them via `--data-binary @file` / `docker exec -i ... < file`.
  Verified independently (raw byte decode) that the actual HTTP/DB layer never mangled anything —
  this was purely a local shell-argument artifact on my end, not a backend issue.
- Confirmed via `pg_tables`/`__EFMigrationsHistory` before testing: all 3 migrations
  (`AddLoyaltyProgram`, `FixLoyaltyTableGrants`, `AddLoyaltyMembershipConcurrencyToken`) are applied
  to dev, the 4 loyalty tables are correctly owned by `shelfguard_app_dev` (TASK-411's fix holds),
  RLS policies match every task log's description exactly.

## Cleanup performed

- Deleted both test `ConsumerAccount`/`Customer`/`LoyaltyMembership` rows and their
  `LoyaltyLedgerEntry` rows, all 4 test `PosTransaction` rows (items cascade-deleted), and the one
  throwaway repro consumer — verified 0 residual rows across every touched table afterward.
  Restored the "Вода Моршинська 1,5л" stock batch from 124.00 back to its original 140.00 (exact
  accounting of the 16 units consumed across my 4 successful test sales).
  Closed the test POS shift via the real API. Reverted tenant "Свіжий Кут"'s modules back to its
  original 5-module set (removed the `"loyalty"` module I had to enable for testing — it was not
  enabled before this session). Stopped both dev servers (backend/frontend) cleanly, confirmed by
  PID/process-name before killing.
- Did **not** touch the 4 pre-existing orphaned test tenants ("Loyalty Repo Test*"×3, "Loyalty
  Concurrency Test*"×1) — confirmed via timestamp that all predate this session (last one created
  16:54 UTC, my session started ~17:0x UTC); these were already flagged as out-of-scope leftovers
  by TASK-406/414's own logs.

## Overall verdict

**Not clear to ship.** One critical, 100%-reproducible blocker (`POST /api/consumer/loyalty/{tenantId}/join`
always 500s for every consumer via `customers` RLS) breaks the entire Фаза 0 core user journey at
its second step. Everything built on top of a manual workaround for that one gap — POS accrual/
redemption math, anti-replay, balance/cap guards, the web sales-view stitch between TASK-408 and
TASK-410, RFM segment classification of a real new customer, and all 3 of TASK-414's security
fixes (formula injection, PII masking, capability gating) — held up cleanly under live, sometimes
deliberately adversarial, testing. `dotnet test`/`tsc`/`npm run build` all green with no accumulated
regressions across the full 11-agent chain.
