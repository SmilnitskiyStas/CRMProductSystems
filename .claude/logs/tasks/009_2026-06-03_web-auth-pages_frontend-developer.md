# TASK-009: Web auth pages

**Date:** 2026-06-03
**Agent:** frontend-developer
**Status:** done
**Duration:** 1 session

## What was done

Implemented full web authentication: login page, JWT storage, auth hooks, dashboard guard, edge middleware.
Design matches prototype exactly: dark theme `#0F1117` background, `#161B27` card, `#2D7DD2` accent, Inter font.

## Files changed

**New — Auth feature:**
- `features/auth/types.ts` — AuthUserDto, LoginRequest, LoginResponse
- `features/auth/store.ts` — user in localStorage + module cache
- `features/auth/api/auth.ts` — login, refresh, logout, getMe
- `features/auth/hooks/useAuth.ts` — useMe, useLogin, useLogout (React Query)
- `features/auth/components/LoginForm.tsx` — form with zod + react-hook-form

**New — Pages:**
- `app/(auth)/layout.tsx` — centered full-screen dark layout
- `app/(auth)/login/page.tsx` — login page with ShelfGuard logo, card, form
- `app/(dashboard)/layout.tsx` — client-side auth guard (redirects on error)
- `app/(dashboard)/dashboard/page.tsx` — placeholder dashboard (TASK-010)
- `app/page.tsx` — root redirect to /dashboard

**New — Infrastructure:**
- `lib/api.ts` — shared fetch client: Authorization header injection, 401 → refresh → retry, token storage helpers
- `middleware.ts` — Next.js Edge middleware: checks refreshToken cookie, redirects unauthenticated to /login

**Modified:**
- `app/globals.css` — dark-first CSS variables matching prototype design tokens + Inter/JetBrains Mono fonts + scrollbar styling

## Design decisions

- `lib/api.ts` owns token storage (localStorage + in-memory) — all API calls go through it
- Access token in localStorage (not HttpOnly) — frontend reads it; refresh token in HttpOnly cookie (only backend reads it)
- 401 on any request: auto-refresh once → retry → if refresh fails: clear token, redirect to /login
- `middleware.ts` checks `refreshToken` cookie as lightweight edge guard; real validation on /auth/me
- Login page uses inline styles matching exact prototype hex values (not CSS vars) for pixel accuracy
- CSS vars updated to dark theme matching prototype — shadcn components now render correctly in dark mode

## Tests

- TypeScript: `tsc --noEmit` → 0 errors
- Visual: pixel-matches prototype dark theme

## Notes for next agent

TASK-010 (frontend-developer): Web dashboard (store overview)
- Auth is fully wired: use `useMe()` hook to get current user in any component
- To protect a new page: just put it inside `app/(dashboard)/` — the layout handles the guard
- API client: import `api` from `@/lib/api` for all HTTP calls
- Auth user: import `useMe` from `@/features/auth/hooks/useAuth`
- Dashboard should display: 4 metric cards (safe/warning/critical/expired), attention table, quick actions panel
- Needs backend stock/analytics endpoints (not yet implemented) — use mock data for now
