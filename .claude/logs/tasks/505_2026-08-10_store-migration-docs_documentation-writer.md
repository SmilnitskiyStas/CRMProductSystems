# TASK-505: Store migration docs (renumbered from plan's TASK-483)

**Status:** done
**Agent:** documentation-writer

## What changed

### `.claude/docs/api-contracts.md`
Added a new "Store Migration" section after the existing "Post-Campaign Analysis" section (end of
file, ~line 1241), matching that section's format exactly: gate description, shared query params,
route table, per-endpoint DTO shape in `ts` blocks, behavior notes.

Documented the 3 new endpoints per `.claude/logs/handoffs/502-to-503_backend-developer.md`:
- `GET /api/marketing-analytics/store-migration` → `StoreMigrationOverviewDto`
- `GET /api/marketing-analytics/store-migration/customers` → `StoreMigrationCustomerRowDto[]`
  (flagged as not-in-original-plan, added because the drill-down was needed for both on-screen and
  export use)
- `POST /api/marketing-analytics/exports/store-migration` → `.xlsx`

Called out the two behaviors most likely to surprise a frontend/API consumer: the migration
definition is first→last-in-period only (not every hop), and the store filter is OR-semantics
(from-store OR to-store), unlike the AND-style inclusion filter used everywhere else in this
controller. Also noted: no `filtersHash`/`calculatedAt` on the overview DTO, phone/email always
masked on the customers GET (no unmask param — only the export can unmask), no `key` field on the
export body (no RFM-segment concept here).

Added a short cross-reference at the end of the new section pointing to KI-033 (below) so a reader
of the contract sees the data-correctness caveat inline, not just in known-issues.md.

### `.claude/docs/known-issues.md`
Added **KI-033** (new entry, inserted at the top of Active Issues, above KI-032) per
`.claude/logs/handoffs/504-to-backend_qa-tester.md`: the `pos_transactions` `store_scope`
RESTRICTIVE RLS policy (TASK-393) silently corrupts marketing-analytics results — including
store-migration and the pre-existing RFM overview — for any caller whose role isn't
provider/provider_admin/worker/enterprise_admin. For store-migration this isn't just
undercounting, it can reclassify a genuinely-migrated customer as "not migrated." Followed the
KI-030 precedent for presenting an unresolved architecture decision: `Status: open, needs
architecture decision`, and a lettered `Resolution (not applied — ...)` list with the 3 directions
from the QA handoff: (a) bypass RLS for this read path (provider_bypass/worker_bypass precedent),
(b) keep scoping but surface partial-visibility explicitly, (c) a hybrid. Explicitly did not touch
any code or the RLS policy itself — documentation only.

Distinguished KI-033 from KI-031 explicitly in the entry text: KI-031 is an under-seeded demo
account (netmgr@demo.local, zero grants); KI-033 affects normally-provisioned store_manager
accounts too, which is the expected shape of that role, not a seed-data gap.

Bumped the file's `**Updated:**` header to 2026-08-10.

## Verification
Read back both edited sections after writing. `### KI-\d+` grep confirms KI-033 is unique and
sequential (no duplicate numbers, no gaps introduced). Header/list nesting in the new
api-contracts.md section matches sibling sections (Фаза 1-4) — same `###`/`####`/fenced-block
pattern, `---` separator before the new section.

## Scope note
Docs only, as instructed. No code, no RLS policy change — that fix is explicitly deferred to a
project-architect decision per KI-033's own text.
