# TASK-511 handoff — KI-033 independently re-verified RESOLVED (documentation-writer to update status)

**From:** qa-tester
**To:** documentation-writer
**Date:** 2026-08-11
**Authority:** TASK-511 (`.claude/logs/tasks/511_2026-08-11_ki033-reverify_qa-tester.md`)

## Status for `.claude/docs/known-issues.md`

**KI-033: resolved.** Mark it resolved, referencing TASK-508 (design/ADR-028), TASK-509
(implementation), TASK-510 (security review), TASK-511 (this QA re-verification).

Independently re-ran the exact TASK-504 repro (same tenant `8abfbbb5-...`, same two accounts) plus
the drill-down/export endpoints and a live UI check:
- `manager@demo.local` (store_manager, restored to its true under-scoped 2/4-location grant state —
  found it was NOT actually restored as TASK-509 claimed, fixed via SQL before testing, see task
  log) now gets **byte-identical** responses to `ea@demo.local` on `/store-migration` (both 6m and
  3m periods), `/overview`, `/store-migration/customers`, and the OR-semantics single-store filter.
- Export endpoint (masked + unmasked) verified by unzipping the actual `.xlsx` — correct PII
  masking, all 3 customers present including the previously-vanished "Loyal One" flow.
- Live UI on `/marketing-analytics` as `manager@demo.local` shows the full, correct 3-flow picture.
- Cross-tenant isolation independently re-verified at the Postgres level (not just cited from
  TASK-510): the new `marketing_analytics_bypass` role value only widens `store_scope`; the
  separate `tenant_isolation` policy is untouched and still fully enforced.
- Full regression pass: `dotnet test` 1400/1400 green, `tsc --noEmit` clean, no latency issue from
  the new transaction wrapping (40–84ms per call).

## One correction needed in the paper trail (not a blocker, please reflect accurately)

The TASK-509/510 handoffs assumed `network_manager` (`netmgr@demo.local`, tied to the separate
**KI-031**, still open) would be **unaffected** by this fix. That assumption is incorrect — please
don't copy it forward as fact:

- `netmgr@demo.local` still has 0 `user_locations` grants (KI-031's root cause, confirmed
  unchanged), and still sees 0 rows on unrelated modules (e.g. `GET /api/stock`) — so KI-031 itself,
  as originally described (network_manager gets zero data across stock/sales/write-offs), is
  correctly still open and unaffected outside marketing-analytics.
- But within marketing-analytics specifically, `netmgr@demo.local` now gets **full, correct data**
  (byte-identical to `ea@demo.local`) — not the "still zero" behavior expected. Reason: the fix's
  `SET LOCAL app.role = 'marketing_analytics_bypass'` applies unconditionally to every call into
  the 13 wrapped repository methods, regardless of the *original* caller's role/grants — it isn't
  gated on "only widen access for an under-scoped store_manager." So it also happens to eliminate
  KI-031's specific *marketing-analytics* symptom as a side effect, while leaving KI-031's broader,
  still-open problem (other modules) untouched.

When you write up KI-033's resolution, please state precisely that it resolves the store_manager
partial-scoping bug (as originally reported) and, as an incidental side effect, also fixes
marketing-analytics specifically for the KI-031 zero-grants case — but does not touch or resolve
KI-031 itself, which remains open exactly as before for every other RLS-scoped module. Full detail
and live evidence in the TASK-511 task log.

## Not your job (already covered, no need to re-derive)

RLS policy correctness, injection surface, tenant-boundary independence — all in TASK-510's log and
independently spot-checked again by me at the Postgres level in TASK-511 (see task log's "Cross-tenant
isolation" section).
