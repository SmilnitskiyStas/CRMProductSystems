# TASK-540 — Block Property Editor

**Agent:** frontend-developer
**Date:** 2026-08-18
**Status:** done (verification pending live device/backend acceptance — same gap TASK-539 flagged)

## Context loaded

`CLAUDE.md`, `.claude/tasks/mobile-roadmap.md` (TASK-540 nominal entry, TASK-538/538b/539
completed entries above it), backend `BlockPropDefinition.cs`/`BlockPropTypes.cs`/
`BlockDefinition.cs`/`BlockRegistry.cs`, frontend `AppBuilderCanvas.tsx` +
`useBlockRegistry.ts` (TASK-539), `ThemeEditorSection.tsx` (TASK-537, this feature area's
form-field/validation-error-display conventions), `types.ts`, `DetailDrawer.tsx`/`Btn.tsx`
(existing shared UI primitives).

## What was built

- **`frontend/features/consumer-app/components/BlockPropertyEditor.tsx`** (new) — the property
  editor drawer. Reuses `DetailDrawer` (existing shared component, not previously used by this
  feature area) as the side panel, opened when a canvas block's new pencil-icon button is
  clicked.
- **`frontend/features/consumer-app/components/AppBuilderCanvas.tsx`** (extended) —
  `selectedBlockId` state, an `updateBlockProps(id, props)` mutator (same `withHomeBlocks`
  read-modify-write helper TASK-539 already uses for add/remove/reorder), a pencil-icon button on
  each `CanvasBlockCard` next to the existing trash icon, and the drawer rendered
  `key={selectedBlock.id}` so switching the selection remounts the form with fresh
  `defaultValues` instead of needing a manual `reset()` effect.
- **`frontend/messages/{en,uk}.json`** — new `Dashboard.consumerApp.appBuilder.propertyEditor`
  key group (drawer subtitle, unknown-block-type/no-props states, Apply button, and every generic
  validation-message key the dynamic zod schemas below use). Reuses the existing `Common.cancel`
  key for the Cancel button rather than adding a duplicate.
- No backend files touched (out of scope per the brief).

## How the generic field-renderer works (DoD's core requirement)

`PropField` (in `BlockPropertyEditor.tsx`) is the **only** place that branches, and it switches
exclusively on `BlockPropDefinitionDto.type` — six cases, one per `BlockPropTypes.cs` constant:

- `string`/`url` → `TextField` (native text input; `url` gets a placeholder hint, both respect
  `maxLength`).
- `int` → `NumberField` (native `type="number"`, `min`/`max` mirrored onto the input attributes).
- `bool` → `BoolField` (native checkbox; `register()` handles checked↔boolean binding natively,
  confirmed this works with react-hook-form's generic `Record<string, unknown>` form type).
- `enum` → `EnumField` (native `<select>` restricted to `allowedValues`).
- `stringArray` → `StringArrayField` — internally branches on whether `allowedValues` is present
  (toggle-chip multi-select when it is — the actual shape of `quickActions.actions` today — vs. a
  free-text tag input with Enter/comma-to-add when it isn't), both paths respecting
  `minItems`/`maxItems`.

There is no `block.type ===`/block-type-name branch anywhere in the file — verified by grep
(`block\.type\s*===` and each concrete block-type-name pattern), zero matches. Field labels are
derived mechanically from `def.name` via `humanizeFieldName` (camelCase → "Title Case") rather
than a hand-authored per-field label dictionary — see "Field-label tradeoff" below for why.

## Dynamic zod schema (DoD: reject invalid values client-side before they reach the canvas)

`buildPropsSchema(schema: BlockPropDefinitionDto[], t)` builds one `z.object({...})` per block
instance being edited, assembling each field's `ZodTypeAny` from its own `BlockPropDefinitionDto`
via `fieldSchemaFor` (the same six-case switch, mirrored at the schema level:
`stringFieldSchema`/`intFieldSchema`/`enumFieldSchema`/`urlFieldSchema`/
`stringArrayFieldSchema`/inline `z.boolean()`). Not one static schema per block type — a brand
new block type registered on the backend gets a correctly-validated form with zero changes here,
as long as its props use the six existing `BlockPropTypes.cs` kinds. `zodResolver` wires this into
`react-hook-form`; "Apply" is a `type="submit"` button gated by `handleSubmit`, so an invalid
value never reaches `onApply`/the canvas's in-memory `configDoc`.

One real zod gotcha hit and fixed: `z.array(...).refine(...)` returns a `ZodEffects` wrapper, not
a `ZodArray` — reassigning that back into a `let schema = z.array(...)`-typed variable is a type
error (`ZodEffects` is missing `.min`/`.max`/etc.). Fixed by `return`-ing the refined schema
directly instead of reassigning (see `stringArrayFieldSchema`'s comment). Caught by `tsc`, not a
runtime bug, but worth noting since the same pattern could bite a future edit to this file.

## How edits flow back into the canvas (DoD: matches TASK-539's persistence model)

`BlockPropertyEditor`'s "Apply" only calls `onApply(formValues)` → `AppBuilderCanvas`'s
`updateBlockProps` → `withHomeBlocks` → `setConfigDoc`/`setDirty(true)`. It never calls the
Draft CRUD API itself. Persisting to the backend still requires the canvas's own pre-existing
explicit "Save draft" button — matches this feature's no-autosave convention (`ThemeEditorSection`,
`AppBuilderCanvas` itself) and the brief's explicit instruction not to add new persistence logic.

## Field-label tradeoff (worth flagging, not a defect)

`BlockPropDefinition` (backend) carries no per-field display label — only `name`/`type`/bounds
(its own doc comment explains this is a deliberate flat descriptor, not a full schema doc). Two
options existed: (a) a hand-maintained `Record<fieldName, translatedLabel>` dictionary, or (b)
mechanically humanize the raw field name. (a) reads nicer ("Image URL") but would need a manual
addition every time a new block type introduces a new prop name — which is exactly the "zero
Property Editor code change for a new block type" constraint the DoD calls out as a violation if
skipped. Went with (b): `humanizeFieldName("imageUrl")` → `"Image Url"` — grammatically
imperfect in a few cases (acronyms), but genuinely zero-touch for any future prop. All the
*editor's own* UI chrome (title, buttons, hints, every validation message) is fully translated in
`en.json`/`uk.json` per the brief's Ukrainian-labels requirement; only the per-prop field labels
use this mechanical fallback, and that's a backend-data-shape limitation, not a translation gap.

## Note on backend props-validation (per the brief — recording only, no action taken)

`BlockPropDefinition.Required` is registry/UI metadata only, not enforced by
`MobileConfigValidator` (TASK-538's own deliberate deferral, extensively documented in
`BlockRegistry.cs`'s remarks). Having now actually built the producer UI TASK-538 was waiting on:
**the client-side zod schemas in `BlockPropertyEditor.tsx` are a reasonably direct, faithful
translation of `BlockPropDefinition`'s fields** (type/required/min/max/minLength/maxLength/
allowedValues/minItems/maxItems) — nothing was invented or guessed past what the registry already
declares. If a backend validator wanted to enforce the same constraints at save time, this
editor's `fieldSchemaFor`/`stringFieldSchema`/etc. functions are close to a literal port target
(same six cases, same bounds). That said, I'd flag one real complication before treating this as
a trivial follow-up: `MobileConfigValidatorTests.cs` (TASK-532, still passing, unmodified by this
task) explicitly asserts `"props": {}` and arbitrary extra keys (e.g. `showQr` on `loyaltyBalance`)
are valid today — retrofitting strict enforcement would still need to either accept breaking that
existing contract or scope the new validator to reject only *out-of-bounds* values on *known*
keys while still tolerating an empty or extra-keyed `props` object (i.e. type/range checking, not
presence/exhaustiveness checking). That's a smaller, more defensible scope than TASK-538 originally
worried about, but it's a real design decision, not just "wire it up" — leaving the call to the
orchestrating session on whether to spin off a TASK-540b for it now.

## Verification

- `npx tsc --noEmit` (full project) — **0 errors**.
- `npx next lint` (full project) — **0 warnings, 0 errors**.
- `npx vitest run` (full project) — **48/48 passed**, same baseline as TASK-539 (no new
  component tests added — this feature area has no existing component-test precedent either,
  same as TASK-539's own log noted).
- Live compile smoke check: started the `frontend-dev` preview server, navigated to
  `/consumer-app/pages`. Next.js Fast Refresh rebuilt cleanly with no error tied to the new
  module; the page correctly redirected under the existing `useMe()`-based client guard (same
  behavior as every sibling route). The only console errors were `net::ERR_CONNECTION_REFUSED`
  (no backend running — expected) and the pre-existing, unrelated `next-intl`
  `ENVIRONMENT_FALLBACK` timeZone warning already present on every page in this app.
- **Not run:** authenticated end-to-end interaction (open a real block's editor, edit a field,
  trigger a validation error, Apply, Save draft, reload) against a live backend + seeded
  enterprise-admin tenant. No backend instance/database/credentials were available in this
  session. TASK-539's log already flagged this same gap and proposed batching live E2E
  acceptance for TASK-539/540/541 together once the App Builder surface is more complete — this
  task doesn't change that plan.

## Scope discipline

`git status` after this task shows the new `BlockPropertyEditor.tsx`, the extension to
`AppBuilderCanvas.tsx`, and the `en.json`/`uk.json` additions — no `types.ts` changes were needed
(TASK-539 already added `BlockDefinitionDto`/`BlockPropDefinitionDto`/`MobileConfigBlockInstance`,
which this task consumes as-is), no new hooks, no backend files.
