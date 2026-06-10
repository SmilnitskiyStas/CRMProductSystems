# Frontend Structure

**Owner:** frontend-developer
**Updated:** 2026-06-04

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

> ⚠️ KI-004: `features/inventory/api/products.ts` and `features/dashboard/api/dashboard.ts` currently use local `apiFetch` — this is a known bug to be fixed.

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
| Route | Status | Notes |
|---|---|---|
| /login | ✅ done | Auth form, JWT stored in localStorage |
| /dashboard | ✅ done | Stats cards, attention table, quick actions, store map |
| /inventory | ✅ done | POC product catalog CRUD |
| /stock | ✅ done | dense table, filters, add batch modal |
| /receipts | ✅ done | list with status tabs |
| /receipts/:id | ✅ done | pre-populated workflow, progress bar |
| /transfers | ✅ done | list with confirm/cancel actions |
| /write-offs | ✅ done | approve/reject, pending counter badge |
| /analytics | ✅ done | expiry summary, write-offs, by-zone, by-category, losses |
| /notifications | 🕐 pending | TASK-024 (API not built) |
| /settings | 🕐 pending | TASK-023 (Users API not built) |
| /* (catch-all) | ✅ placeholder | "Сторінка в розробці" |

## shadcn/ui Components
Install via: `npx shadcn@latest add {component}`
Never copy-paste shadcn source — always install through CLI.

## Key Patterns
- `"use client"` only on components that use hooks or browser events
- CSR for all authenticated dashboard views (no SSR needed, no SEO requirement)
- Forms: `react-hook-form` + `zod` validation
- Tables: custom inline-styled (consistent with dark theme) until a shadcn table component is adopted project-wide
