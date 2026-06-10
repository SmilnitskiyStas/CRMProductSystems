# TASK-033 — Provider Panel: Frontend

**Date:** 2026-06-06
**Agent:** frontend-developer
**Status:** done

## Files created

```
frontend/features/provider/
  types.ts                                — TenantSummaryDto, TenantDetailDto, ProviderHealthDto,
                                            ProviderLogDto, ImpersonateResponse, PLAN_COLORS, MODULE_LABELS
  api/provider.ts                         — providerApi: getTenants, getTenant, updatePlan, updateModules,
                                            impersonate, endImpersonate, getHealth, getLogs
  hooks/useProvider.ts                    — useTenants, useTenant, useProviderHealth, useProviderLogs,
                                            useUpdatePlan, useUpdateModules, useImpersonate
  components/TenantCard.tsx               — grid card: name, plan badge, stats, modules chips
  components/TenantDetailPanel.tsx        — right slide-in: info grid, plan editor, modules editor, impersonate
  components/ImpersonationBanner.tsx      — fixed top banner with "Вийти з перегляду" button
  components/ProviderLogsPanel.tsx        — cross-tenant activity log list

frontend/app/(dashboard)/provider/page.tsx — main page
```

## Files modified

- `lib/roles.ts` — added `PROVIDER_ONLY` set
- `components/layout/Sidebar.tsx` — added Provider nav item (Shield icon, PROVIDER_ONLY roles)

## Features implemented

- **Role guard**: `useEffect` redirects non-provider users to `/dashboard`
- **Stats row**: Total tenants, Active tenants, Total users, Expired batches (from `/api/provider/health`)
- **Tenants grid**: responsive cards with search by name/slug, click → right panel
- **Tenant detail panel**: info grid, plan selector (4 options), modules checkboxes (6 options), save buttons
- **Impersonation flow**:
  1. Click "Увійти як клієнт" → `POST /api/provider/tenants/:id/impersonate`
  2. Save original token in `sessionStorage.sg_provider_token`
  3. Swap main token via `setToken(resp.accessToken)`
  4. Show `ImpersonationBanner` at top (position: fixed, z-index: 100)
  5. "Вийти" → `DELETE /api/provider/tenants/:id/impersonate` + restore original token
- **Logs tab**: cross-tenant activity log, newest first, action color coding
- **Tabs**: "Клієнти (N)" | "Логи"

## TypeScript
`npx tsc --noEmit` → 0 errors
