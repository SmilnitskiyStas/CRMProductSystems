# TASK-537 — Theme Editor UI with live preview

**Status:** `review`
**Agent:** `frontend-developer`
**Date:** 2026-08-17

## Scope delivered

Replaced TASK-535's `/consumer-app/design` placeholder with a real Theme Editor for the
tenant's single `MobileTheme` row (TASK-536's `GET`/`PUT /api/v1/mobile/theme`).

### Files

- `frontend/features/consumer-app/types.ts` — added `MobileThemeDto`,
  `UpdateMobileThemeRequest`, `MobileThemeValidationError`, `ThemeSpacingPreset`, and
  `THEME_*` constants that mirror `MobileThemeWhitelists.cs` client-side (button radius 0-32,
  card radius 0-40, logo URL max length 2048, hex pattern, `compact`/`comfortable` spacing —
  read directly from the backend source, not guessed).
- `frontend/features/consumer-app/api/mobileTheme.ts` (new) — `fetchMobileTheme`,
  `updateMobileTheme`, and `extractThemeValidationErrors` (see below).
- `frontend/features/consumer-app/hooks/useMobileTheme.ts` (new) — `useMobileTheme` (GET,
  `staleTime` 30s, matches `useLoyaltySettings`) and `useUpdateMobileTheme` (PUT, seeds the
  query cache with the server's response on success).
- `frontend/features/consumer-app/components/ThemeEditorSection.tsx` (new) — the editor +
  live preview, described below.
- `frontend/app/(dashboard)/consumer-app/design/page.tsx` — swapped `PlaceholderSection` for
  `ThemeEditorSection`; widened `maxWidth` 720→1100 for the two-column form+preview layout.
  Guard/shell logic (role check, loading/denied states) left untouched.
- `frontend/messages/{uk,en}.json` — added `Dashboard.consumerApp.themeEditor` (labels, hints,
  validation messages, the live-effect notice, preview copy). Ukrainian is the primary text;
  English mirrors it.
- `frontend/lib/api.ts` — one small, backward-compatible addition (see "lib/api.ts" below).

## Form: react-hook-form + zod, whitelist-exact

`ThemeEditorSection.tsx` uses `useForm` + `zodResolver`, following
`frontend/features/locations/components/LocationFormDialog.tsx`'s established
RHF+zod+inline-style pattern (this codebase's `consumer-app` feature itself predates that
convention — `BonusProgramSection`/`BannerForm` use plain `useState`+manual `validate()` — but
RHF+zod is a real, already-installed project convention used in `inventory/`, `locations/`,
`iot/`, `events/`, etc., and the task brief asked for it explicitly).

Every whitelisted field from TASK-536 is present, nothing else: `logoUrl` (text, optional,
absolute http(s) URL, ≤2048 chars — `new URL()` + scheme check, mirrors
`MobileThemeValidator.ValidateLogoUrl` exactly), 6 hex colors (`^#[0-9A-Fa-f]{6}$`),
`buttonRadius`/`cardRadius` (integer, in-bounds), `spacingPreset` (`<select>` limited to
`compact`/`comfortable`, matching `mobile/features/mobile-config/types.ts`'s own enum — the
gap TASK-536 closed).

## Live preview

`ThemePreview` (bottom of the same file) renders a small phone-shaped mock — logo/app-name
header, a sample card (surface color, card radius, primary/secondary text), a primary button
(primary color, button radius), a secondary pill (secondary color), and a 4-dot bottom-nav
mock — built from `watch()`'s current *unsaved* form values, so every keystroke/color-pick/
radius change/spacing switch updates it immediately. `spacingPreset` maps to a small
presentational-only padding/gap table (`compact` vs `comfortable`) so that field's effect is
visible too, not just described. No existing live-preview pattern existed elsewhere in this
feature directory to reuse (checked `BannerForm.tsx` — it has an image-upload preview only, no
live-form-value preview); this is a new, self-contained pattern scoped to this component.

Color fields (`ColorField` subcomponent) pair a native `<input type="color">` swatch with a
validated hex text input, kept in sync via `setValue(name, ..., { shouldValidate: true })` —
covers both "color picker" and "validated hex text input" from the brief in one control.

## Live-effect honesty notice

An inline notice (info icon + text) sits between the form fields and the Save button, stating
plainly that changes take effect immediately for every customer and that there is no preview
or publish step yet (`themeEditor.liveEffectNotice`). No "Publish" button or draft/live
language exists anywhere in the component — this reflects `MobileThemeService`'s documented
live-effect gap (TASK-536 log), not a hypothetical.

## Structured backend errors — `lib/api.ts` change

`MobileThemeController`'s 400 body is `{ errors: [{ field, message }] }`
(`MobileConfigValidationError[]`), a different shape than the rest of the API's flat
`{ error: string }` convention. `lib/api.ts`'s shared `apiFetch` only ever parsed
`body.error`, so a 400 from this endpoint would have silently collapsed to `"HTTP 400"` with
the per-field detail discarded before reaching any caller — the DoD's "surface the backend's
structured per-field error, not a generic failure message" would have been unmet no matter how
the feature code was written.

Fix: added one optional, additive constructor param to `ApiError` — `body?: unknown`, the raw
parsed JSON — populated in `apiFetch`'s existing error branch. No existing behavior changes
(message/status unchanged, `body` is unused by every other caller); confirmed via
`lib/api.test.ts`'s existing 15 tests, all still passing. This file is outside the file list
in the task brief's "files changed" scope, called out explicitly here — it was a genuine,
otherwise-unavoidable gap in shared infrastructure, not a scope choice.

`extractThemeValidationErrors(err)` in `api/mobileTheme.ts` reads `ApiError.body.errors`,
type-guards each entry, and returns the list (or `null` for anything else). In
`ThemeEditorSection.onValid`'s catch block, each returned `{field, message}` is mapped via
RHF's `setError(field, { type: "server", message })` — field names from the backend
(`primaryColor`, `buttonRadius`, `spacingPreset`, ...) match the form's field names exactly
(same camelCase DTO shape both sides). Any error whose `field` doesn't match a known form
field falls back to a generic banner (`formError` state, same pattern as `BannerForm.tsx`).

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` (`next lint`) — "No ESLint warnings or errors."
- `npx vitest run` — 48/48 tests passed (6 files), including `lib/api.test.ts` (15 tests,
  confirming the `ApiError.body` addition is backward-compatible).
- `git status` confirms the diff is scoped to: `types.ts`, `lib/api.ts`, `messages/{uk,en}.json`,
  the 3 new theme-editor files, and `design/page.tsx` — plus pre-existing uncommitted TASK-535
  work (`Sidebar.tsx`, `PlaceholderSection.tsx`, the other placeholder route directories) that
  predates this session and was left untouched.
- No device/browser smoke test was run (no dev server started this session); relied on
  type-check + lint + unit tests per the task's stated verification steps.

## Notes for the orchestrating session

- Did not touch `.claude/tasks/mobile-roadmap.md` — leaving TASK-537's status update to the
  orchestrator per the brief.
- `lib/api.ts`'s `ApiError.body` addition is generic, reusable infrastructure — any future
  endpoint returning a structured (non-`{error}`) error body can use it the same way.
