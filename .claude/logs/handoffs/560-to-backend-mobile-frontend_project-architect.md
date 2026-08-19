# Handoff: TASK-560 → backend-developer / mobile-developer / frontend-developer

**From:** project-architect
**Full brief:** `.claude/logs/tasks/560_2026-08-19_app-builder-live-preview-architecture_project-architect.md`
**ADR:** `.claude/docs/decisions.md` ADR-031

## What this is

Elementor-style live preview panel for the App Builder (`/consumer-app/pages`,
`AppBuilderCanvas.tsx`) — instant visual feedback on add/remove/reorder/property-edit, plus a new
drag-to-resize control for 4 block types, no save round-trip. Web-native mirror components (not
react-native-web), zero new backend endpoints — read the ADR for the reasoning on both.

## Spawn plan (3 agent spawns, 6 tasks)

1. **backend-developer → TASK-561.** Start immediately. Adds 4 `BlockPropDefinition` entries to
   `BlockRegistry.cs` + 1 focused test. Small, no other file touched.

2. **mobile-developer → TASK-562.** Can start as soon as TASK-561's prop table (below) is
   confirmed — not a runtime dependency on TASK-561's PR landing. **Read this task's brief
   carefully**: it contains a real correctness bug fix in `resolveBlocks.ts` (3 of the 4 new props
   get silently dropped by the existing prop-rebuilding switch statement unless explicitly
   forwarded) — this is not optional polish, it's the difference between the web preview lying
   and telling the truth.

3. **frontend-developer → TASK-563, 564, 565, in that order, one spawn.** These three are a
   sequential chain in the same file tree (`frontend/features/consumer-app/`) — do not split
   across separate spawns/worktrees, later tasks depend on earlier ones' new files
   (`PhoneFrame.tsx`, then `blockPreviews.tsx`/`AppPreviewPanel.tsx`, then the live-edit + drag
   wiring on top of both). TASK-565 additionally needs TASK-561's registry bounds
   (`useBlockRegistry()`'s `validationSchema` at runtime) to clamp resize drags — sequence
   accordingly if TASK-561 is still mid-flight when this spawn starts.

4. **qa-tester → TASK-566** once 561/562/564/565 are all done.

## New prop table (source of truth — mobile and frontend both mirror this by hand, no shared package)

| Block type | Prop | Default | Min | Max |
|---|---|---|---|---|
| `heroBanner` | `heightPx` | 190 | 120 | 260 |
| `bannerCarousel` | `cardWidthPx` | 280 | 200 | 360 |
| `promotionCarousel` | `cardWidthPx` | 210 | 150 | 270 |
| `productCarousel` | `cardWidthPx` | 170 | 120 | 220 |

## No worktree isolation needed

`backend/`, `mobile/`, `frontend/` are disjoint trees — TASK-561/562/563 can run fully concurrently
in the main working tree with zero file-collision risk, per this repo's parallel-agent convention
(worktree isolation is for when two agents might touch the *same* files).

## If a spawned agent hits a genuine unresolved decision

Stop and report back rather than guessing — per CLAUDE.md's clarify-before-implementing gate. The
two most likely spots, already pre-decided in the brief so this shouldn't recur, but flagging in
case something looks off during implementation:
- `usePromoProducts` needs a `storeId` the App Builder screen has no selector for — brief says use
  the tenant's first `useLocations()` result, preview-only.
- `loyaltyCard`/`loyaltyBalance` have no admin-side data source — brief says render clearly-labeled
  sample data, not real-looking fake data.
