# TASK-074 — SaaS Admin Panel UI

**Date:** 2026-06-14
**Agent:** frontend-developer
**Status:** done

## Summary

Built the `/admin` route and `frontend/features/admin/` feature directory — a Provider-only SaaS admin panel for managing tenants via the `/api/admin/*` endpoints.

## Files Created

### Feature directory
- `frontend/features/admin/types.ts` — `TenantDto`, `TenantUsage`, `CreateTenantRequest`, plan/module display helpers
- `frontend/features/admin/api/admin.ts` — `adminApi` with all 7 endpoint functions
- `frontend/features/admin/hooks/useAdmin.ts` — React Query hooks: `useTenants`, `useTenant`, `useCreateTenant`, `useUpdatePlan`, `useUpdateModules`, `useActivateTenant`, `useDeactivateTenant`
- `frontend/features/admin/components/TenantTable.tsx` — table with columns: Name/Slug, Plan badge, Modules count, Users/Stores/Products, Sales 30d, Status, Actions
- `frontend/features/admin/components/CreateTenantModal.tsx` — modal form with auto-slug generation from name, plan select, admin credentials
- `frontend/features/admin/components/TenantDetailDrawer.tsx` — sticky side drawer with usage stats cards, plan editor, modules checkboxes, activate/deactivate button

### Page
- `frontend/app/(dashboard)/admin/page.tsx` — Provider-only guard (redirect on non-provider role), header + "Новий тенант" button, stats row (total/active/plan breakdown), search, TenantTable, TenantDetailDrawer, CreateTenantModal

### Modified
- `frontend/components/layout/Sidebar.tsx` — added "Адмін" nav item (Settings icon, PROVIDER_ONLY)

## Verification

- `npx tsc --noEmit` — clean, no errors
- Role guard implemented via `useEffect` redirect + early return for non-provider users
- Dark theme consistent with `/pos` and `/provider` pages (bg #161B26 / #0D1117, border #1F2937)
- Plan badge colors: trial=yellow, basic=blue, standard=purple, enterprise=green
- Slug auto-generation: `name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')`
- Does not touch any backend files
