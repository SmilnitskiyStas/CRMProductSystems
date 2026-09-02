# TASK-674 — Provider-controlled `mobile_app` + `analytics` modules

**Status:** review (not pushed) · **Agent:** main session · **Plan:** `peaceful-chasing-piglet.md` · **ADR-037**

## Problem
"Застосунок" (`consumer_app` NavGroup) and "Аналітика" (`analytics` NavGroup) rendered for every
tenant regardless of provider grants — module gating (ADR-015) was never wired to them. `loyalty`
key existed but only gated POS accrual; no `analytics` key existed.

## Decisions (user-confirmed)
- New keys `mobile_app` (whole "Застосунок" section) + `analytics` (reports section). `loyalty`
  untouched (POS only).
- **No backfill.** Existing tenants lose both sections on deploy until provider re-grants per
  tenant. → no DB migration.
- Not added to `DefaultModulesForBusinessType` (default-off for new tenants).

## Changes

### Backend
- `Tenant.UpdateModules` allow-list: `+ "mobile_app", "analytics"`.
- `[RequireModule("mobile_app")]` class-level: `LoyaltySettingsController`,
  `LoyaltyTierSettingsController`, `BannersController`, `DiscountsController`,
  `PromotionCampaignsController`, `MobileCatalogSettingsController`, `MobileBlocksController`,
  `MobileConfigDraftController`, `MobileConfigVersionsController`, `MobileConfigPublishController`,
  `MobileConfigPreviewController`, `MobileThemeController`.
- `[RequireModule("mobile_app")]` per-action: `NotificationsController` — the 4 `customer-messages`
  endpoints only.
- **Not gated:** `MobileConfigController` (`[AllowAnonymous]`, serves published config to the
  shopper app), `MobileAuthController`, all `api/consumer/*`.
- `[RequireModule("analytics")]` per-action on `AnalyticsController` — 14 of 17 actions. Ungated:
  `expiry-summary/compare` + `dashboard/weekly-kpi` (dashboard home), `pos/products/{id}/trend`
  (Events calendar).

### Frontend
- `features/modules/types.ts` — `ModuleKey` + `ALL_MODULE_KEYS` `+ loyalty, mobile_app, analytics`
  (also closed the pre-existing `loyalty` gap in the tenant Settings→Модулі tab).
- `features/provider/types.ts` (`TenantModule` + `ALL_MODULES`), `features/admin/types.ts`
  (`ALL_MODULES`) — `+ mobile_app, analytics` (become provider/admin checkboxes automatically).
- `Sidebar.tsx` — `moduleKey: "analytics"` on `analytics` group, `moduleKey: "mobile_app"` on
  `consumer_app` group; moved `/consumer-app/analytics` nav item into `consumer_app`.
- New `features/modules/components/ModuleGate.tsx` (extracted from `marketing-analytics/page.tsx`
  inline pattern; provider bypass, loading→children).
- New `app/(dashboard)/consumer-app/layout.tsx` + `app/(dashboard)/analytics/layout.tsx` — one
  `ModuleGate` per subtree.
- i18n `uk.json` + `en.json`: `Dashboard.modules.catalog.{loyalty,mobile_app,analytics}`,
  `Dashboard.modules.gate.{title,body}`, `Dashboard.provider.modules.*` +
  `.moduleDescriptions.*`, `Dashboard.admin.modules.*`.

### Docs
- ADR-037 added (`decisions.md` index + full text). KI-019 annotated as partially addressed.

## Verification
- Backend: `dotnet build` 0 err/0 warn. `dotnet test --filter RequireModule|Analytics` — **309/309**.
- Frontend: `tsc --noEmit` clean. `next build` — all routes incl. 2 new layouts compiled;
  `/consumer-app/analytics` builds. uk/en JSON valid.
- **Not** run: full browser E2E (needs a running stack + a tenant without the modules + provider
  login). Manual checklist in the plan's Verification section.

## Rollout note (breaking)
Every existing tenant loses "Застосунок" + "Аналітика" on deploy. Provider must re-grant per
tenant (TenantDetailPanel checkboxes) immediately after. Compile the list of paying tenants first.

## Follow-ups
- openapi.json regen (already pending).
- Shopper app (`api/consumer/*`) still serves when `mobile_app` revoked — separate task.
- `DiscountsController` gated under `mobile_app` — revisit if discounts become general pricing.
