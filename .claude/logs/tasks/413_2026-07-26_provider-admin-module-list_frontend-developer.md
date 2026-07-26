# TASK-413: Add "loyalty"/"marketing_analytics" to provider/admin module lists

**Agent:** frontend-developer
**Date:** 2026-07-26
**Status:** done

## Контекст

Follow-up flagged in `.claude/logs/tasks/409_2026-07-26_marketing-analytics-frontend_frontend-developer.md`:
`frontend/features/provider/types.ts` and `frontend/features/admin/types.ts` each own a separate
`ALL_MODULES` list (used by the provider/admin panel's tenant module-activation checkboxes), and
neither included `"loyalty"` (TASK-405) or `"marketing_analytics"` (TASK-406) — so a provider had
no UI way to enable either module for a tenant, only a direct DB write.

## Зроблено

- **`frontend/features/provider/types.ts`** — added `"loyalty" | "marketing_analytics"` to the
  `TenantModule` union and to the `ALL_MODULES` array (used by both `TenantDetailPanel.tsx`'s
  edit-modules checklist and `CreateTenantWizard.tsx`'s step-3 modules picker). Did not add either
  key to `BUSINESS_TYPE_PRESETS` — deliberate, matches how `marketplace_supplier` is the only
  preset-gated module; loyalty/analytics are optional add-ons for any business type, freely
  toggleable in the wizard regardless of preset.
- **`frontend/features/admin/types.ts`** — added both keys to its own `ALL_MODULES` const (used by
  `TenantDetailDrawer.tsx`'s edit-modules checklist). Admin has no create-time module picker (its
  `CreateTenantRequest` has no `modules` field — business type alone decides the default set
  server-side), so only the detail-drawer edit path needed the keys.
- **i18n** (`frontend/messages/en.json` + `uk.json`): added `loyalty`/`marketing_analytics` to
  `Dashboard.provider.modules`, `Dashboard.provider.moduleDescriptions`, and
  `Dashboard.admin.modules` (admin has no per-module description map — only labels are rendered
  there). Reused the exact English copy TASK-409 already shipped for `marketing_analytics` in
  `Dashboard.modules.catalog` (label "Marketing Analytics" / description "RFM customer
  segmentation, top products, cross-sell, AI-assisted recommendations"); wrote new copy for
  `loyalty` ("Loyalty Program" / "Customer bonus program: QR membership card, point accrual and
  redemption at POS", uk "Програма лояльності" / "Бонусна програма для клієнтів: QR-картка
  учасника, нарахування і списання балів на касі").

## Знайдено і виправлено (поза початковим скоупом, але блокувало верифікацію)

While live-verifying the admin panel's "Save" flow for modules, found the checkbox toggle
genuinely did nothing: `PUT /api/admin/tenants/{id}/modules → 405 Method Not Allowed`.
Root cause: `frontend/features/admin/api/admin.ts`'s `updatePlan`/`updateModules` both call
`api.put(...)`, but `AdminController.cs` declares both endpoints `[HttpPatch(...)]` (confirmed:
`ProviderController.cs`'s sibling endpoints really are `[HttpPut]`, so `provider.ts`'s `api.put`
calls are correct — `admin.ts` looks like a copy-paste from `provider.ts` that never got the verb
corrected, even though its own doc-comments already said "PATCH"). Net effect: **every admin-panel
"change plan"/"change modules" save has silently 405'd since TASK-074** (the admin panel's original
implementation) — not specific to the two new modules. Fixed both call sites to `api.patch`
(helper already existed in `lib/api.ts`, no new plumbing needed). Confined to this one file, no
backend/schema change.

## Не зроблено (навмисно, за скоупом брифу)

- `frontend/features/modules/types.ts` (`ALL_MODULE_KEYS`) + `Dashboard.modules.catalog` — the
  tenant-facing **read-only** Settings > Modules tab (`ModulesTab.tsx`, explicitly "activation is
  provider-managed") already has `"marketing_analytics"` (TASK-409) but is still missing
  `"loyalty"`. Different list, different (read-only, tenant-side) surface than what this task's
  brief described (provider/admin *activation* UI) — flagged as a separate follow-up via
  `spawn_task` (chip `task_cc5b2371`) rather than expanded into here.
- Dismissed the stale `task_22a39ac1` chip ("Wire marketing_analytics into provider
  module-activation UI") — superseded by this task.

## Верифікація

- `npx tsc --noEmit` — clean, 0 errors (both before and after the admin.ts bugfix).
- `npm run build` — exit 0, no new warnings, route table unchanged.
- **Live browser verification** (dev stack: `dotnet run --project backend/ShelfGuard.Api` +
  `npm run dev`, both via `preview_start`; temporary `Cors:Origins` addition in
  `appsettings.Development.json` since port 3000 was occupied by another dev server on this
  machine — reverted before finishing, confirmed via `git diff` showing no residual change).
  Logged in as the seeded provider account (`admin@shelfguard.local`), tenant "Свіжий Кут"
  (`8abfbbb5-3190-4de9-9f91-f4de59101bca`):
  - **Provider panel** (`TenantDetailPanel.tsx`): opened "Configure" under Modules — both
    "Loyalty Program" and "Marketing Analytics" appear as checkboxes alongside the 7 existing
    modules. Checked "Loyalty Program", clicked Save → real `PUT /api/provider/tenants/{id}/modules
    → 204`. Hard-reloaded the page — module persisted server-side (tenant card badge count went
    from "+1" to "+2"). Reverted (unchecked, saved again → second real `204`), hard-reloaded,
    confirmed back to original state.
  - **Provider panel, create-tenant wizard** (`CreateTenantWizard.tsx` step 3): both new modules
    render with label + description text correctly, in both English and Ukrainian (switched
    locale live via the app's own `sg_locale` cookie + `sg-locale-changed` event, no reload
    needed). Closed the wizard without submitting — client count unchanged (16 before/after).
  - **Admin panel** (`TenantDetailDrawer.tsx`): same "Configure" checklist shows both new modules
    (no "Supplier cabinet" row here, matching admin's pre-existing narrower list, unchanged).
    First save attempt hit the pre-existing 405 (see above); after the `admin.ts` fix, retried:
    checked "Loyalty Program", Save → real `PATCH /api/admin/tenants/{id}/modules → 200`,
    hard-reload confirmed persisted (module count 5→6 in the tenant table), then reverted
    (`PATCH → 200` again), hard-reload confirmed back to 5. Also confirmed Ukrainian labels
    render correctly ("Програма лояльності", "Маркетингова аналітика").
  - No console errors observed during any of the above; all mutations went through the app's real
    React Query hooks (`useUpdateModules`), not simulated.
- Cleanup: reverted the temporary CORS config addition (confirmed 0 diff via
  `git diff --stat`), stopped both preview servers, no test tenant left behind (wizard was
  cancelled, not submitted), the one real tenant touched ("Свіжий Кут") round-tripped back to its
  original 5-module state on both the provider and admin save paths.

## Файли

- `frontend/features/provider/types.ts`
- `frontend/features/admin/types.ts`
- `frontend/features/admin/api/admin.ts` (bugfix, see above)
- `frontend/messages/en.json`
- `frontend/messages/uk.json`

Not committed (repo convention — main session/user commits).
