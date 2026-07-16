# Frontend Structure

**Owner:** frontend-developer
**Updated:** 2026-06-04
**Last reviewed:** 2026-07-16 (pre-launch audit) — KI-004 note and pages table below refreshed to reality.

## Feature Directory Structure
```
frontend/features/{domain}/
  types.ts           — TypeScript interfaces for domain
  api/
    {domain}.ts      — uses shared api from lib/api.ts (NOT a local apiFetch)
  hooks/
    use{Domain}.ts   — React Query hooks
  components/
    {Domain}Table.tsx
    {Domain}Form.tsx
```

## Shared API Client
**All feature API modules MUST import from `@/lib/api`.**
```ts
import { api } from "@/lib/api";
// then: api.get<T>("/api/..."), api.post<T>("/api/...", body)
```
`lib/api.ts` handles: Authorization header, 401 → refresh → retry, window.location redirect on session expiry.

> ✅ KI-004 resolved (2026-07-15): no local `apiFetch` remains anywhere in `frontend/` — every feature
> API module imports the shared `api` from `@/lib/api`. The 401→refresh→retry state machine is now unit-
> tested (`lib/api.test.ts`). Note KI-021 still open: the access token is mirrored into `localStorage`
> (survives reload) — an accepted-with-mitigations XSS blast-radius item, see `known-issues.md`.

## Naming Conventions
- Hooks: `useProducts`, `useCreateProduct`, `useDeleteProduct`
- API modules: `productsApi.getAll`, `productsApi.create`
- Query keys: `["products"] as const`, `["products", id]`
- Components: PascalCase, named exports

## State Rules
- **Server state**: React Query only — never duplicate in Zustand
- **UI state** (modal open, selected item): local `useState` in page component
- **Global UI state** (sidebar, theme): Zustand only

## Layout (dashboard)
- `app/(dashboard)/layout.tsx` — auth guard + Sidebar + TopBar shell
- Uses `mounted` state to prevent SSR hydration mismatch (localStorage not available server-side)
- `components/layout/Sidebar.tsx` — 240px sticky, full nav, logout
- `components/layout/TopBar.tsx` — store name, user avatar, notification bell

## Pages Implemented
The 2026-06-04 table below is a v1 snapshot. The app now has ~43 App-Router pages across ~35 feature
directories (POS, marketplace, ai-orders, ai-assistant, suppliers, customers, auto-service, production,
iot, schedules, users, integrations, service-desk, chat, provider, admin, settings, profile, events,
modules, plus a public marketing landing at `/`). `/notifications` and `/settings` (marked pending
below) are both implemented. Error boundaries (`app/error.tsx`, `app/global-error.tsx`) were added in
Block 13. For the current inventory see CLAUDE.md's frontend layout.

| Route | Status | Notes |
|---|---|---|
| / | ✅ done | Public SSG marketing landing + lead form (TASK-334) |
| /login | ✅ done | Auth form + 2FA step (TOTP/recovery); token in localStorage (KI-021) |
| /dashboard | ✅ done | Stats cards, attention table, quick actions, store map (KI-007/010 = placeholder data) |
| /inventory /stock /receipts /transfers /write-offs /analytics | ✅ done | Core v1 flows |
| /pos | ✅ done | Shifts, cash reconciliation (close-shift dialog) |
| /notifications /settings /profile | ✅ done | (were pending in the old snapshot) |
| /marketplace /suppliers /ai-orders /ai-assistant /service-desk /provider /admin ... | ✅ done | v2–v4 feature pages |
| /* (catch-all) | ✅ placeholder | "Сторінка в розробці" (KI-011, intended) |

## shadcn/ui Components
Install via: `npx shadcn@latest add {component}`
Never copy-paste shadcn source — always install through CLI.

## Key Patterns
- `"use client"` only on components that use hooks or browser events
- CSR for all authenticated dashboard views (no SSR needed, no SEO requirement)
- Forms: `react-hook-form` + `zod` validation
- Tables: custom inline-styled (consistent with dark theme) until a shadcn table component is adopted project-wide
