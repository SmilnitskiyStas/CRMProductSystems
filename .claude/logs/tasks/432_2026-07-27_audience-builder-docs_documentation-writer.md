# TASK-432: AudienceBuilder documentation (Фаза 3 docs pass)

**Agent:** documentation-writer
**Date:** 2026-07-27
**Status:** done — 4 docs updated, `current.md` verified consistent, no code touched

## Context

Read TASK-428 (database-engineer), TASK-429 (backend-developer), TASK-430 (frontend-developer),
TASK-431 (security-reviewer) logs, plan `deep-cooking-nygaard.md`, and
`docs/uployal/AUDIENCE_PREPARATION_ANALYSIS.md`. Cross-checked the API contract directly against
`AudienceBuilderController.cs`/`AudienceBuilderDtos.cs`/`IAudienceBuilderRepository.cs`/
`AudienceBuilderRepository.cs` rather than trusting the task logs alone — confirmed the exact
`Text`-term match logic (`Item.Id`::text OR `Item.Barcodes` jsonb containment OR `Item.Name ILIKE`,
one OR clause) and the exact migration DDL (`CREATE INDEX idx_items_name_trgm ON items USING gin
("Name" gin_trgm_ops)`) from the actual code/migration rather than paraphrasing the logs.

## Done

1. **`glossary.md`** — new "## Audience Builder (Фаза 3)" section: the audience-builder concept,
   `Term` (incl. the Id/Barcodes/Name match detail — no separate external-id column exists, `Item.Id`
   fills that role), Any/All (OR/AND), **term coverage** (the AND-mode double-counting fix TASK-429
   found and fixed), manual SKU curation, competitive audience, and the two exclusion horizons
   (InPeriod/AllTime, with the competitor analysis' own ~23% ratio cited).
2. **`database-schema.md`** — new "## TASK-428 — `items.Name` trigram index" section: the index DDL
   + the RLS/non-leakproof-ILIKE limitation (Seq Scan ~1085ms on the real app connection vs Bitmap
   Index Scan ~2ms only for a superuser bypassing RLS), explicitly framed as an accepted v1
   limitation, not a defect to "fix" by re-tuning the index. Cross-references the same finding
   already silently affecting the pre-existing `idx_notification_queue_title_trgm`, and points to
   the new ADR addendum for the three-option tradeoff.
3. **`api-contracts.md`** — new "### Audience Builder" section, all 8 routes (`GET /categories`,
   `POST /overview|buyers|matched-items|exports/buyers|competitor/overview|competitor/buyers|
   exports/competitor-buyers`) verified against the live controller, not just the task log — DTOs,
   `sortBy` allowlists, PII/capability posture, cross-linked to the glossary/schema/ADR entries above.
4. **`decisions.md`** — new ADR-023 addendum (Фаза 3, alongside the existing Фаза 2 addendum): the
   "accept Seq Scan" decision with all 3 options considered (mark `texticlike` LEAKPROOF globally /
   add a `SECURITY DEFINER` search function / accept the Seq Scan as-is) and why option 3 — the most
   conservative, zero new security-posture surface, fully reversible later — was chosen for v1.
5. **`current.md`** — verified TASK-428 through TASK-431 entries: all four present, dependency
   chain and cross-references internally consistent (428→429→430→431; each "Next" pointer matches
   what the following entry actually records, e.g. TASK-429's "handled per orchestrator's
   accept-for-v1 decision" lines up with TASK-428's flagged options and this task's new ADR text).
   No gaps found. Not edited — tracker updates are the user's/orchestrator's own responsibility per
   this session's brief (TaskUpdate unavailable to spawned agents).

## Not in scope / not changed

- No code changes (docs only, per brief).
- `domain-model.md` — no new entities from Фаза 3 (AudienceBuilder computes everything live off the
  existing `Item`/`Customer`/`PosTransaction`/`categories` tables, same as RFM/Price Segments) —
  nothing to add there.
- `known-issues.md` — the Seq Scan tradeoff is deliberately documented as an **accepted decision**
  (`decisions.md`) and a **known v1 limitation** (`database-schema.md`), not an open defect — did
  not also file it as a KI entry, consistent with how Фаза 2's `MinReceiptsForBoundaries` inert-field
  gap was handled (ADR note only, no KI entry).

## Git

Not committed (repo convention — main session/user commits).
