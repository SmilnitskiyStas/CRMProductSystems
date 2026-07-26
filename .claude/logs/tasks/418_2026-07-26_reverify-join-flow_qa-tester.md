# TASK-418: Re-verify consumer loyalty join flow after TASK-417 fix

**Agent:** qa-tester
**Date:** 2026-07-26
**Status:** done — **verdict: clear to ship.** TASK-417's fix holds under live re-test; no regressions found. Series TASK-404..417 is clean.

## Scope

Focused re-verification (not a full repeat of TASK-416) against real `dotnet run` backend on :5000 and
live Postgres (`crmproductsystems-postgres-1:5435`, dev DB `crm`). No mocks.

## 1. Exact TASK-416 repro — FIXED

`POST /api/consumer-auth/register` → `POST /api/consumer/loyalty/{tenantId}/join` (tenant = Свіжий Кут,
loyalty module temporarily enabled, same as TASK-416): **200**, not 500.
`Customer` (`3adc6ea3-...`) and `LoyaltyMembership` (`fb7e0077-...`) both confirmed in DB with correct
`TenantId` (queried as superuser `crm` to bypass RLS for verification — querying as `shelfguard_app_dev`
with no session GUCs set returns 0 rows by design, not a bug, matches the RLS model this fix concerns).

## 2. Full chain past the former blocker — PASS

- `GET code` → live rotating `SGLOY1.{id}.{code}`.
- `POST /api/loyalty/resolve-code` as `manager@demo.local` (store_manager) → correct
  customerId/customerName/maskedPhone.
- `POST /api/pos/sales` (10× "Вода Моршинська 1,5л" @14.00 = 140.00, customerId+loyaltyMembershipId) →
  201, `loyaltyAccrued: 4.20` (3% default rate). Verified byte-for-byte in DB: `LoyaltyMembership.Balance
  = 4.20`, one `loyalty_ledger_entries` row (accrual, BalanceAfter 4.20), and via
  `GET /consumer/loyalty/{tenantId}/history` — all three agree.

## 3. Second tenant, cross-tenant wallet — PASS, no leakage

No second dev tenant already had `loyalty` enabled (the pre-existing `Loyalty Repo/Concurrency Test *`
tenants are empty-module leftovers correctly left untouched, per TASK-416/406/414). Created a fresh
throwaway tenant (`TASK-418 Second Tenant`, `Modules: ["loyalty"]`).

- Same consumer JWT → `POST join` on tenant 2 → 200, new membership, balance 0.00.
- DB confirms a **separate** `Customer` row per tenant (no row reuse across tenants) and a separate
  `LoyaltyMembership` per tenant.
- `GET /api/consumer/loyalty/memberships` → both memberships returned, each with its own correct
  balance (4.20 / 0.00), no cross-tenant bleed.

## 4. Spot-check regressions (light sample, not full pass) — all 4 clean

- Redemption-cap guard: redeem 10.00 on a 14.00 sale (cap 50% = 7.00) → 400 "exceeds the redemption
  cap". Insufficient-balance guard: redeem 5.00 vs 4.20 balance (within cap) → 400 "Insufficient loyalty
  balance". Neither rejection touched balance or stock (confirmed via DB before/after).
- Anonymous sale (no customer/loyalty fields) → 201, all loyalty fields null, coexists fine in the same
  shift.
- RFM "New" segment export + PII masking (combined check): exported `New` segment as store_manager →
  200, valid xlsx, our fresh customer ("QA418 Reverify Consumer") present — confirms new-customer RFM
  classification — with phone masked as `+380 67 *** ** 01` (correct format). Did not re-run the
  formula-injection adversarial payload from TASK-416/414 — that code path is untouched by TASK-417's
  diff and was already exhaustively verified live; out of scope for a focused pass.

## 5. `dotnet test`

Full suite: **1109/1109 passed**, 0 failed, 0 skipped — matches TASK-417's reported count exactly, no
accumulated regressions.

## Cleanup

Deleted both test `PosTransaction` rows (items cascade-deleted), the `LoyaltyLedgerEntry`, both
`LoyaltyMembership` rows, both `Customer` rows, and the `ConsumerAccount` — verified 0 residual rows
across every touched table afterward. Restored "Вода Моршинська 1,5л" stock from 129.00 back to its
original 140.00 (11 units across the 2 real test sales). Closed the test POS shift via the real API.
Reverted Свіжий Кут's modules back to its original 5-module set. Dropped the throwaway second tenant.
Stopped the backend dev server process I started for this session.

## Overall verdict

**Clear to ship.** TASK-417's `ITenantSessionOverride` fix eliminates the critical 100%-reproducible
join-flow 500 with no observed regressions anywhere I touched. Cross-tenant membership isolation holds
correctly for the same consumer joining two different tenants. This closes out the TASK-404–417 series.
