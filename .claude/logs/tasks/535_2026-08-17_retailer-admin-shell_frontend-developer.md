# TASK-535: Expand /consumer-app into full Retailer Admin shell

**Agent:** frontend-developer
**Date:** 2026-08-17
**Status:** done — tsc/lint clean. Navigation/routing scaffolding only, per brief; dev-server
live verification skipped (optional/bonus per brief, not required).

## Context

Stage C's first task on the multi-tenant consumer app-builder initiative. Stage B (backend:
`MobileConfiguration`/`MobileConfigurationVersion`/`MobileTheme`, `GET /api/v1/mobile/config`)
already shipped. This task only scaffolds five new sibling routes + sidebar entries under the
existing `/consumer-app` area — no builder logic. Real UIs land in TASK-536+ (Theme Editor
TASK-537, Page Builder TASK-541, Navigation Builder TASK-542, Feature Flags/Version History
Stage D).

## Done

- Five new routes, each an exact copy of the existing sibling page-shell pattern (role check via
  `useMe()` + `hasRole(me.role, AT_LEAST_ENTERPRISE_ADMIN)`, `AccessDenied` on deny, `return null`
  while `me` is loading to avoid a denied-then-granted flash, then `<h1>`/subtitle + section body):
  - `frontend/app/(dashboard)/consumer-app/design/page.tsx`
  - `frontend/app/(dashboard)/consumer-app/pages/page.tsx`
  - `frontend/app/(dashboard)/consumer-app/navigation/page.tsx`
  - `frontend/app/(dashboard)/consumer-app/features/page.tsx`
  - `frontend/app/(dashboard)/consumer-app/versions/page.tsx`
- New shared `frontend/features/consumer-app/components/PlaceholderSection.tsx` — the "not yet
  built" body for all five pages (icon + heading + body, dashed border). No existing generic
  `EmptyState` component was found in the codebase, so this is new; deliberately visually distinct
  from `AccessDenied.tsx` (dashed border vs. plain, "planned" not "forbidden") while reusing its
  color/typography language (`#E8EDF5` heading, `#4B5563` body, 40px icon `strokeWidth={1.5}`).
  Copy comes from one shared i18n key pair (`Dashboard.consumerApp.placeholder.{heading,body}`) —
  the per-page title/subtitle already carries the section-specific description, so the placeholder
  body itself stays generic across all five routes.
- `frontend/components/layout/Sidebar.tsx` — `consumer_app` group's `items` expanded from 4 to 9:
  added `/consumer-app/{design,pages,navigation,features,versions}`, same
  `roles: AT_LEAST_ENTERPRISE_ADMIN` as existing entries. New icons (all distinct from the four
  already in use — `Smartphone`, `Megaphone`, `TrendingUp`, `Package`): `Palette` (Design),
  `LayoutTemplate` (Pages), `Compass` (Navigation), `ToggleLeft` (Features), `History` (Versions).
- i18n: `Dashboard.sidebar.groups.consumerApp.{design,pages,navigation,features,versions}` (nav
  labels) and `Dashboard.consumerApp.{designPage,pagesPage,navigationPage,featuresPage,
  versionsPage}.{title,subtitle}` + `Dashboard.consumerApp.placeholder.{heading,body}` (page
  content), added to both `uk.json` and `en.json`.

## Verification

- `npx tsc --noEmit` — clean, no errors.
- `npx next lint` on all new/changed files — clean, no warnings.
- `node -e "JSON.parse(...)"` on both message files — valid JSON after edits.
- `git status --porcelain -- frontend/` confirms scope is exactly: `Sidebar.tsx`, `en.json`,
  `uk.json` modified; five new route dirs + `PlaceholderSection.tsx` added. No edits to the four
  existing page files (`page.tsx`, `banners/`, `promotions/`, `catalog/`) — diffed and confirmed.
- Runtime verification (how to do it, not required by brief): log in as `enterprise_admin`,
  confirm the "App" sidebar group shows 9 items, click each new one — expect the page's own
  title/subtitle plus the dashed placeholder box, no console errors. Log in as a role below
  `AT_LEAST_ENTERPRISE_ADMIN` (e.g. `store_manager`) and hit `/consumer-app/design` directly by
  URL — expect `AccessDenied`, not the placeholder or a crash.

## Files

- `frontend/app/(dashboard)/consumer-app/design/page.tsx` (new)
- `frontend/app/(dashboard)/consumer-app/pages/page.tsx` (new)
- `frontend/app/(dashboard)/consumer-app/navigation/page.tsx` (new)
- `frontend/app/(dashboard)/consumer-app/features/page.tsx` (new)
- `frontend/app/(dashboard)/consumer-app/versions/page.tsx` (new)
- `frontend/features/consumer-app/components/PlaceholderSection.tsx` (new, shared)
- `frontend/components/layout/Sidebar.tsx` (consumer_app group: 4 → 9 items, 5 new icon imports)
- `frontend/messages/uk.json`, `frontend/messages/en.json` (sidebar labels + page title/subtitle
  + shared placeholder copy)

## Not in scope (per brief)

- No Theme Editor, Page Builder, Navigation Builder, Feature Flags UI, or Version History logic —
  all deferred to TASK-536 onward.
- No backend changes.
- `.claude/tasks/mobile-roadmap.md` not updated — orchestrating session marks TASK-535 `done`.

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
