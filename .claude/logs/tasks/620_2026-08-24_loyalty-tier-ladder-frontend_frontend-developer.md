# TASK-620 — Loyalty tier ladder admin page (frontend)

**Status:** done · **Agent:** frontend-developer · **Date:** 2026-08-24

Plan: `goofy-bubbling-naur.md` §4 "Драбина рангів". Handoff read:
`.claude/logs/handoffs/615-to-frontend_backend-developer.md`.

## What was built

- Route `frontend/app/(dashboard)/consumer-app/loyalty-tiers/page.tsx` — gated
  `AT_LEAST_ENTERPRISE_ADMIN`, mirrors `/consumer-app/page.tsx`'s guard pattern exactly.
- `frontend/features/consumer-app/api/loyaltyTiers.ts` +
  `frontend/features/consumer-app/hooks/useLoyaltyTiers.ts` — 1:1 mirror of
  `loyaltySettings.ts`/`useLoyaltySettings.ts` (React Query key, `staleTime`, `retry: false`).
- `LoyaltyTierDefinitionDto`/`UpsertTierRequest` added to
  `frontend/features/consumer-app/types.ts`, matching the backend's `LoyaltyDtos.cs` records
  field-for-field (camelCase, decimal→number, Guid→string).
- `frontend/features/consumer-app/components/TierLadderSection.tsx` — editable, reorderable
  list of tiers (name, min composite score, accrual multiplier, discount %), built on
  react-hook-form + `useFieldArray` + `@dnd-kit/sortable`, following
  `NavigationBuilderSection.tsx`'s established pattern in this feature area. Add/remove rows,
  drag-to-reorder, client-side validation mirroring `LoyaltyService.UpsertTierLadderAsync`'s
  server rules. `sortOrder` is never a user-editable field — it's always derived from a row's
  0-based position at submit time, so the ladder always reloads in the order shown (GET orders
  by `sortOrder` ascending).
- Reorder-identity-shift warning: since the backend's PUT matches submitted rows to existing
  ones **by `sortOrder` value** (see the handoff), any drag — or an add/remove above an existing
  row — can silently reassign which saved tier record a row's edits land on.
  `hasIdentityShiftingReorder()` detects this (compares each persisted row's originally-loaded
  `sortOrder` to its final index) and, when true, routes Save through the existing
  `ConfirmDialog` component with an explanation before the PUT fires. A save with no such shift
  skips the dialog entirely.
- Unsaved-changes affordance: reused `useUnsavedChangesGuard` (already generic, no changes
  needed) for the `beforeunload` + in-app-link-click guard.
- Sidebar: one entry added to `frontend/components/layout/Sidebar.tsx` (`Award` icon, placed
  right after "Bonus Program", same `AT_LEAST_ENTERPRISE_ADMIN` gate) — file re-read immediately
  before editing per the TASK-621 collision warning; nothing else in the file touched.
- i18n: `tierLadder`, `tierLadderPage`, and `sidebar.groups.consumerApp.loyaltyTiers` keys added
  to both `frontend/messages/en.json` and `frontend/messages/uk.json`.

## Verification

- `npx tsc --noEmit` in `frontend/`: clean.
- `npm run lint` in `frontend/`: clean, no warnings.
- Manual browser verification: started `backend-dev` (port 5000) and `frontend-dev` (port 3001)
  from `.claude/launch.json` against the local dev Postgres
  (`crmproductsystems-postgres-1`:5435; `AddLoyaltyTierLadder` migration already applied).
  Logged in as seeded `ea@demo.local` (enterprise_admin):
  - Empty state ("No tiers configured yet") renders on first visit.
  - A `store_manager`-role session hitting the route gets `AccessDenied`, matching the
    enterprise_admin gate.
  - Added Bronze (min score 0, ×1.0, 0%) and Silver (min score 50, ×1.5, 5%) rows, saved — `PUT`
    persisted both with sequential `sortOrder` 0/1. Full page reload rehydrated both rows with
    the correct values.
  - Removed the first row (Bronze), forcing Silver's effective `sortOrder` to shift 1→0, then
    saved: the reorder-confirmation dialog appeared with the expected copy and blocked the
    request until confirmed. After confirming, the `PUT` response showed the surviving row had
    inherited the removed row's database `Id` — the exact identity-reassignment the dialog
    warns about, confirming both the detection logic and the copy are accurate.
  - No console errors traceable to this feature (some 401s appeared from an unrelated manual
    token-swap used to switch between test users during setup, not from the new code).
- Preview servers stopped after verification.

## Not implemented here (per plan §5, later waves)

`CustomerDetail.tsx` tier/progress tab, `/customer-support` inbox page, marketing-analytics tier
segmentation (TASK-621+). `mobile/` untouched — owned by a separate concurrent agent.
