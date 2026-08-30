# TASK-646 — Marketplace provider-RLS leak: documentation

**Agent:** documentation-writer · **Model:** haiku-tier task, run inline · **Date:** 2026-08-30 · **Status:** done
**Source:** TASK-641..645 task logs + plan `snappy-dreaming-hanrahan.md`. Assembly only, no new design.
**Not committed.** Docs-only — no code, tests, `database-schema.md` or `prelaunch-readiness.md` touched (TASK-642 owned those).

## Files touched

| File | Change |
|---|---|
| `.claude/docs/decisions.md` | **ADR-035** prepended above ADR-034; `**Updated:**` → 2026-08-30 |
| `.claude/docs/known-issues.md` | **KI-036** added at top of Active Issues; **KI-028** forward-reference note added; `**Updated:**` → 2026-08-30 |
| `.claude/docs/backend-structure.md` | `IProviderRlsOverride` added as the 3rd `SET LOCAL` override primitive in the Tenant Context section + `MarketplaceOrderService` app-level `TenantId` exception note + F7 `DISCARD ALL` line; `**Updated:**` → 2026-08-30 |
| `.claude/tasks/current.md` | Added **TASK-643** (incl. 643b remediation) and **TASK-646** entries; 641/642/644/645 already present and well-formed, left as-is |
| `.claude/logs/tasks/646_...md` | this log |

## Identifiers assigned

- **ADR-035** — "`IProviderRlsOverride` — scoping the marketplace provider bypass to one repository method, replacing session-level `SET app.role`". Status: accepted, implemented + reviewed (final SHIP), not committed/deployed. Supersedes: nothing.
- **KI-036** — severity critical; status resolved by TASK-641..645 (2026-08-30), not yet deployed. Next number after KI-035 (verified highest).

## Content decisions (all from the briefs / source logs, none new)

- ADR-035 Decision 2 records the sentinel as **deferred hardening** with the concrete revisit trigger (new call site outside `MarketplaceRepository`, or a block body touching a non-marketplace table / calling outward). `provider_bypass` count phrased as "107 measured 2026-08-30, 109 a day later, grows with every new RLS table" — never a bare fixed number.
- KI-036 blast radius covers all four of R6: (i) read disclosure, (ii) F2 write vector incl. the `.Include`d-graph 4-table rewrite, (iii) F5 cross-tenant Claude API-key consumption (called out as resolved-by-this-fix), (iv) C1 order-number scheme.
- TASK-644's verbatim pre-fix failure output quoted in KI-036's verification chain.
- KI-036 cross-references KI-028 and KI-030; KI-028 gets the reciprocal forward reference.
- F7 (`DISCARD ALL` / `No Reset On Close`, `supplier_admin` not in `ValidRoles`) recorded in both ADR-035 Consequences and `backend-structure.md`.

## Verification

Docs-only pass — no build/test run. Cross-checked ADR-035 / KI-036 claims against the five source logs; no contradictions.
