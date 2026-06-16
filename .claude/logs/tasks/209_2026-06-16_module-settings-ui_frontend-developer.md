# TASK-209 — Frontend: Module activation settings UI

**Agent:** frontend-developer · **Date:** 2026-06-16 · **Status:** done (with one deliberately deferred scope item — see below)

## What was done

### New feature: `frontend/features/modules/`
- `types.ts` — `ModuleKey`, `ModulesSettings`, `ModuleMeta`, the full `ALL_MODULES` catalog (6 v4 modules with Ukrainian label + description), `BUSINESS_TYPE_LABELS` map.
- `api/modules.ts` — `modulesApi.get()` → `GET /api/settings/modules`.
- `hooks/useModules.ts` — `useModules()` React Query hook (`staleTime: 60_000`, `retry: false`, matches `usePrroSettings` conventions).

### New tab: «Модулі» in Settings
- `frontend/features/settings/components/ModulesTab.tsx` — renders business type + a card per module (active/inactive badge, toggle-style visual indicator, description). **Read-only** — see scoping note below.
- `frontend/app/(dashboard)/settings/page.tsx` — added the tab, gated to `enterprise_admin` only via a new `ENTERPRISE_ADMIN_ONLY` role set in `lib/roles.ts` (provider has no tenant_id outside impersonation, so the GET would 403 for them — hiding the tab avoids showing a broken one).

## Scoping decisions (read before extending this)

**1. The module list is read-only, not an interactive toggle**, despite the backlog wording ("Toggle-список активних модулів"). The backend (TASK-208) only exposes `GET /api/settings/modules` for enterprise_admin — activation/deactivation is `PATCH /api/admin/tenants/{id}/modules`, **ProviderOnly**. There is no backend capability for a tenant admin to flip their own modules. Building an interactive toggle here would either (a) silently do nothing on click, or (b) require adding a new tenant-self-service PATCH endpoint — out of scope for this task and a meaningful permission-model decision that should go through an ADR, not get bundled into a UI ticket. The toggle *visual* is rendered (pill-shaped indicator matching iOS-style switches) for the "looks like a toggle list" requirement, but it doesn't respond to clicks — it communicates current state, and there's an explanatory line: "Активація чи вимкнення модулів виконується провайдером платформи. Зверніться до підтримки...".

**2. Sidebar module-gating ("Sidebar-групи ховаються якщо модуль вимкнений") was NOT implemented.** Verified live against the demo tenant: `GET /api/settings/modules` for `ea@demo.local` (enterprise_admin, tenant "Свіжий Кут") returns:
```json
{"businessType":"retail","modules":["shelf_manager","crm","notifications"]}
```
This is the **legacy** module vocabulary (KI-012, flagged in TASK-208's log) — none of the v4 keys (`inventory`, `pos`, `procurement`, etc.) are present. If Sidebar groups were gated on these v4 keys today, this tenant (and presumably the production tenant, same seed data) would have its Каса/Склад/Продажі/Аналітика/Управління groups **all disappear** — a severe, immediate regression on a live system. Implementing this safely requires a backfill of existing tenants' `Modules` to the v4 vocabulary first (already documented as the resolution for KI-012). Doing this without that backfill, just to satisfy this task's literal acceptance line, would break production. I'm deferring it and recording the reasoning here instead of either skipping silently or breaking the site.

Recommended order for whoever picks this up: (a) write the KI-012 backfill (one-time script or migration calling `tenant.UpdateModules(Tenant.DefaultModulesForBusinessType(tenant.BusinessType))` for every existing tenant, merged with whatever legacy keys still matter), (b) only then wire `Sidebar.tsx`'s existing per-item `roles: Set<AppRole>` checks to also check `useModules()` data — this pairs naturally with TASK-210 (new v4 menu structure) since that task is already renaming/regrouping the sidebar from scratch.

## Verification
- `npx tsc --noEmit` → 0 errors
- `npm run build` → 27/27 pages succeeded
- Manually verified `GET /api/settings/modules` against the local API with a real enterprise_admin JWT (see businessType/modules payload above) — confirms the DTO shape (`businessType`, `modules: string[]`) matches what `ModulesTab.tsx`/`useModules.ts` expect.
- Settings page (`/settings`) returns 200 with the dev server running.

## Files changed
- `frontend/features/modules/types.ts` (new)
- `frontend/features/modules/api/modules.ts` (new)
- `frontend/features/modules/hooks/useModules.ts` (new)
- `frontend/features/settings/components/ModulesTab.tsx` (new)
- `frontend/app/(dashboard)/settings/page.tsx` (added tab + role gate)
- `frontend/lib/roles.ts` (added `ENTERPRISE_ADMIN_ONLY`)

## Next
TASK-210 — Frontend: New v4 menu structure. Should incorporate the KI-012 backfill + Sidebar module-gating deferred here.
