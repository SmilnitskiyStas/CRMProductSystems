# Handoff: TASK-570 → backend-developer / mobile-developer / frontend-developer

**From:** project-architect
**Full brief:** `.claude/logs/tasks/570_2026-08-19_catalog-curation-architecture_project-architect.md`
**ADR:** `.claude/docs/decisions.md` ADR-032

## What this is

Phase 1 (deliberately descoped) of catalog curation: let a retailer admin pick specific products for
a `productGrid`/`productCarousel` block instead of "first N alphabetically" (today's only option).
Bestsellers/personalization/personal-discounts/POS-bonus were raised alongside this by the user and
explicitly deferred to a separate future initiative — not designed, not scaffolded here.

New `BlockPropTypes.ProductIds` kind (not a `stringArray` name special-case — read ADR-032 Decision 1
if the "why not just reuse stringArray" question comes up), a new `ProductPickerField.tsx` picker
component, and — the part that isn't obvious from the surface ask — a new bounded catalog-by-ids read
path on **both** the mobile and web-preview sides, because both existing catalog fetches
(`PageRenderer.tsx`'s hardcoded `pageSize=30`, `AppPreviewPanel.tsx`'s `/api/items` default
`pageSize=50` with no search/id filter) only ever see a short alphabetical prefix of the catalog —
without this fix, a curated pick outside that window would silently and incorrectly resolve as
"deleted." See ADR-032 Decision 3 for the full finding.

## Spawn plan (3 parallel spawns, 6 tasks)

1. **backend-developer → TASK-571 then TASK-572, one spawn, sequential.** TASK-571 adds the new
   `BlockPropTypes.ProductIds` constant + 2 registry entries (`productGrid.productIds` MaxItems=30,
   `productCarousel.productIds` MaxItems=20) — small, matches TASK-561's own shape. TASK-572 is
   larger: gives admin `/api/items` a `search` + `ids` filter (new — that endpoint has zero text
   search today), and adds a brand-new consumer endpoint
   `GET /api/consumer/{tenantId}/catalog/by-ids`. Sequential in one spawn (not two parallel spawns)
   per this repo's own prior incident where disjoint-file parallel backend edits still broke each
   other's `dotnet build` via shared solution state — see memory
   `feedback-parallel-ef-migrations-need-worktree`; playing it safe even though these two tasks'
   files don't actually overlap.

2. **mobile-developer → TASK-573.** Can start immediately using the prop table below + TASK-572's
   endpoint contract (not a runtime dependency on either backend PR merging, same precedent as
   TASK-562). Real correctness-critical part: `PageRenderer.tsx` currently fetches catalog with a
   hardcoded `{ page: 1, pageSize: 30 }` — this task adds a merged `catalogById` lookup so
   `resolveBlocks.ts` can resolve ANY curated id, not just ones in that first alphabetical page.
   **Read this task's brief carefully** — it's the mobile-side half of the "catalog fetch is too
   short" finding, same class of correctness bug as TASK-560's prop-forwarding gap.

3. **frontend-developer → TASK-574 then TASK-575, one spawn, sequential.** TASK-574: the new
   `productIds` case in `BlockPropertyEditor.tsx`'s three switches (schema/coerce/field-render) +
   new `ProductPickerField.tsx` + `search`/`ids` params on `catalogApi.getAll`. TASK-575: the same
   curated-resolution logic added to `blockPreviews.tsx`'s `ProductGridPreview`/
   `ProductCarouselPreview` + `AppPreviewPanel.tsx`'s own catalog-by-ids fetch (mirrors TASK-573's
   mobile fix — the web preview has the identical "only sees a short catalog prefix" gap via
   `/api/items`'s `pageSize=50` default). Sequential because TASK-575 uses TASK-574's new hook.

4. **qa-tester → TASK-576** once 571/572/573/574/575 are all done.

## New prop table (source of truth — mobile and frontend both mirror this by hand, no shared package)

| Block type | Prop | Type | Default | MinItems | MaxItems |
|---|---|---|---|---|---|
| `productGrid` | `productIds` | `productIds` | `[]` | 0 | 30 |
| `productCarousel` | `productIds` | `productIds` | `[]` | 0 | 20 |

`promotionGrid`/`promotionCarousel` get nothing — out of scope (ADR-032).

## Resolution semantics (must be identical in `resolveBlocks.ts` and `blockPreviews.tsx`)

Non-empty `productIds` → resolve in the admin's exact chosen order, silently skip any id that's
missing or whose item has `priceRetail === null`, then cap to `limit`. Empty/absent `productIds` →
byte-identical to today: `ctx.catalog.filter(priceRetail !== null).slice(0, limit)`, alphabetical.
Full algorithm + code sketch in the task log's "Resolution algorithm" section.

## No worktree isolation needed between the three spawns

`backend/`, `mobile/`, `frontend/` are disjoint trees — same precedent as TASK-560. Worktree
isolation only matters *within* the backend spawn's own two sequential tasks, per the note above,
and even there a worktree isn't required since they're sequential in one session, not concurrent.

## If a spawned agent hits a genuine unresolved decision

Stop and report back rather than guessing — per CLAUDE.md's clarify-before-implementing gate. Two
spots most likely to raise a question, both already pre-decided in the brief:
- Mobile's `getConsumerCatalogByIds` needs the `ids` query param serialized as repeated
  `ids=<guid>&ids=<guid>` (not `ids[]=`) to match ASP.NET Core's `Guid[]` model binder — brief flags
  this as something to verify against `personalApiClient`'s actual axios config, not assume.
- The product picker has no drag-reorder in this phase — selection order = display order, reorder
  via remove-and-re-add. This was a deliberate scope call (ADR-032 Decision 4), not an oversight —
  don't add drag-reorder without checking back first if it looks tempting to include.
