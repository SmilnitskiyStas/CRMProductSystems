# TASK-475: Post-campaign docs — glossary/schema/API/domain-model/ADR (Фаза 4)

**Agent:** documentation-writer
**Date:** 2026-08-06
**Status:** done — 5 docs updated, no code touched

## Context

Read TASK-471 (database-engineer), TASK-472 (backend-developer), TASK-473 (frontend-developer),
TASK-474 (security-reviewer), TASK-477 (backend-developer, incl. its empirical follow-up addendum)
task logs, plus TASK-432 (`.claude/logs/tasks/432_2026-07-27_audience-builder-docs_documentation-writer.md`)
as the closest-precedent template (Фаза 3's own docs pass). Cross-checked every claim against the
actual shipped code rather than transcribing task-log prose: `PostCampaignSegment.cs`,
`PostCampaignSegmentMember.cs`, `PostCampaignController.cs`, `PostCampaignService.cs`,
`PostCampaignDtos.cs`, `SegmentImportParser.cs`, `IPostCampaignRepository.cs`,
`PostCampaignBehaviorClassifier.cs`, `PostCampaignBehaviorStatus.cs`, `ImportLimits.cs`,
`MarketingAnalyticsAuthorization.cs`, `AddPostCampaignSegmentSchema` migration, and
`AppDbContext.cs`'s fluent config for both new entities.

## Done

1. **`glossary.md`** — new "## Post-Campaign Analysis (Фаза 4)" section: the concept itself, draft
   vs. analyzed segment (the nullable-date-columns state, no boolean/enum), before/after window
   auto-derivation, the four behavior states (Реактивовані/Утримані/Відпали/Не повернулись — pulled
   verbatim from `frontend/messages/uk.json`, not invented), the RFM migration matrix, and segment
   hash in this feature's specific once-per-analyze sense (vs. Фаза 1-3's per-request `filtersHash`).
2. **`database-schema.md`** — new "## TASK-471 — Post-campaign segment schema" entry, same
   format/depth as the existing "## TASK-419 — Price segment settings schema" entry: both tables,
   RLS posture (canonical triad, no `consumer_self_access`, same as `price_segment_settings`), the
   draft-vs-analyzed nullable-date design, indexes, and the live-verification summary.
3. **`api-contracts.md`** — new "### Post-Campaign Analysis" section, all 11 controller actions
   (see discrepancy note below) verified directly against `PostCampaignController.cs`/
   `PostCampaignDtos.cs`, not just the task logs. Explicitly calls out the `Import`-vs-everything-
   else auth asymmetry (`CanImportSegments` is a stricter, role-only floor) and documents the
   two-layer import size guard (ZIP-entry uncompressed-size pre-check, then row/column check) with
   the 10 MB request cap / 25,000-row / 300-column / 20 MB-per-ZIP-entry numbers and why the
   ZIP-level check is the one that actually matters.
4. **`domain-model.md`** — new `PostCampaignSegment`/`PostCampaignSegmentMember` entity entries,
   same format as the existing `LoyaltyMembership`/`LoyaltyLedgerEntry` entries, plus a
   "Relationships to existing entities" subsection (Customer via id-or-phone match,
   User.created_by_user_id, PosTransaction read-only, Tenant/module key).
5. **`decisions.md`** — new addendum to ADR-023 (Фаза 4), appended after the existing Фаза 3
   addendum, same "**Addendum (TASK-nnn, date) — Фаза N ...**" convention. Five numbered decisions:
   (a) breaking the Фаза 1-3 stateless precedent and why the four nullable date columns ARE the
   draft/analyzed state; (b) import identity matching reusing Фаза 0's `PhoneNormalizer`, not a new
   concept; (c) the RFM migration matrix's 3-call reuse of `GetScoredCustomersAsync`/
   `RfmSegmentClassifier` with zero new RFM logic; (d) the XLSX-bomb security story as its own
   clearly-marked subsection — TASK-477's first fix (post-parse row/column check) verified
   *empirically* insufficient, real cost lives inside `new XLWorkbook(stream)` itself, with the
   measured numbers table (25k/250k/1,048,576 rows → ctor time/allocation, headlining the <5 MB
   file / ~38s / ~1.7GB result) and the resulting two-layer guard now framed as the required
   pattern for this codebase's next upload feature; (e) `CanImportSegments` being role-only with no
   new `TenantRoleCapabilities` entry, citing the `ReceiptsView` write-action-stays-out-of-catalog
   precedent (ADR-020 point 3).

All 5 files' `**Updated:**` header dates bumped to 2026-08-06.

## Discrepancy found (per brief's instruction: trust the code over the task-log prose)

TASK-472/474/477's logs all describe the controller as having "10 endpoints." Reading
`PostCampaignController.cs` directly counts **11** distinct actions/routes: `GET segments`,
`POST segments/import`, `POST segments/{id}/analyze`, `GET {id}/summary`, `GET {id}/daily-turnover`,
`GET {id}/rfm-activity`, `GET {id}/customers`, `GET {id}/migration`, `POST {id}/explain`,
`POST {id}/exports/customers`, `POST {id}/exports/unknown-tokens`. The task logs' own bullet lists,
when counted, also enumerate 11 items — "10" appears to be a miscount in their prose summary line,
not a sign of a missing/extra endpoint in the code. `api-contracts.md` documents all 11 actual
routes individually and avoids asserting either digit as a summary count, so this doesn't leave a
wrong number sitting in the docs.

## Not in scope / not changed

- No code changes (docs only, per brief).
- `known-issues.md` — explicitly out of scope per brief (zero KI entries exist for any phase of
  this whole initiative, tracked independently).
- Фаза 0-3's own existing docs sections — not re-described, only Фаза 4 additions made.
- `.claude/tasks/current.md` — new TASK-475 entry added (see below) per brief's deliverable list;
  no edits to any other existing entry.

## Git

Not committed (repo convention — main session/user commits).

## Files

- `.claude/docs/glossary.md`
- `.claude/docs/database-schema.md`
- `.claude/docs/api-contracts.md`
- `.claude/docs/domain-model.md`
- `.claude/docs/decisions.md`
- `.claude/tasks/current.md` (new TASK-475 entry added to top)
