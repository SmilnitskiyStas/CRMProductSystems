# TASK-510 handoff — KI-033 fix cleared, ready for QA re-verification (TASK-511)

**From:** security-reviewer
**To:** qa-tester
**Date:** 2026-08-11
**Authority:** ADR-028, TASK-510 review (`.claude/logs/tasks/510_2026-08-11_ki033-fix-security-review_security-reviewer.md`)

**Verdict: SHIP.** Independently re-verified all 5 items from the TASK-509 handoff (blast radius,
reachability, transaction-scoping, call-site containment, trust-boundary claim) plus 2 more of my
own (tenant-isolation independence, alternate-reachability sweep). No exploitable gap found. One
LOW/informational note (call-site containment is convention- not compiler-enforced) — not new,
mirrors the already-accepted `ITenantSessionOverride` pattern, not blocking.

## What to re-verify (per TASK-509's own "Definition of done" + TASK-504's original repro)

1. `GET /api/marketing-analytics/store-migration?period=6m` (no store filter), tenant
   `8abfbbb5-3190-4de9-9f91-f4de59101bca` ("Свіжий Кут"): `manager@demo.local` (store_manager,
   under-scoped to 2/4 locations — same state TASK-504 found the bug in) vs. `ea@demo.local`
   (enterprise_admin). Expect byte-identical responses (3 flows, `migratedCustomerCount: 3`,
   "Троєщина→Подільський"/"Loyal One" flow present, "Центральний→Подільський" revenue/receipts at
   the full 3124.25/22, not the previously-undercounted 3004.25/21).
2. `GET /api/marketing-analytics/overview?period=6m` same two callers — expect identical
   `periodRevenue` etc. (KI-033's description confirmed this endpoint was affected too, not just
   store-migration).
3. Spot-check `network_manager` (`netmgr@demo.local`) if it has real `user_locations` grants in
   the current demo state — KI-031 notes this account may have zero grants, which is an unrelated
   pre-existing seed-data gap, not something this fix changes or needs to fix.
4. Confirm store_manager/network_manager still correctly **cannot** see another tenant's data —
   this fix only changes `app.role`, never `app.tenant_id`; a quick cross-tenant sanity check would
   close the loop even though the RLS-level proof is already in TASK-510's log.
5. Sanity-check the `store-migration/customers` drill-down and `exports/store-migration` Excel
   export endpoints for the same store_manager account — both inherit the same
   `[Authorize]`+`[RequireModule]` gates and the same repository wrap, so they should show the same
   fix, but weren't part of TASK-504's original repro.

## Not your job (already covered)

- RLS policy correctness, injection surface, tenant-boundary independence, and containment of the
  new `marketing_analytics_bypass` value — all verified in TASK-510's log with source citations,
  live test runs against local Postgres, and a full migration-file grep. No need to re-derive.

## Next

TASK-512 (whoever picks it up) updates KI-033's status in `.claude/docs/known-issues.md` from
"open" to resolved once TASK-511 passes — reference both TASK-510 and TASK-511 in that update.
