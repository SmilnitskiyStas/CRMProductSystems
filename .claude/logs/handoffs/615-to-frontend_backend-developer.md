# Handoff: TASK-615 backend-developer → frontend-developer

Plan: `C:\Users\stass\.claude\plans\goofy-bubbling-naur.md` (§4 "Драбина рангів" is this
wave's brief). Task log: `.claude/logs/tasks/615_2026-08-24_loyalty-tier-ladder-pos-integration_backend-developer.md`.

## What's ready

**Admin tier ladder CRUD** — `api/settings/loyalty/tiers` (`AppPolicies.AtLeastEnterpriseAdmin`,
same tier as `api/settings/loyalty`):
- `GET` → `LoyaltyTierDefinitionDto[]` (`Id`, `Name`, `SortOrder`, `MinCompositeScore`,
  `AccrualMultiplier`, `DiscountPercent`), ordered by `SortOrder`, empty array (not null) when
  the tenant has no ladder yet.
- `PUT` body `UpsertTierRequest[]` (`Name`, `SortOrder`, `MinCompositeScore`,
  `AccrualMultiplier`, `DiscountPercent` — **no `Id`**, it's a bulk replace of the whole ladder
  keyed by `SortOrder`; see the controller/service doc comments for why). Returns the updated
  ladder or `400 { error }` on validation failure (duplicate `SortOrder`, empty `Name`,
  `AccrualMultiplier` outside [0, 999.99], `DiscountPercent` outside [0, 100]).

**Consumer-facing** (spec only — mobile is out of scope for this repo's `frontend/`, but the
shape is here in case a web wallet view ever needs it): `GET
api/consumer/loyalty/{tenantId}/tiers` → `LoyaltyTierProgressDto`, `GET
api/consumer/loyalty/{tenantId}/tiers/history` → paged `LoyaltyTierChangeHistoryDto`.

## What's NOT done (this wave's job, per plan §4/§7)

New admin page-section for the tier ladder — plan suggests route
`consumer-app/loyalty-tiers` (precedent: `frontend/app/(dashboard)/consumer-app/page.tsx`),
gated `AT_LEAST_ENTERPRISE_ADMIN`, with a form to add/edit/reorder rungs
(name/threshold/multiplier/discount). Note the bulk-replace-by-SortOrder contract above when
wiring the save action — reordering existing rows (not just editing their values) is a
delete+insert on the backend, which is fine functionally but worth knowing if you want to warn
the admin before a reorder.

`CustomerDetail.tsx` tier/progress tab, `/customer-support` inbox page, and marketing-analytics
tier segmentation are later waves per plan §5 (steps 5, 8) — not this handoff's concern yet.
