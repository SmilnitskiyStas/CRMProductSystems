# TASK-549 — QR/deep-link onboarding (frontend half)

**Status:** done (frontend portion — backend half already done, see
`.claude/logs/tasks/549_2026-08-18_qr-onboarding-backend_backend-developer.md`)
**Agent:** frontend-developer
**Date:** 2026-08-18

## What was built

1. **Public web fallback page** — `frontend/app/[locale]/join/[slug]/page.tsx`, reachable at
   `https://<domain>/join/{slug}` (`uk` unprefixed, `en` as `/en/join/{slug}`), following the
   existing landing page's placement/locale pattern (`frontend/app/[locale]/page.tsx`).
   - Async server component. Fetches `GET /api/v1/retailers/{slug}/public` server-side via a new
     small helper, `frontend/features/join/api/retailers.ts` (`getRetailerPublicInfo`), which never
     throws — any non-OK response or network error collapses to `null`.
   - On success: retailer name/logo (or a neutral placeholder icon when `logoUrl` is `null`), an
     "Open in app" button linking to the proposed deep link `shelfguard://join/{slug}`, and an
     honest "Coming soon" state for Google Play / App Store instead of real or fake store badges
     (no listing exists yet — TASK-440 is still blocked on credentials/assets, confirmed by reading
     that entry in `.claude/tasks/mobile-roadmap.md` before writing this).
   - On 404/any failure: one generic "this link isn't valid" state — deliberately does not attempt
     to distinguish unknown-slug vs. inactive vs. module-missing vs. paused-program, matching the
     backend's own enumeration-safety design documented in its task log.
   - `export const dynamic = "force-dynamic"` — this page must never serve a cached answer about
     whether a retailer is currently joinable.
   - No auth of any kind — `[locale]/layout.tsx` (shared with the public landing) wraps it; no
     dashboard/auth guard applies to this path.

2. **Deep-link contract doc** — `docs/integration/deep-link-onboarding.md` (new; the existing
   `docs/integration/MOBILE_API_STAGE_*.md` files are a different, stage-numbered series for a
   different workstream, so this got its own descriptively-named file per the brief). Documents the
   web fallback URL shape, the proposed `shelfguard://join/{slug}` custom scheme plus iOS Universal
   Links / Android App Links on the same path (none implemented — explicitly flagged as owned by the
   mobile workstream), the 404/enumeration-safety rule the native side must also follow, and the
   full `Open app → resolve by slug → show retailer → join → set active` flow referencing
   `GET /api/v1/retailers/{slug}/public` (anonymous preview) and TASK-548's existing
   `POST /api/v1/retailers/{slug}/join` (auth-required actual join, unchanged).

## Routing fix required beyond the stated file list (flagging explicitly)

Reading the actual routing setup (`frontend/middleware.ts`, `frontend/i18n/routing.ts`) before
writing the page surfaced a load-bearing gap: `middleware.ts` only ran next-intl's locale
middleware for `pathname === "/"` or `pathname.startsWith("/en")` — hardcoded to the landing page,
the only route under `[locale]` at the time. `uk` (default locale) has no URL prefix, so an
unprefixed request like `/join/{slug}` needs next-intl's rewrite to reach
`app/[locale]/join/[slug]/page.tsx` at all; without it, Next's router has no route matching a bare
`/join/{slug}` (only `/uk/join/{slug}` internally) and the whole feature 404s for every default
(uk) visitor while only ever working via the `/en` prefix. Fixed by adding `/join` to the set of
intl-routed path prefixes in `middleware.ts`, with a comment explaining why the rewrite is
load-bearing. Also updated the doc comments in `frontend/i18n/routing.ts` and
`frontend/app/[locale]/layout.tsx` (no behavior change there — just correcting "landing-only" claims
that were no longer true) — the layout's `NextIntlClientProvider` still only forwards the `Landing`
namespace since the join page has no client components and reads its own inline copy dictionary
server-side, so no namespace change was needed there.

This means git status is not limited to exactly page.tsx + docs + fetch helper as originally
scoped — it also touches `frontend/middleware.ts` (substantive, necessary fix),
`frontend/i18n/routing.ts` and `frontend/app/[locale]/layout.tsx` (comment-only). No
`(dashboard)`/`(auth)` route or backend file was touched. Confirmed via
`git diff --stat -- frontend/middleware.ts frontend/i18n/routing.ts "frontend/app/[locale]/layout.tsx"`
— 3 files, 25 insertions/11 deletions, all doc-comment or the one middleware prefix-list change.

## Locale/messages decision

Did not add a `Join` namespace to `frontend/messages/{en,uk}.json` (both already have large,
unrelated pre-existing uncommitted diffs from other sessions per `git status` at task start — did
not touch them). Instead the page keeps a small inline `COPY = { uk: {...}, en: {...} }` dictionary
in `page.tsx` itself and resolves it from the route's own `locale` param. This avoids expanding
those already-dirty files and avoids needing to widen the `[locale]/layout.tsx` client-provider
message scope (which currently only forwards `Landing.*`) — the join page renders entirely
server-side, no `useTranslations` client hook needed.

## Files changed

- `frontend/app/[locale]/join/[slug]/page.tsx` — new page.
- `frontend/features/join/api/retailers.ts` — new fetch helper.
- `docs/integration/deep-link-onboarding.md` — new contract doc.
- `frontend/middleware.ts` — extended intl-routed path prefixes to include `/join` (functional fix,
  required for the route to resolve at the default locale).
- `frontend/i18n/routing.ts`, `frontend/app/[locale]/layout.tsx` — comment corrections only.

No `(dashboard)`/`(auth)` route or backend file touched.

## Verification actually performed this run

- `npx tsc --noEmit` — **PASS**, no errors.
- `npm run lint` (`next lint`) — **PASS**, "No ESLint warnings or errors" (0 introduced; used the
  same `// eslint-disable-next-line @next/next/no-img-element` convention already established in
  `frontend/features/consumer-app/components/ThemeEditorSection.tsx` for the one external
  tenant-logo `<img>`, rather than leaving a bare warning — kept the run at zero either way).
- **Live browser verification, genuinely performed this run** (Browser pane, `preview_start` for
  both `frontend-dev` on :3001 and `backend-dev` on :5000 against the real local dev Postgres —
  confirmed migrations already applied, `Now listening on: http://localhost:5000` in server logs):
  - `GET http://localhost:5000/api/v1/retailers/svizhy-kut/public` directly →
    `{"name":"Свіжий Кут","slug":"svizhy-kut","logoUrl":null,"joinable":true}` (found the real seeded
    tenant/slug by querying the dev Postgres container directly — `docker exec
    crmproductsystems-postgres-1 psql ...` — after the placeholder slug from the backend log's
    illustrative JSON example turned out not to exist in this dev DB).
  - `http://localhost:3001/join/svizhy-kut` (default uk, unprefixed) → renders "Приєднайтесь до
    Свіжий Кут", correct subheading, "Відкрити в застосунку" button with
    `href="shelfguard://join/svizhy-kut"` (confirmed via `read_page`), Google Play / App Store
    badges rendered as non-interactive "Скоро" chips (confirmed absent from the interactive-elements
    list), no console errors.
  - `http://localhost:3001/en/join/svizhy-kut` → same page in English ("Join Свіжий Кут").
  - `http://localhost:3001/join/test-slug` (unknown slug, `NEXT_LOCALE=uk` cookie set to pin the
    default locale) → confirmed via `location.href` the URL stayed unprefixed
    (`/join/test-slug`, not rewritten to `/uk/...` in the visible URL) while rendering the Ukrainian
    not-found state ("Це посилання недійсне") — this is the proof the `middleware.ts` fix above
    actually works, not just that it type-checks.
  - Without the `NEXT_LOCALE` cookie (default browser `Accept-Language`), the same unknown-slug URL
    redirected to `/en/join/test-slug` and rendered the English not-found state — confirms
    next-intl's locale negotiation now runs correctly for this route.
  - No console errors on any of the four loads (checked via `read_console_messages`,
    `onlyErrors: true`).
  - Both dev servers stopped after the pass (`preview_stop`).
- Did not test: a paused-program tenant, a tenant with a real `logoUrl` set, or the native deep
  link actually opening anything (no handler is registered anywhere — expected, out of scope here).

## Handoff / next

Mobile-side deep-link handler implementation (Universal/App Links, custom scheme registration) is
out of scope here per the brief and owned by the mobile workstream — `docs/integration/deep-link-onboarding.md`
is the contract for whoever picks that up next. Orchestrating session to mark TASK-549 `done` in
`.claude/tasks/mobile-roadmap.md` (not done here per instruction).
