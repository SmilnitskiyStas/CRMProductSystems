# TASK-423: Docs — Фаза 2 price segments + frequency/reactivation

**Agent:** documentation-writer
**Date:** 2026-07-27
**Status:** done — all 5 doc updates + the current.md gap-check completed, no blocker.

## Context

Read TASK-419 (database-engineer)/420 (backend-developer)/421 (frontend-developer)/422
(security-reviewer) logs, plan `deep-cooking-nygaard.md` §"Фази 2-4", and
`docs/uployal/PRICE_SEGMENTS_ANALYSIS.md`. Verified the docs additions below against the actual
source (`PriceSegmentSettings.cs`, `PriceSegmentsController.cs`, `PriceSegmentSettingsController.cs`,
`AddPriceSegmentSettings.cs` migration, `PriceSegmentDtos.cs`/`FrequencyDtos.cs`,
`PriceSegmentKey.cs`/`PriceAudienceKey.cs`/`FrequencyAudienceKey.cs`,
`PriceSegmentCatalog.cs`, and grepped `PriceSegmentsRepository.cs` for `PERCENTILE_CONT`/`NTILE`) —
not just the task logs' claims, per the docs-writer role's usual practice on this repo.

## Done

1. **`glossary.md`** — new "Price Segments & Frequency/Reactivation (Фаза 2)" section: типовий чек
   (median, not mean — the point of the metric), ціновий сегмент (quantile tier, P20-P97
   boundaries, all-time-computed), індекс цін, RealGrowth vs PriceGrowth ("по-справжньому" vs
   "через ціни"), and the 4 frequency audiences (Sleeping/Declining/Growing/Other, union not
   intersection population). `Stable`'s day-0 full-parity distinction noted inline.
2. **`database-schema.md`** — new `price_segment_settings` entry (one row/tenant, canonical RLS
   triad only, no `consumer_self_access` — staff-only like `loyalty_program_settings`), applied via
   the non-superuser app connection with no grants incident this time, `MinReceiptsForBoundaries`
   flagged as persisted-but-inert (cross-referenced to TASK-422's own note).
3. **`api-contracts.md`** — full `PriceSegmentsController`/`PriceSegmentSettingsController` routes
   (comparison/all-time/frequency modes + settings), exact query params, enum wire spellings, and
   DTO field shapes, transcribed directly from the controllers/DTOs, not just task log 420's prose.
4. **`decisions.md`** — ADR-023 addendum (not a new ADR, per the plan's own framing of Фаза 2 as an
   extension): (a) why `PERCENTILE_CONT` not `NTILE` — reusable ₴ cutoff needed across 3 call sites
   vs. Фаза 1's per-query relative bucket assignment; (b) why boundaries are all-time, not
   window-scoped — empirical evidence from the competitor doc §8.3, not a guess; (c) why `Stable`
   ships with full audience parity from day 0, unlike the competitor's KPI-only treatment.
5. **`current.md`** — grepped for TASK-419..422: 420/421/422 already had entries, **419 did not**
   (only referenced as a dependency by 420/422's own entries) — added it, matching the file's
   existing per-task format, inserted directly after TASK-420's entry. Also checked for TASK-418
   while grepping this range — no entry exists for it either, but it's outside this task's assigned
   419-422 range, so left untouched; flagging here only for the tracker owner's awareness, not acted
   on.

## Not done (out of scope, per brief)

No code changed anywhere. Did not touch `domain-model.md` (no new domain entity relationships beyond
the one settings table, already covered by the schema doc) or `known-issues.md` (the one open item —
`MinReceiptsForBoundaries` inert — is a fresh, not-yet-triaged product note, not a confirmed bug
pattern worth a KI entry yet; also already cross-referenced from both `database-schema.md` and
`decisions.md`).

## Files

- `.claude/docs/glossary.md`
- `.claude/docs/database-schema.md`
- `.claude/docs/api-contracts.md`
- `.claude/docs/decisions.md`
- `.claude/tasks/current.md` (added missing TASK-419 entry)
