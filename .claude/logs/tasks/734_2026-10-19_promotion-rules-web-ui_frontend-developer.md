# TASK-734 Promotion rules web UI (frontend-developer)

Built the "Акції та знижки" rule builder against the Phase 2 API contract (`.claude/docs/promotion-rules.md`).

- `frontend/features/promotion-rules/`: `types.ts`, `api/`, `hooks/`, `lib/{definition,messages}.ts` (pure helpers: wizard state <-> definition, step gating, code localization, summaries), `components/` (RulesList, RuleWizard 6 steps, ReviewStep with validation panel + /simulate panel, ItemPicker).
- Pages `app/(dashboard)/promotion-rules/{page,new/page,[id]/page}.tsx`; Sidebar entry in `sales` group (store_manager+, module pos); i18n `Dashboard.promotionRules` in uk.json/en.json (+ `nav.sales.promotionRules`).
- Autosave on step change (POST once, then PUT); versions history aside; banner when editing published/paused rule.
- Verified: `tsc --noEmit`, eslint, vitest (17 tests in lib/promotionRules.test.ts), `next build` all pass.
- UNVERIFIED: all API calls (backend not available); real UI not opened in a browser.
- Known limits: item chip names are not in the definition, so when editing a saved rule items show "Товар …abc123" until re-picked (categories are resolved by name). Loyalty tiers list needs tiers endpoint access (admin-only).
- Follow-up: store/network managers get scope.storeIds defaulted (header store, else first location), "Усі магазини" disabled with note; autosave/API errors show localized first error code; item chips resolved via GET lookups/items?ids= (fallback short id). tsc/eslint/vitest (19) green.
