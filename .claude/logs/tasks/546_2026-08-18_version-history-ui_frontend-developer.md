# TASK-546 — Version History UI + rollback action

**Agent:** frontend-developer
**Status:** done
**Date:** 2026-08-18

## Scope delivered

Replaced the TASK-535 placeholder at `/consumer-app/versions` with the real Version History
screen, wired to TASK-545's backend (`GET .../versions`, `POST .../versions/{id}/rollback`) and
TASK-544's `POST /api/v1/mobile/config/publish` — the first in-app trigger for that endpoint
anywhere in Retailer Admin. Also added an unsaved-changes guard to the three draft-editing screens.

## Files

**New:**
- `frontend/features/consumer-app/api/mobileConfigVersions.ts` — `fetchMobileConfigVersions`,
  `rollbackMobileConfigVersion`, `publishMobileConfigDraft`. Reuses
  `extractDraftValidationErrors` from `mobileConfigDraft.ts` for the 400 case — both new endpoints
  return the byte-identical `{ errors: [{ field, message }] }` shape, no duplicate parser needed.
- `frontend/features/consumer-app/hooks/useMobileConfigVersions.ts` — `useMobileConfigVersions`
  (query), `usePublishMobileConfigDraft`/`useRollbackMobileConfigVersion` (mutations). Both
  mutations invalidate not just the versions list but also the draft and theme query caches on
  success — `MobileConfigPublishService.PublishVersionAsync` always clones a fresh draft forward
  and (on rollback) rewrites the live `MobileTheme` row, so those caches are stale the instant
  either call succeeds; without this, reopening `AppBuilderCanvas`/`ThemeEditorSection` right after
  would show pre-publish/pre-rollback content.
- `frontend/features/consumer-app/hooks/useUnsavedChangesGuard.ts` — shared hook, see "Unsaved
  changes coverage" below.
- `frontend/features/consumer-app/components/ConfirmDialog.tsx` — first confirmation-dialog
  component in this codebase's inline-styled feature areas (mirrors `Modal.tsx`/`ReasonModal.tsx`'s
  visual pattern; the shadcn `alert-dialog.tsx` primitive exists but nothing else here uses
  Tailwind/shadcn forms, so introducing that would be a second design language for one screen).
  Takes an optional `error` prop rendered inside the dialog itself (the overlay covers the page
  behind it, so a failure needs to surface inside the dialog, not on the page).
- `frontend/features/consumer-app/components/VersionHistorySection.tsx` — the screen itself: list
  of versions (status badge, version number, created/published timestamps, creator), "Publish
  draft" button, per-row "Rollback to this version" button (archived rows only — see below), both
  actions gated behind `ConfirmDialog`.

**Modified:**
- `frontend/app/(dashboard)/consumer-app/versions/page.tsx` — swapped `PlaceholderSection` for
  `VersionHistorySection`, widened `maxWidth` 720→860.
- `frontend/features/consumer-app/types.ts` — added `MobileConfigVersionStatus`,
  `MobileConfigVersionSummary`, `MobileConfigPublishedResult` mirroring the three backend DTOs.
- `frontend/features/consumer-app/components/{AppBuilderCanvas,ThemeEditorSection,
  NavigationBuilderSection}.tsx` — wired `useUnsavedChangesGuard`; updated each `draftNotice` to
  link to `/consumer-app/versions` (the nice-to-have the brief flagged as optional — did it since
  it was a one-line addition per screen); updated stale JSDoc that said "no publish UI exists yet".
- `frontend/messages/en.json` / `uk.json` — new `Dashboard.consumerApp.versions` namespace (28
  keys); `unsavedChangesWarning` + `goToVersionsLink` keys added to `appBuilder`/`themeEditor`/
  `navigationBuilder`; trimmed the now-stale "Publishing will be available in a future update" /
  "Publishing isn't available from this screen yet" clauses out of the three `draftNotice` strings
  since publishing exists now.

## Design decisions

- **Rollback button only on `archived` rows.** The backend rejects a rollback target equal to
  either the current published or current draft version (`CannotRollbackToCurrentVersion`) — the
  UI mirrors that by only rendering the action for `status === "archived"`, so the error case is
  structurally avoided rather than caught after a failed request.
- **Creator name resolution.** `MobileConfigVersionSummaryDto.CreatedBy` is a bare `Guid?` — no
  joined display name from the backend (adding one would be backend scope, out of bounds for this
  task). Resolved client-side against the tenant's existing `useUsers()` list (`GET /api/users`,
  already fetched elsewhere in Retailer Admin — no new backend surface). Degrades gracefully to an
  "Unknown" label if the id is null or doesn't match a current user (e.g. `useUsers()` itself
  errors, or the account was since removed) — never blocks the page.
- **Both Publish and Rollback share one `ConfirmDialog` instance,** driven by a small
  `{ type: "publish" } | { type: "rollback", version }` union — per the task brief, Rollback
  republishes immediately (same consequence class as Publish), so a second bespoke dialog wasn't
  justified.
- **Validation-error display** matches `AppBuilderCanvas.tsx`'s convention exactly: structured
  `{ field, message }` errors are joined into one readable line and shown inside the dialog; any
  other thrown error falls back to its message text.

## Unsaved-changes coverage (read before trusting this as "full navigation blocking")

Next.js App Router has no first-class in-app route-block API — no `useBlocker`/`usePrompt`
equivalent exists (checked; confirmed nothing like the old Pages Router's `router.events` survives
in App Router). `useUnsavedChangesGuard(dirty, message)` covers two of the ways a user can leave a
dirty screen, not all of them:

1. **Tab close / refresh / typed-URL navigation** — native `beforeunload`, always reliable. The
   browser owns the confirmation text in every modern browser; the `message` argument is unused by
   `beforeunload` itself (kept for the second case below) since no browser has honored a custom
   `beforeunload` string in years.
2. **In-app navigation via clicking an `<a>` element** — every in-app link in this codebase
   (Sidebar.tsx included) renders through `next/link`, a real `<a>` with navigation wired to a
   normal bubble-phase `onClick`. A capture-phase `document` click listener runs before that (capture
   fires top-down before the target's bubble-phase handlers), so `preventDefault`+`stopPropagation`
   there reliably blocks the navigation with a `window.confirm()` gate — this covers Sidebar clicks
   too, not just clicks inside the editor component's own subtree, since it's a document-level
   listener, not scoped to a wrapping layout.

**NOT covered, and not silently missing — documented in the hook's own JSDoc:** browser
Back/Forward (`popstate`), and any programmatic `router.push()` call that isn't the result of an
`<a>` click. Neither exists inside these three screens today (no such call exists in
`AppBuilderCanvas`/`ThemeEditorSection`/`NavigationBuilderSection`), but a future one would bypass
this guard silently unless someone remembers to route it through the same hook or a new mechanism.

Wired into all three screens using each screen's existing dirty-tracking state — `dirty` (a plain
boolean, `AppBuilderCanvas`) or `formState.isDirty` (react-hook-form, `ThemeEditorSection`/
`NavigationBuilderSection`) — no new dirty-tracking was introduced; the hook just consumes what
each screen's Save-button-disabled logic already computes.

## Verification

- `node -e "JSON.parse(...)"` on both message files — valid JSON.
- `npx tsc --noEmit` (frontend) — PASS, no output.
- `npx next lint` (frontend) — "No ESLint warnings or errors".
- `git status` scoped to `frontend/features/consumer-app`, `frontend/app/(dashboard)/consumer-app`,
  `frontend/messages` — matches the expected file set (new files + the three editor screens +
  `types.ts`/message files modified); no unrelated files touched.
- Started the `frontend-dev` preview server and loaded `http://localhost:3001/consumer-app/versions`
  directly (no backend running, no auth session). Dev-server log confirms the route compiled clean
  (`✓ Compiled /consumer-app/versions in 22.6s (933 modules)`, `GET /consumer-app/versions 200`) —
  no React render/hydration error from any new file. The only console errors were
  `ERR_CONNECTION_REFUSED` (expected — backend wasn't started) and a pre-existing app-wide
  `IntlError: ENVIRONMENT_FALLBACK` (`timeZone` not configured) that fires from `(dashboard)/layout.tsx`'s
  `Loading` component, unrelated to this change. Without a backend/auth session the app redirected
  through `/dashboard` → `/en` as expected — never reached the role-gated content, so this is
  **not** authenticated E2E verification.
- **No live/authenticated browser verification was performed this run.** The Publish/Rollback
  flow, the history list's actual data rendering, the unsaved-changes guard's real click/beforeunload
  behavior, and the confirm-dialog UX have not been exercised against a running app with a real
  auth session and backend in this session — only the compile-time check above and the static
  `tsc`/`next lint`/JSON-parse checks.

## Next

None from this task. Suggest a future task (or a follow-up manual QA pass) actually exercises
Publish/Rollback end-to-end against a live tenant, since this session only verified the code
compiles and lints — not that the flow behaves correctly at runtime.
