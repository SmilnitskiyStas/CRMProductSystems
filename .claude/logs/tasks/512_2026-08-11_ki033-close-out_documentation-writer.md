# TASK-512: KI-033 close-out (docs only)

**Status:** done
**Agent:** documentation-writer
**Authority:** `.claude/logs/handoffs/511-to-512_qa-tester.md`

## What changed

### `.claude/docs/known-issues.md`
- Bumped `**Updated:**` header to 2026-08-11.
- KI-033 title line: appended `✅ resolved (2026-08-11, TASK-508..511)`.
- KI-033 `Status:` line: `open, needs architecture decision` → `✅ resolved (2026-08-11)`, with the
  TASK-508→509→510→511 chain cited inline.
- Replaced the old `Resolution (not applied — needs an architecture-level decision...)` paragraph
  with the applied fix: `marketing_analytics_bypass` RLS bypass role-value on `pos_transactions`'
  `store_scope` policy, activated only via the new `IAnalyticsRlsOverride` primitive inside
  `MarketingAnalyticsRepository` — points to ADR-028 for full reasoning rather than repeating it.
  Kept the original Description/Root cause sections untouched (historical repro record).
- Captured the nuance from the QA handoff precisely: the fix is not role-conditioned — it applies
  uniformly to every caller that clears `MarketingAnalyticsController`'s existing
  `[Authorize]`+`[RequireModule]` gates, because the trust boundary was already established once at
  the controller. Stated the `network_manager`/KI-031 side effect exactly as briefed: marketing
  analytics specifically is no longer affected by KI-031's zero-grants symptom (incidental, verified
  in TASK-511), but KI-031 itself stays open, unaffected, for every other RLS-scoped module.
  Explicitly did **not** phrase this as "KI-033 fixed KI-031."

### `.claude/docs/decisions.md`
ADR-028 was already in a good final state (reasoning unchanged, nothing to rewrite). Added a short
status close-out only: `Status: accepted` → `accepted — implemented and verified (TASK-509
implementation, TASK-510 security review: SHIP/0 blocking findings, TASK-511 independent QA
re-verification: byte-identical to the RLS-exempt baseline; all 2026-08-11)`, with a pointer to
KI-033 for the closed-out status and the KI-031 nuance. No other line touched.

## Final KI-033 status text
`Status: ✅ resolved (2026-08-11) — found 2026-08-10 (TASK-504...); fixed via TASK-508 (design,
ADR-028) → TASK-509 (implementation) → TASK-510 (security review, clean) → TASK-511 (independent QA
re-verification, byte-identical results for the originally-affected account).`

## Scope note
Docs only, as instructed — `known-issues.md` and `decisions.md`. No code touched.
