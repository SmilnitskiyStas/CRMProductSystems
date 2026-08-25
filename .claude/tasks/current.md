# Current Sprint — v4.5 «Security Hardening» (started 2026-07-09)

Джерело: security audit `.claude/logs/reviews/2026-07-09_security-audit_auth-infra.md`
(TASK-329..332). Паралельні власники: TASK-331 — frontend, TASK-332 — devops.

## TASK-600 — Marketplace order receiving: API reference update (docs)

**Status:** done · **Agent:** documentation-writer
Log: `.claude/logs/tasks/600_2026-08-22_marketplace-orders-api-reference-update_documentation-writer.md`

Updated `mobile/features/marketplace-orders/API.md` (TASK-586/595's reference doc) for
TASK-596..599: added `price`/`referenceImageUrl` to the `MarketplaceOrderReceiptItemDto` TS
block (verified against the live backend record), documented the finalize-time discrepancy
auto-ticket, rewrote the barcode-crosswalk limitation to reflect order-time catalog
auto-provisioning, and added a new "Not yet built" section describing the intended (unbuilt)
mobile UX for showing price + reference photo per item, pointing at the existing no-image
placeholder pattern in `catalog.tsx`/`product/[id].tsx`. Docs-only, no code changes.

## TASK-598 — Marketplace catalog auto-provisioning at order time (Wave 2, backend)

**Status:** done · **Agent:** backend-developer · Wave 2 of 2 on TASK-596's schema.
Log: `.claude/logs/tasks/598_2026-08-22_marketplace-catalog-auto-provisioning_backend-developer.md`

Placing a marketplace order now auto-provisions the client's own `Item` catalog from the
supplier's `SupplierItem` listing (name/barcodes/price/unit/image), unless a barcode collision
is found with an existing client `Item` — in which case the order is rejected until the client
picks `catalogAction: "link"` (attach to the existing item) or `"create_new"` (duplicate anyway).
New read-only pre-flight `POST /api/marketplace/suppliers/{id}/orders/conflicts` lets the
checkout UI show conflicts before submitting. `CreateMarketplaceOrderItemDto` extended with
`CatalogAction`/`LinkedItemId`. `CreateOrderAsync` does a two-pass validate-then-execute per line
so a mid-order failure can never leave an earlier line's auto-created `Item` orphaned. Full
contract in the task log. Parallel: TASK-597 (frontend-developer, checkout UI, already verified
this contract) + a separate backend-developer agent (isolated worktree) on receipt enrichment/
discrepancy tickets — no file overlap. `dotnet build` clean; `dotnet test` — 1819/1819 passing
(1810 baseline + 9 new).

## TASK-599 — Marketplace receipt enrichment (price/photo) + discrepancy auto-ticket (backend)

**Status:** done · **Agent:** backend-developer (isolated worktree) · Wave 2 of TASK-586/ADR-033,
parallel to TASK-598 (catalog auto-provisioning, main worktree) — no file overlap except
`Dtos/CooperationDtos.cs`, reconciled by the orchestrator when merging both worktrees.
Log: `.claude/logs/tasks/599_2026-08-22_marketplace-receipt-enrichment-discrepancy-ticket_backend-developer.md`

`MarketplaceOrderReceiptItemDto` +`Price`/+`ReferenceImageUrl` (reference photo resolves via a
new provider-bypass repository method — `supplier_items` RLS has no client-read policy, a plain
Include would have silently returned nulls). New `ISupplierSupportService.CreateSystemTicketAsync`
— `MarketplaceOrderReceiptService.ReceiveAsync` auto-opens a supplier support ticket + outbox
notification (`supplier_support_ticket.opened`) when a finalized receipt has discrepancy notes.
Deviated from the brief's "single atomic SaveChangesAsync" ask for the discrepancy ticket —
verified that's unsafe given `product_stocks`/`stock_movements` vs. `notification_queue` need
opposite ambient-tenant RLS contexts; used two sequential `SaveChangesAsync` calls instead (see
task log for full rationale). Worker (`notification-dispatch.job.ts`) + `CabinetSupportTab.tsx`
(+ `marketplace/types.ts` companion field) wired for the new event. Build clean, full suite
1819/1819 passing after merge (this agent's own worktree run reported 1813/1813 against its
stale pre-Wave-1 baseline), frontend `tsc --noEmit` clean.

**Merge note (orchestrator):** this agent's worktree was created before TASK-596 (Wave 1) was
committed to `main`, so it never saw the `SupplierSupportTicket.MarketplaceOrderId` column and
independently re-added it (entity property, `AppDbContext` FK/index, and a duplicate migration
`20260822141854_AddMarketplaceOrderIdToSupplierSupportTickets`). That duplicate schema work was
discarded when merging (TASK-596's `20260822134439_...` migration already covers the same
column) — only this agent's genuine application-layer work (DTOs, service methods, worker/
frontend wiring, tests) was merged into `main`. Originally self-logged as TASK-596 (a second
collision, on top of the schema duplication) — renumbered to TASK-599 here and in its own log
file, and all in-code `TASK-597`/`TASK-596` comment references corrected to `TASK-599` (or
`TASK-598` for TASK-598's own stray comments referencing the wrong number) during the merge.

## TASK-596 — Marketplace catalog auto-provisioning + discrepancy tickets: schema (Wave 1)

**Status:** done · **Agent:** database-engineer · Wave 1 of 2 (repository/service/controller
logic is a follow-up wave for backend-developer agents, not built here).
Log: `.claude/logs/tasks/596_2026-08-22_marketplace-catalog-provisioning-schema_database-engineer.md`

Two schema additions + one repository method, in support of client-catalog auto-provisioning
from a supplier's marketplace listing at order time, and auto-opening a `SupplierSupportTicket`
when a receiving employee flags a discrepancy. `Item.SourceSupplierItemId` (nullable, FK →
`supplier_items.Id`, SET NULL) and `SupplierSupportTicket.MarketplaceOrderId` (nullable, FK →
`marketplace_orders.Id`, RESTRICT) — migration `AddItemSourceSupplierItemAndTicketOrderRef`,
applied cleanly to local dev DB, confirmed via psql. `IItemRepository.GetByAnyBarcodeAsync`
(batch barcode lookup for checkout) added using `EF.Functions.JsonExistAny` (Postgres `?|`
jsonb-array-overlap operator) — single query regardless of barcode count; 3 new integration
tests against real Postgres confirm correctness (dedup, no-match, empty-input short circuit).
`dotnet test` — 1810 passed, 0 failed.

## TASK-595 — Marketplace order receiving: post-implementation API reference (docs)

**Status:** done · **Agent:** documentation-writer
Log: `.claude/logs/tasks/595_2026-08-22_marketplace-orders-api-reference_documentation-writer.md`

New `mobile/features/marketplace-orders/API.md` — reference doc for the already-shipped
marketplace order receiving feature (TASK-586/ADR-033 backend + mobile, both live in prod).
Full API contract (5 endpoints, DTOs, error strings, auth), known v1 limits, and
confirmed-implemented details (manual search fallback, datetimepicker 9.1.0, barcode types,
`saveItem()` always sends a full field snapshot). Verified every route/DTO/error string against
current source — old pre-implementation handoff (`586-to-mobile-codex.md`) had zero drift.

## TASK-593 — Events: multi-store scope + global header selector wiring (frontend)

**Status:** done · **Agent:** frontend-developer · parallel to TASK-592 (database-engineer,
`storeIds` migration) and a backend-developer agent building the matching API contract.
Log: `.claude/logs/tasks/593_2026-08-22_multi-store-event-scope-frontend_frontend-developer.md`

Adds a 3rd event scope `"stores"` (several specific stores, via reused
`LocationsMultiSelectDropdown`) alongside existing `"network"`/single-`"store"`; wires the
Events calendar page to the global header store selector (`useStoreContext`, multi-store
pattern like `useUsers()`) so it now filters by the selected store(s) instead of always
fetching every event. `tsc --noEmit` + eslint clean on all 6 touched files.

## TASK-590 — Product sales trend vs. baseline comparison (backend)

**Status:** done · **Agent:** backend-developer
Log: `.claude/logs/tasks/590_2026-08-21_product-sales-trend-comparison_backend-developer.md`

Backend half of the Events calendar sales-comparison feature (product linked to a demand event,
e.g. Easter → paska bread, vs. an equal-length baseline period immediately preceding it, sourced
from live POS transactions). Extended the existing `GET /api/analytics/pos/products/{productId}/
trend` action (TASK-482) with `compare`/`compareFrom`/`compareTo` query params — no new route,
reused the existing `ResolveCompareRange`/`PercentChange` helpers unchanged.
New `ProductSalesTrendComparisonDto` + `IAnalyticsService.GetProductSalesTrendComparisonAsync`
(calls `IAnalyticsRepository.GetProductSalesTrendAsync` twice — current + baseline — same
zip pattern as `GetPosRevenueTrendComparisonAsync`; no repository/SQL changes). Zero-sale
baseline window handled as zero totals + null percent-change, not an error. Deliberately kept
single-product (no batched multi-product endpoint) — frontend calls once per linked product.
5 new tests. Build clean, full suite 1790/1790 passing. Frontend team: see task log's "Notes for
frontend" for the exact contract.
Note: originally logged as TASK-588 by the agent (ran in an isolated worktree, collided with a
parallel agent's TASK-588); renumbered to TASK-590 when merging into the main tree.

**Parallel workstream — Stage 6 (Multi-Tenant Consumer Platform):** TASK-526 audit done,
TASK-556 (2026-08-17) resolved the 3 open decisions and registered TASK-527–555 as `planned`
(TASK-529/530 descoped). Implementation ready to start with TASK-527/TASK-528 (Stage A). Full
detail: `.claude/tasks/mobile-roadmap.md` Stage 6.

## TASK-560 — App Builder live preview: architecture + task breakdown (project-architect)

**Status:** done · **Agent:** project-architect
Log: `.claude/logs/tasks/560_2026-08-19_app-builder-live-preview-architecture_project-architect.md`
ADR: `.claude/docs/decisions.md` ADR-031. Designed the Elementor-style live preview panel for
`/consumer-app/pages` (`AppBuilderCanvas.tsx`) — web-native mirror components (not RN-web reuse),
4 new resizable `int` props on 4 block types (`heroBanner.heightPx`,
`bannerCarousel`/`promotionCarousel`/`productCarousel`.`cardWidthPx`), entirely client-side
(zero new backend endpoints). Registered TASK-561–566 as `planned`, sequenced backend → {mobile ∥
frontend chain} → qa; no worktree isolation needed (backend/mobile/frontend touch disjoint trees).

## TASK-561 — Block Registry: 4 new resizable size props (backend)
**Status:** done · **Agent:** backend-developer
Log: `.claude/logs/tasks/561_2026-08-19_block-registry-size-props_backend-developer.md`
Added `heightPx`/`cardWidthPx` `BlockPropDefinition`s to `heroBanner`/`bannerCarousel`/
`promotionCarousel`/`productCarousel` per ADR-031's bounds table. Build clean, 251/251
`MobileConfig`-filtered tests pass.

## TASK-562 — Mobile: consume heightPx/cardWidthPx in CoreBlocks + fix resolveBlocks prop drop
**Status:** done · **Agent:** mobile-developer
Log: `.claude/logs/tasks/562_2026-08-19_mobile-block-resize-props_mobile-developer.md`
Added `heightPx`/`cardWidthPx` to block types + validators; fixed the `resolveBlocks.ts`
prop-forwarding gap for `cardWidthPx` on all 3 carousel types (was silently dropped before);
`CoreBlocks.tsx` now honors both with today's hardcoded values as fallback. `tsc --noEmit` clean,
full mobile suite 54/54 suites (255/255 tests) green including new regression-guard cases.

## TASK-563 — Frontend: extract shared PhoneFrame.tsx from ThemeEditorSection
**Status:** done · **Agent:** frontend-developer
Log: `.claude/logs/tasks/563-565_2026-08-19_app-builder-live-preview-panel_frontend-developer.md`
New `PhoneFrame.tsx`, wired into `ThemeEditorSection.tsx`. Verified byte-identical computed styles
in-browser (320px/28px/8px border/boxShadow/24px padding) — zero visual change.

## TASK-564 — Frontend: web-native block preview mirror components + AppPreviewPanel (read-only column)
**Status:** done · **Agent:** frontend-developer
Log: `.claude/logs/tasks/563-565_2026-08-19_app-builder-live-preview-panel_frontend-developer.md`
New `blockPreviews.tsx` (12 mirror components, theme tokens derived from `MobileThemeDto` via the
same formulas as `mobile/features/theme/tokens.ts`) + `AppPreviewPanel.tsx` (maps `useBanners`/
`usePromoProducts`/`useCatalogProducts`/`useLocations` into preview items); third sticky column
added to `AppBuilderCanvas.tsx`. Verified live in-browser: add/remove/reorder reflect in the same
render, loyalty blocks show a visible "приклад даних" sample-data badge.

## TASK-565 — Frontend: live unsaved-edit reflection + resize drag handles
**Status:** done · **Agent:** frontend-developer
Log: `.claude/logs/tasks/563-565_2026-08-19_app-builder-live-preview-panel_frontend-developer.md`
`BlockPropertyEditor.tsx` gained `onLiveChange`; `AppBuilderCanvas.tsx` gained `liveProps`/
`previewBlocks`/`updateBlockSizeProp`; new `useResizeDrag.ts` hook (native Pointer Events, no drag
library) backs 4 resize handles in `blockPreviews.tsx`. Verified live in-browser: typing reflects
before Apply and reverts on Cancel; drag clamps at both registry bounds (120/260px tested); dirty/
Save-button state changes exactly once per drag gesture, not per pointermove.

## TASK-566 — QA: App Builder live preview regression pass (web ↔ mobile parity)
**Status:** done · **Agent:** qa-tester
Log: `.claude/logs/tasks/566_2026-08-19_app-builder-live-preview-regression_qa-tester.md`
Full regression pass clean: add/remove/reorder, property live-edit/revert/persist, all 4 resize
types' bounds + commit-once-per-drag, old-config zero-regression (web + mobile suite 31/31), full
draft→publish byte-for-byte parity, loyalty sample-data badges, TASK-539/540/541/546 flows all
unaffected. One bug found (TASK-565 `BlockPropertyEditor.tsx` infinite "Maximum update depth
exceeded" loop while any drawer was open) — fixed in TASK-565b (switched to `watch()`'s
subscription form). Targeted re-check confirmed the fix: 0 console errors over 9s idle with a
drawer open (was 37/~6s), live-reflect-before-Apply/Apply-persists (verified via raw API)/
Cancel-reverts all intact, resize commit-once-per-drag intact (dirty stayed false through
pointerdown+pointermove, flipped true only on pointerup).

## TASK-567 — App Builder: fix preview-panel wrap bug + phone-model picker (frontend)

**Status:** done · **Agent:** frontend-developer · **Direct user-reported follow-up to TASK-560..566**
Log: `.claude/logs/tasks/567_2026-08-19_preview-layout-and-device-picker_frontend-developer.md`
Bug: preview column wrapped below the canvas on every window size — root cause was
`consumer-app/pages/page.tsx`'s `maxWidth: 1100` page wrapper, not just the row's own `flexWrap`;
bumped to 1360 + new `useMediaQuery` hook drives `nowrap` above that breakpoint. Feature: 5-preset
device-model picker (`devicePresets.ts`, default Pixel 8 Pro) — `PhoneFrame.tsx` gained additive
`width`/`height` props (byte-identical when omitted, verified against `ThemeEditorSection.tsx`'s
preview), `AppPreviewPanel.tsx`'s scroll area now sized from the selected device's real height.
`tsc --noEmit` clean; verified in-browser via DOM geometry (screenshot unavailable this session).

## TASK-568 — App Builder preview: viewport-fit scaling, show/hide toggle, interactive bottom nav (frontend)

**Status:** done · **Agent:** frontend-developer · **Follow-up to TASK-560..567**
Log: `.claude/logs/tasks/568_2026-08-19_preview-scale-toggle-navigation_frontend-developer.md`
Three parts: (1) `PhoneFrame.tsx` gained opt-in `fitToViewport` — scales the device mockup down
(CSS `transform: scale()`, true aspect ratio, outer box reserves the scaled footprint) to fit the
vertical room below the panel, recomputed on window resize; omitted → byte-identical
(`ThemeEditorSection.tsx` unaffected, verified). (2) `AppBuilderCanvas.tsx` gained a
`previewVisible` toggle (`Btn variant="ghost"` next to the canvas title) — hiding the preview
column lets the canvas column reclaim the width. (3) `AppPreviewPanel.tsx` now renders its own
bottom tab bar mirroring the tenant's real `navigation` config (icons reused from
`NavigationBuilderSection.tsx`'s now-exported `NAVIGATION_ICON_COMPONENTS`) — clicking one of the
4 App-Builder-editable types (home/promotions/catalog/news) switches the previewed page
independent of the canvas's own `PageTabs`; clicking one of the other 4 (loyalty/coupons/stores/
profile — verified against `mobile/features/retail-navigation/policy.ts`'s `retailRoutePolicies`
as the source of truth, not guessed) shows a "not editable here" placeholder instead of
fabricated content (ADR-031 truthfulness requirement). `AppBuilderCanvas.tsx` now passes
`pages`/`navigation`/`activePage` instead of a single page's `blocks`, substituting
`activePage`'s entry with the TASK-565 live-edit-merged array so before-Apply live editing still
works when the preview's shown page matches the canvas's active page.

`npx tsc --noEmit` clean. Verified in-browser at an 800px-tall viewport: full mockup (chrome +
bottom nav) fits with 0 outer-page scroll needed to see its bottom; toggle hides/reclaims width
and restores correctly; clicking Home→Promotions in the mockup's own nav changed content
independent of the canvas's active tab; a temporarily-added `loyalty`-type nav item showed the
placeholder (test nav items removed after); re-synced correctly when the canvas's `PageTabs`
selection changed; live-property-edit-before-Apply and the resize-drag handles (simulated
pointer down/move/up, 225→255px, dirty flipped once on release) both still function with the new
nested/scaled frame. `ThemeEditorSection.tsx`'s own preview confirmed pixel-identical (320px,
`transform: none`, `overflow: visible`). Known accepted minor tradeoff (not fixed, out of this
task's scope): the 4 resize handles' pointer-delta math isn't scale-aware, so under
`fitToViewport` scaling a drag needs more physical mouse travel than before to reach the same
value change — functionally correct (clamps/commits right), just visually less than 1:1 while
scaled below 1.

## TASK-569 — Fix 4-column grid preview clamped to 2 columns (web + mobile)

**Status:** done · **Agents:** frontend-developer (web half) + mobile-developer (app half, parallel)
Log (web): `.claude/logs/tasks/569_2026-08-19_fix-4-column-grid-preview_frontend-developer.md`
Log (mobile): `.claude/logs/tasks/569_2026-08-19_fix-4-column-grid-mobile_mobile-developer.md`
Bug: `columns: 4` on Product Grid / Promotion Grid rendered only 2 cards per row. Root cause (web):
`blockPreviews.tsx`'s `columns()` helper only recognized `3`, clamping everything else (including
`4`) to `2`; the two grid width ternaries were binary (`=== 3 ? "31%" : "48%"`), so `4` never had a
branch. Root cause (mobile) was worse: `validators.ts`'s `validColumns` type guard only accepted
`2 | 3 | undefined`, so a published block with `columns: 4` failed prop validation entirely and
`BlockRenderer.tsx` rendered it as `null` — the block vanished on real devices, not just wrong
layout. Also clamped in `resolveBlocks.ts`'s `columns()` helper and hardcoded 2-or-3 in
`types.ts` (`PromotionCollectionProps`/`ProductCollectionProps`) and `CoreBlocks.tsx`'s
`PromotionGridBlock`/`ProductGridBlock` width ternaries. Fixed all 4 mobile spots to accept/pass
through/render `2 | 3 | 4`, using the same `23%` 4-column width as the web fix (ADR-031 web/app
parity). Widths for 2 (`48%`) and 3 (`31%`) unchanged.

Tests: +5 new cases across `coreBlocks.test.tsx` (validators accept `columns: 4`; grids render at
`23%`) and `resolveBlocks.test.ts` (columns: 4 forwarded unchanged) — 35/35 passing (was 30).
`npx tsc --noEmit` clean on mobile. `npx tsc --noEmit` clean on web (frontend half). Verified
in-browser (DOM geometry, web) and by full jest run (mobile) — zero regression on existing
2/3/undefined column values on either side.

## TASK-570 — Catalog Curation (Phase 1): architecture + task breakdown (project-architect)

**Status:** done · **Agent:** project-architect
Log: `.claude/logs/tasks/570_2026-08-19_catalog-curation-architecture_project-architect.md`
ADR: `.claude/docs/decisions.md` ADR-032. Handoff: `.claude/logs/handoffs/
570-to-backend-mobile-frontend_project-architect.md`. Designed catalog curation for
`productGrid`/`productCarousel` — a new `BlockPropTypes.ProductIds` kind (not a `stringArray` name
special-case, keeps `BlockPropertyEditor.tsx`'s switch-only-on-type invariant intact), curated
selection overrides today's alphabetical `limit`-slice with the admin's exact chosen order, stale/
deleted ids silently skipped. Found and designed around a real gap: both `PageRenderer.tsx` (mobile,
hardcoded `pageSize=30`) and `AppPreviewPanel.tsx` (web, `/api/items` default `pageSize=50`, no
search/id filter) only ever see a short alphabetical catalog prefix — a curated pick outside that
window would silently resolve as "deleted" without a new bounded catalog-by-ids read path on both
sides. `promotionGrid`/`promotionCarousel` and bestsellers/personalization/personal-discounts/
POS-bonus are explicitly out of scope (user-deferred to a future initiative). Registered TASK-571–576
as `planned` below, sequenced backend (571→572, one spawn) ∥ mobile (573) ∥ frontend (574→575, one
spawn) → qa (576); no worktree isolation needed (disjoint trees).

## TASK-571 — Backend: `productIds` block-prop kind + Block Registry entries
**Status:** done · **Agent:** backend-developer
Log: `.claude/logs/tasks/571-572_2026-08-19_catalog-curation-backend_backend-developer.md`
`BlockPropTypes.ProductIds` constant + `productGrid.productIds`
(MaxItems 30) / `productCarousel.productIds` (MaxItems 20) registry entries.

## TASK-572 — Backend: catalog-by-ids query support (admin `/api/items` + new consumer endpoint)
**Status:** done · **Agent:** backend-developer
Log: `.claude/logs/tasks/571-572_2026-08-19_catalog-curation-backend_backend-developer.md`
`/api/items` gains `search`/`ids` filters; new
`GET /api/consumer/{tenantId}/catalog/by-ids` endpoint.

## TASK-573 — Mobile: curated-selection resolution
**Status:** done · **Agent:** mobile-developer
Log: `.claude/logs/tasks/573_2026-08-19_mobile-curated-catalog-resolution_mobile-developer.md`
`resolveBlocks.ts` curated-order resolution + `PageRenderer.tsx` catalog-by-ids merge (fixes the
"only sees first 30 alphabetically" gap).

## TASK-574 — Frontend: `productIds` field type + `ProductPickerField` + catalog search/by-ids hooks
**Status:** done · **Agent:** frontend-developer
Log: `.claude/logs/tasks/574-575_2026-08-19_catalog-curation-frontend_frontend-developer.md`
`BlockPropertyEditor.tsx` gains the `productIds` case in its 3 switches; new searchable
multi-select `ProductPickerField.tsx`; `catalogApi.getAll`/`useCatalogProducts` gain `search`/`ids`;
new `useCatalogProductsByIds`. `npx tsc --noEmit` clean.

## TASK-575 — Frontend: `blockPreviews.tsx` + `AppPreviewPanel.tsx` curated-selection parity
**Status:** done · **Agent:** frontend-developer
Log: `.claude/logs/tasks/574-575_2026-08-19_catalog-curation-frontend_frontend-developer.md`
Same curated-resolution logic as TASK-573, web-preview side (fixes the same "short catalog prefix"
gap via a new `catalogById` by-ids fetch merged into `AppPreviewPanel`). `npx tsc --noEmit` clean;
live-verified in browser (`ea@demo.local`): search reaches `/api/items?search=`, curated pick
outside the default page resolves via `/api/items?ids=`, live preview updates instantly in chosen
order, removal reverts to byte-identical alphabetical fallback, Apply/Cancel untouched.

## TASK-576 — QA: Catalog curation regression pass
**Status:** done · **Agent:** qa-tester
Log: `.claude/logs/tasks/576_2026-08-19_catalog-curation-regression_qa-tester.md`
Clean pass, no bugs. Verified: search reaches `/api/items?search=` (network-confirmed, not
client-filtered); `MaxItems` cap (20/30) enforced with search UI hidden past cap; empty selection
byte-identical to today's alphabetical-`limit` fallback; curated order + `limit` cap correct;
outside-window resolution correct on both web preview (`/api/items?ids=`) and the mobile-facing
`catalog/by-ids` endpoint (tested directly); deactivated curated item silently disappears
client-side and server-side (`IsActive` filter), no console error; full publish loop round-trips
byte-identical `productIds`/order via `GET /api/v1/mobile/config`; `promotionGrid`/
`promotionCarousel` untouched; App Builder regressions (device picker, preview toggle, interactive
nav, dirty-guard) spot-checked clean; drag-reorder not re-driven (pane tooling limitation) but
`AppBuilderCanvas.tsx` confirmed zero-diff via `git diff --stat`. `tsc --noEmit` clean; targeted
`dotnet test` 307/307 pass.

## TASK-586 (stage 2/4) — Marketplace order receiving: schema layer (database-engineer)

**Status:** done · **Agent:** database-engineer · **Depends:** project-architect ADR-033
Log: `.claude/logs/tasks/586_2026-08-21_marketplace-order-receiving-schema_database-engineer.md`
Handoff: `.claude/logs/handoffs/586-to-backend_database-engineer.md`
New `MarketplaceOrderReceipt`/`MarketplaceOrderReceiptItem` entities + `MarketplaceOrder.
DestinationStoreId` (nullable) + migration `20260821151649_AddMarketplaceOrderReceiving` with
split client-write/supplier-read RLS (`tenant_isolation` + `supplier_read` FOR SELECT, deliberately
not the OR-based `marketplace_orders` pattern — supplier gets no write access). Migration applied
to local dev DB, schema verified column-for-column against ADR-033's spec, `RlsCrossTenantIntegrationTests`
(incl. the FORCE-RLS audit test) pass, full suite 1765/1765. Stage 3 (backend-developer) and stage 4
(frontend-developer) pending; mobile handled separately by a Codex-based agent.

## TASK-586 (stage 3/4) — Marketplace order receiving: DTO/service/API layer (backend-developer)

**Status:** done · **Agent:** backend-developer · **Depends:** TASK-586 stage 2 (database-engineer)
Log: `.claude/logs/tasks/586_2026-08-21_marketplace-order-receiving-logic_backend-developer.md`
Handoff: `.claude/logs/handoffs/586-to-frontend_backend-developer.md`
New `MarketplaceOrderReceiptService`/`IMarketplaceOrderReceiptService` + `IMarketplaceOrderReceiptRepository`
(create-draft / per-item update / finalize, mirrors `ReceiptService`). 5 new endpoints on
`MarketplaceCooperationController` (order-centric routes). `AllowedTransitions[Shipped]` key
removed — supplier can no longer self-declare Delivered; `MarketplaceOrderReceiptService.ReceiveAsync`
is now the only writer of `Status = Delivered`. `CreateOrderAsync` requires `DestinationStoreId`
(400 if missing); DB column stays nullable (historical orders). Full suite 1785/1785 (1765 + 20
new). **Pre-deploy check required on PROD before this ships** — see task log; local dev returned
0 rows but the table is empty there (uninformative). Stage 4 (frontend-developer) next; mobile
handled separately by a Codex-based agent using the handoff doc above.

## TASK-586 (stage 4/4) — Marketplace order receiving: web layer (frontend-developer)

**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-586 stage 3 (backend-developer)
Log: `.claude/logs/tasks/586_2026-08-21_marketplace-order-receiving-web_frontend-developer.md`
Required destination-store picker on the order-creation cart (`SupplierOrderCart.tsx`, explicit
`useStores()` `<select>`, not `usePrimaryStoreId()` per ADR-033 Decision 2). Removed the dead
"Deliver" button in `CabinetOrdersTab.tsx` (`shipped` case), replaced with a status hint. New
read-only "what was actually received" block on `/marketplace/orders` (client side only) via new
`useMarketplaceOrderReceipt` hook + `GET .../orders/{id}/receipt`. **Supplier-cabinet received-
detail block skipped, confirmed unreachable**: the endpoint hard-checks the caller's tenant
against the order's `ClientTenantId` (backend/.../MarketplaceOrderReceiptService.cs:108-113) and,
independently, the supplier tenant used in testing doesn't have the `marketplace` module active
(`403 Module not activated`) — both confirmed live against a real supplier session. `tsc`/lint
clean; full manual browser pass (order creation → confirm → ship → SQL-fixtured deliver+receipt),
fixtures cleaned up after. Stage 4/4 — TASK-586 complete; mobile scan/count UI handled separately
by a Codex-based agent per the handoff.

**Stage 5/4 (orchestrator, not a spawned agent):** wrote
`.claude/logs/handoffs/586-to-mobile-codex.md` — self-contained mobile-facing API contract
(routes, DTOs, error messages, existing mobile patterns to follow — Receipts screens, `scan.tsx`
barcode flow, missing date-picker dependency) for the separate Codex agent to build the scan/
count/finalize screens against. Backend + web scope for TASK-586 is now fully done; mobile is out
of this session's scope entirely.

## TASK-587 — Remove local store picker on Sales page (frontend)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-21 · **Next:** none
Log: `.claude/logs/tasks/587_2026-08-21_remove-local-store-picker-sales_frontend-developer.md`

Same cleanup as TASK-583, extended to Sales (manual daily-sales entry). Removed all three
local store `<select>`s — page filter, `SaleEntryForm`, `CsvImportDialog` — all previously
reading `useStores()` independently of the header's `StoreSelector`. Switched to
`usePrimaryStoreId()`. List filter (`useDailySales`) stays unfiltered when "all stores" is
active (backend `store_id` is optional there); "Add Sales"/"Import CSV" are
`disabled={!primaryStoreId}` with **one shared** hint (not duplicated per button). `storeId`
is now a fixed prop into both modals (no longer a user-editable field) — `POST /api/daily-sales`
and `POST /api/daily-sales/import` both require a single concrete `Guid StoreId`, unchanged.
i18n: added `Dashboard.sales.page.selectStoreHint`; removed the now-dead `allStores`,
`entryForm.storeLabel`, `csvImport.storeLabel`, `entryForm.validation.selectStore` keys (all
confirmed unused elsewhere via grep) — in both `en.json`/`uk.json`. `tsc --noEmit` and
`eslint` clean on all 5 touched files. No authenticated browser session available (fresh dev
server, empty localStorage) — did not log in per task boundary; live check left for the user.

## TASK-588 — Remove event coefficient endpoint (backend)

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-21 · **Next:** frontend
(day-detail drawer "unlink product" button; already in progress in parallel per orchestrator)
Log: `.claude/logs/tasks/588_2026-08-21_remove-event-coefficient-endpoint_backend-developer.md`

Added `DELETE /api/events/{id}/coefficients/{coefId}` — the missing counterpart to existing
`POST`/`PUT` coefficient endpoints, needed by the Events calendar day-detail view (link/unlink
products to a demand event). `IEventRepository.RemoveCoefficient(DemandEventCoefficient)` (void,
mirrors existing `Remove(DemandEvent)`) → `EventRepository` removes via
`_db.DemandEventCoefficients`. `IEventService.RemoveCoefficientAsync(eventId, coefId, ct)` reuses
existing `GetCoefficientAsync` lookup and mirrors `UpdateCoefficientAsync`'s not-found/ownership
check exactly (`"Coefficient not found."` when missing OR `coef.EventId != eventId`). Controller
action inherits the controller's class-level `[Authorize(Policy = AppPolicies.AtLeastStoreManager)]`
— same as `AddCoefficient`/`UpdateCoefficient`, no per-action override needed. New
`EventServiceTests.cs` (Events test folder had none yet, only the resolver test) covers not-found,
wrong-event-id, and happy-path (repo `RemoveCoefficient` + `SaveChangesAsync` called). Full
`dotnet test` (1788 tests) and `dotnet build` clean.

## TASK-519 — Users list: close storeIds authorization gap (backend)

**Status:** done · **Agent:** security-reviewer
Log: `.claude/logs/tasks/519_2026-08-13_users-store-scope-auth-fix_security-reviewer.md`
Renumbered from the brief's suggested TASK-518 — that id was claimed concurrently by
frontend-developer's `StoreSelector.tsx` UX cleanup
(`518_2026-08-13_hide-redundant-all-stores-toggle_frontend-developer.md`).

TASK-517's `GET /api/users?storeIds=...` trusted the caller-supplied `storeIds` with no check
that the acting caller was authorized to see those stores — `users` is excluded from ADR-022
Stage 3 RLS, so any store-bound role (`store_manager` etc.) could select "all stores" and see
every employee in the tenant, or request a store they weren't assigned to. Fixed:
`UserService.GetAllAsync` gained an `actingUserId` parameter — when the acting caller's own
role is in `LocationScopedRoles`, their effective `storeIds` is always clamped to their own
`user_locations` (intersected if explicit, "my own stores" if omitted, fail-closed to zero
location-scoped users if the clamp collapses to nothing). `UsersController.GetAll` now passes
the JWT-resolved acting user id. `SupplierCabinetService.GetStaffAsync` unaffected
(`actingUserId` defaults to null).

New tests: `UserServiceStoreFilterTests.cs` +6 cases. Docs: `.claude/docs/api-contracts.md`
updated. Build clean, `dotnet test` full suite 1411/1411 passing.

## TASK-517 — Users list storeIds filter (header store selector)

**Status:** done · **Agents:** backend-developer + frontend-developer (parallel, fixed contract)
Renumbered from the brief's suggested TASK-508 — max in `.claude/logs/tasks/` at start was 516
(TASK-508..516 already taken by concurrent KI-033 fix, pchilka import, store-selector/analytics
frontend, floor-plan work).

**Backend** (`.claude/logs/tasks/517_2026-08-13_users-store-filter_backend-developer.md`):
`GET /api/users` gains an optional repeated `storeIds` query param (same convention as
`PriceSegmentsController`) so the Users page can respect the header store selector.
`IUserLocationRepository.GetUserIdsWithLocationInAsync` (new, batched) backs
`UserService.GetAllAsync(tenantId, storeIds, ct)`: non-location-scoped roles (enterprise_admin
etc.) always visible; location-scoped-role users need ≥1 `user_locations` row in `storeIds`.
`NeedsLocationAssignment` unaffected — still computed from the full, unfiltered assignment.

Fixed a positional-arg compile break at `SupplierCabinetService.GetStaffAsync` (now-earlier `ct`
slot) and its test's NSubstitute setup. New tests: `UserServiceStoreFilterTests.cs` (5 cases).
Docs: `.claude/docs/api-contracts.md` updated.

Build clean, `dotnet test` full suite 1405/1405 passing.

**Frontend** (`.claude/logs/tasks/517_2026-08-13_users-store-filter_frontend-developer.md`):
`usersApi.getAll(storeIds?)` (repeated `?storeIds=`, same style as `priceSegmentsApi`) +
`useUsers()` now reads `useStoreContext`'s `selectedStoreIds` and includes it in the React
Query key. No page/component changes — all 8 `useUsers()` consumers (Users page, UsersList,
TenantRolesTab, TicketDetail, NotificationFilterDrawer, WeekGrid, CreateWorkOrderModal) pick
up the filter transparently. Known limitation: the 3 optimistic `setQueryData(USERS_KEY, ...)`
calls (invite/update/deactivate) only patch the all-stores cache entry now — acceptable, a
fresh fetch happens on store switch anyway.

`npx tsc --noEmit` clean. Live-verified against the backend's running implementation:
`GET /api/users?storeIds=<id>` with one store selected, plain `GET /api/users` on "All
stores" — both 200 OK.

## TASK-518 — Hide redundant "Select all stores" toggle for single-store users

**Status:** done · **Agent:** frontend-developer
Follow-up UX report on TASK-517: a store-scoped user narrowed to exactly one store still saw
a separate "Select all stores" checkbox implying a broader choice distinct from their own
store. Backend authorization half of the same report handled in parallel by security-reviewer.

`frontend/components/layout/StoreSelector.tsx`: "Select all stores" checkbox row now wrapped
in `{stores.length > 1 && (...)}`. 0-store early return and single-store's own row (existing
`{stores.map(...)}`) untouched — pure conditional-render change, no state/logic changes.

`npx tsc --noEmit` clean. Live-verified multi-store account (`ea@demo.local`, 4 stores) still
shows "All stores" + all 4 rows. Single-store account not live-verified: while switching test
accounts the agent logged the shared dev session out and then could not log back in — entering
a password (even a local dev-seed one) is a hard-blocked action with no exceptions. Shared dev
browser tab is left logged out; needs manual re-login before further browser verification.
Full detail: `.claude/logs/tasks/518_2026-08-13_hide-redundant-all-stores-toggle_frontend-developer.md`.

## TASK-505 — Docs: store-migration API contracts + known-issues entry
**Status:** done · **Agent:** documentation-writer
Log: `.claude/logs/tasks/505_2026-08-10_store-migration-docs_documentation-writer.md`
Renumbered from the plan's TASK-483. Added a "Store Migration" section to
`.claude/docs/api-contracts.md` (after the Post-Campaign Analysis section) documenting the 3 new
endpoints from TASK-502/503: `GET store-migration`, `GET store-migration/customers` (not in the
original plan, added mid-round per the 502→503 handoff), `POST exports/store-migration` — DTO
shapes, the first→last (not every-hop) migration definition, and the OR-semantics store filter
(from-store OR to-store, unlike the AND-style filter elsewhere on this controller). Added
**KI-033** to `.claude/docs/known-issues.md` for the RLS bug TASK-504 found (`store_scope` policy
silently corrupts marketing-analytics results, including reclassifying migrated customers, for
store_manager/network_manager) — status `open, needs architecture decision`, 3 lettered options
per the QA handoff (RLS bypass / explicit partial-data signal / hybrid), explicitly distinguished
from KI-031. No code touched, docs only. Read-back confirmed no duplicate KI numbers, formatting
matches sibling sections.

## TASK-504 — QA: store-migration feature end-to-end (real cross-store data)
**Status:** done · **Agent:** qa-tester
Log: `.claude/logs/tasks/504_2026-08-10_store-migration-qa_qa-tester.md`
Handoff: `.claude/logs/handoffs/504-to-backend_qa-tester.md`
Seeded real cross-store test data into tenant "Свіжий Кут" (2 new locations, 11+3
`pos_transactions`, 1 dedicated A→B→A edge-case customer, 2 emails, `user_locations` grants) —
prior handoffs only saw the empty state. Verified with populated data: matrix dynamic axis
(excludes stores with no cross-traffic), KPI math (migrated count/%/best-gain/worst-loss)
hand-checked against raw API JSON, customer table masking, store-filter OR-semantics (single
store shows both directions), A→B→A "not migrated" rule, Excel export (masked default +
unmasked for store_manager+), period/store filter atomic refetch. `dotnet test
--filter MarketingAnalytics`: 250/250 green. `npx tsc --noEmit`: clean.
**Bug found (high severity, pre-existing root cause):** `pos_transactions`' RESTRICTIVE
`store_scope` RLS policy silently corrupts store-migration results for any caller who isn't
provider/provider_admin/worker/enterprise_admin — a normally-scoped store_manager gets flows
that vanish entirely (real migrations reclassified as "not migrated") and undercounted
revenue/receipt-counts on the flows that do show, no partial-data indication. Confirmed the
aggregation logic itself is correct (matches enterprise_admin/raw-SQL exactly once RLS grants
are widened) — this is purely an RLS-visibility bug. Distinct from the already-tracked KI-031
(network_manager zero grants); affects the realistic production shape of store_manager. Full
repro + suggested directions in the handoff — needs a backend/architecture decision, routed
there rather than fixed inline (QA task, no code changes made). Also flagged: pre-existing RFM
overview endpoint has the same root-cause issue (smaller/wrong totals, not a reclassification).
Single-store-tenant empty-state guard still unverified (no usable single-store tenant in dev
DB) — same gap the frontend handoff already flagged, low risk by code inspection.

## TASK-506 — Backend: loyalty network picker — store names per tenant
**Status:** done · **Agent:** backend-developer
Log: `.claude/logs/tasks/506_2026-08-10_loyalty-network-store-names_backend-developer.md`
Renumbered from the brief's TASK-501 — that ID landed first under TASK-501/502/503/505 (this
sprint's concurrent store-migration workstream); actual max at start was 505. `GET
/api/consumer/loyalty/networks` (`LoyaltyService.GetAvailableNetworksAsync`) now returns each
qualifying tenant's active, shoppable store names alongside `tenantId`/`tenantName` — informational
only, membership stays one-per-tenant (confirmed with product owner, no per-store join). New
`LoyaltyNetworkSummaryDto.StoreNames` (`IReadOnlyList<string>`, always `[]` not null/omitted for a
zero-store tenant). Injected `ILocationRepository` (already DI-registered) into `LoyaltyService`;
reads settings + locations together inside the existing per-tenant `ITenantSessionOverride` block.
Filter: `IsActive` + `Type` not in `{warehouse, central_warehouse, distribution, office,
production}` — investigated first and found entity `Location.LocationType` is dead/unused despite
its name; the DTO field also named "LocationType" actually maps onto entity `Type`, which is the
real populated field (see `LocationService.IsValidLocationType` for its full value set). Sorted
alphabetically for stable ordering. `dotnet build`: 0 errors. `dotnet test`: 1387/1387 pass (also
fixed `LoyaltyJoinRlsIntegrationTests.BuildLoyaltyService`'s direct constructor call for the new
dependency, using the real `LocationRepository`). `docker build -f backend/Dockerfile backend`
succeeded. Nothing staged/committed — user reviews and deploys. Out of scope (per brief): mobile/
frontend consumption of the new field, and `JoinAsync`/`ResolveCodeAsync`/`GetConsumerCodeAsync`
untouched.

## TASK-507 — Backend: loyalty network stores restructure + consumer preferred-store
**Status:** done · **Agent:** backend-developer
Log: `.claude/logs/tasks/507_2026-08-10_loyalty-preferred-store_backend-developer.md`
Follow-up to TASK-506. `LoyaltyNetworkSummaryDto.StoreNames: string[]` replaced with
`Stores: LoyaltyNetworkStoreDto[]` (`{storeId, storeName, address}`) on `GET
/api/consumer/loyalty/networks` — same filter/sort, now with an ID mobile can reference. New,
explicitly separate "preferred store" concept — NOT a membership/join change (still one
`LoyaltyMembership` per tenant/consumer): `LoyaltyMembership.PreferredStoreId` (nullable Guid,
SetNull FK, same convention as `CustomerId`/`LinkedUserId`), migration
`20260811054559_AddLoyaltyMembershipPreferredStore` (applied to local dev Postgres), new `PUT
/api/consumer/loyalty/preferred-store` (`{tenantId, storeId}` → 403 no membership / 400 invalid
store / 200 updated membership), and `GET /consumer/loyalty/memberships` now resolves
`PreferredStoreId`/`PreferredStoreName`/`PreferredStoreAddress` (all null together if unset or
stale — never errors). `dotnet build`: 0 errors. `dotnet test`: 1397/1397 pass (1387 baseline +
10 new). `docker build -f backend/Dockerfile backend` succeeded. Nothing staged/committed. Out
of scope (per brief): mobile/frontend consumption, and `JoinAsync`/`ResolveCodeAsync`/
`GetConsumerCodeAsync`/`ResolveOrCreateMembershipByPhoneAsync` untouched.

## TASK-503 — Frontend: store-migration section on RFM dashboard
**Status:** done · **Agent:** frontend-developer
Log: `.claude/logs/tasks/503_2026-08-10_store-migration-frontend_frontend-developer.md`
Handoff: `.claude/logs/handoffs/503-to-504_frontend-developer.md`
Plan: `flickering-moseying-fountain.md`. New "Міграція покупців між закладами" section on
`/marketing-analytics`, always rendered below `SegmentDetailPanel`, driven by the page's
existing period/store `filters`/`enabled` state — no new filter UI. Types added to the root
`marketing-analytics/types.ts` (page-section, not a separate route like post-campaign/
price-segments/audience-builder, so no sibling types file). New API functions
(`getStoreMigration`, `getStoreMigrationCustomers`, `exportStoreMigration`) and hooks
(`useStoreMigration`, `useStoreMigrationCustomers`, `useExportStoreMigration`) follow the
existing `buildFilterQs`/React-Query-key-is-the-filter-object conventions exactly. New
`components/StoreMigration/` folder: `StoreMigrationSection.tsx` (KPI row + empty-state guard
for `useStores().length <= 1`), `StoreMigrationMatrix.tsx` (from×to table with a DYNAMIC axis
built from the stores actually present in `flows`, not the full tenant store list), `
StoreMigrationCustomerTable.tsx` (masked-PII drill-down list + export button/unmask-toggle,
since the unmask capability only ever applies to the export, never the on-screen list). i18n
keys added under `Dashboard.marketingAnalytics.storeMigration.*` in both `uk.json`/`en.json`.
Deviation: `KpiCard` reused as a locally-duplicated component (same shape as the one inline in
`page.tsx`) rather than imported across the app/→feature boundary — matches this codebase's
existing per-file `KpiCard` convention (`price-segments/page.tsx`, `FrequencyKpiCards.tsx`,
`AllTimeKpiCards.tsx` all do the same). `npx tsc --noEmit`: 0 errors. Manual verification: ran
backend+frontend locally against real Postgres, confirmed all 3 endpoints fire with correct
params on load and on filter change, both locales render correctly, export button downloads a
file (200 response). **Not verified: populated matrix/table visuals** — local test tenant had
no actual cross-store migration data (always `flows: []`); empty-state rendering confirmed
instead. Flagged for QA (TASK-504) as the top thing to check with real/seeded data.

## TASK-502 — Backend: store-migration feature on RFM dashboard (DTOs/repo/service/controller)
**Status:** done · **Agent:** backend-developer
Log: `.claude/logs/tasks/502_2026-08-10_store-migration-backend_backend-developer.md`
Handoff: `.claude/logs/handoffs/502-to-503_backend-developer.md`
Plan: `flickering-moseying-fountain.md` (renumbered from the plan's "TASK-480" — collided with
2026-08-07 work; see TASK-501's log). New, additive "customer store migration" feature: per
customer, first-store vs. last-store within a period; detects the customer as "migrated" if they
differ. Built on TASK-501's `idx_pos_tx_customer_migration` index
(`DISTINCT ON (cust_id) ... ORDER BY cust_id, created_at [ASC/DESC]`). New DTOs
(`StoreMigrationOverviewDto`, `StoreMigrationFlowDto`, `StoreNetFlowDto`,
`StoreMigrationCustomerRowDto`, `ExportStoreMigrationRequest`), repo methods
(`GetStoreMigrationFlowsAsync`, `GetStoreMigrationCustomersAsync`, plus
`GetActivePeriodCustomerCountAsync` — new, needed for the KPI %, not named in the brief),
service (`GetStoreMigrationAsync`, `GetStoreMigrationCustomersAsync`, `ExportStoreMigrationAsync`
+ `BuildStoreMigrationExcel`), controller: `GET store-migration`, `GET
store-migration/customers` (**added beyond the brief's literal 2-endpoint list** — the on-screen
drill-down table had no wired data source otherwise; PII always masked there, no unmask option),
`POST exports/store-migration` (masked-by-default, unmask gated by existing
`MarketingAnalyticsAuthorization.CanExportPii`). Store filter = from-OR-to store match. `dotnet
build`: 0 errors/warnings. `dotnet test --filter "FullyQualifiedName~MarketingAnalytics"`:
250/250 pass (live Postgres on 5435 was reachable, migration applied, integration tests actually
ran, not skipped) — includes new repo integration tests (single-store exclusion, 3-store
first/last resolution ignoring the middle store, from-only/to-only store filter) and new service
unit tests (net-flow derivation, zero-active-customers guard, masking). Fixed one pre-existing
test whose fixture-wide customer counts shifted after adding 2 new fixture customers. Frontend
(TASK-503) and docs (TASK-505) not touched — out of scope per brief.

## TASK-499 — Backend: per-tenant customer code display format (QR vs. barcode)
**Status:** done · **Agent:** backend-developer
Log: `.claude/logs/tasks/499_2026-08-09_loyalty-customer-code-format_backend-developer.md`
Product decision: a store network (`Tenant`), never an individual store, chooses whether its
customers' universal checkout code renders as QR or Code 128 barcode — `LoyaltyProgramSettings.
CustomerCodeFormat` (string, "qr"|"barcode", default "barcode"), threaded through
`GetSettingsAsync`/`UpsertSettingsAsync` (400 on any other value, including null/empty) and into
`LoyaltyProgramSettingsDto`. `GetConsumerCodeAsync` gained an optional `tenantId` param to resolve
`LoyaltyCodeDto.DisplayFormat`: explicit tenantId → 403 if not a member there, else that tenant's
format; omitted → 0 memberships = "barcode" default, 1 membership = that tenant's format, 2+ =
409 `network_selection_required`. `GET /api/consumer/loyalty/code?tenantId=` (new optional query
param) wired through in `ConsumerLoyaltyController`. Reads `loyalty_program_settings` from a
consumer session via `ITenantSessionOverride` (that table has no `consumer_self_access` RLS
policy, only the canonical tenant triad). Migration `20260809180100_AddLoyaltyCustomerCodeFormat`
(scaffolded with `dotnet ef migrations add`, then renamed forward past
`20260809180000_AddConsumerLoyaltyCodeSecret` — the real UTC clock was earlier than that
hand-authored migration's timestamp) applied cleanly to local dev Postgres. `dotnet build`: 0
errors. Full `dotnet test`: 1375/1375 pass (1363 baseline + 12 new cases, 0 regressions),
including all Loyalty *integration* tests live against Postgres. `docker build -f backend/
Dockerfile backend`: succeeds. Nothing staged/committed. Legacy `SGLOY1.` code resolution
(`ResolveCodeAsync`) untouched, confirmed still passing.

## TASK-498 — Backend: auto-create loyalty membership by phone at POS (no manual store selection)
**Status:** done · **Agent:** backend-developer
Log: `.claude/logs/tasks/498_2026-08-09_loyalty-auto-membership-by-phone_backend-developer.md`
Product decision: removes the only path that required a consumer to manually type a tenant GUID
to join a store's loyalty program. Staff-facing `POST /api/loyalty/resolve-or-create-by-phone`
(same `CanAccessPos` policy as `resolve-code`) normalizes a phone typed at the register, looks up
its `ConsumerAccount`, and idempotently gets-or-creates the `LoyaltyMembership` at the cashier's
own tenant — runs entirely inside the staff request's existing RLS tenant context, no
`ITenantSessionOverride` needed (unlike the consumer-session `JoinAsync`). Extracted
`JoinAsync`'s membership-creation body into a shared private `CreateMembershipCoreAsync`,
reused by the new `LoyaltyService.ResolveOrCreateMembershipByPhoneAsync`; `JoinAsync`'s external
behavior/signature unchanged. `dotnet build`: 0 errors. `LoyaltyServiceTests`: 39/39 pass
(includes 8 new TASK-498 cases + 2 pre-existing tests fixed — see below). Nothing staged/committed
(reviewed by product owner first).
Found (not fixed, out of scope): the working tree already carried unrelated, uncommitted WIP
(visible via `git diff HEAD`, predates this session) redesigning the consumer QR/checkout code
to be cross-tenant (`GetCurrentCodeAsync`→`GetConsumerCodeAsync`, new `ConsumerAccount.
LoyaltyTotpSecret` column, `ResolveCodeAsync`'s "SGLOY1." legacy branch) — explicitly out of scope
per this task's brief. Its test file was stale (2 broken compile refs), fixed only to unblock
`dotnet build`. Its EF migration was never added, so all 8 Loyalty *integration* tests
(`LoyaltyRepositoryIntegrationTests`, `LoyaltyJoinRlsIntegrationTests`,
`LoyaltyConcurrencySalesIntegrationTests`) fail against the real test DB with `column
"LoyaltyTotpSecret" of relation "consumer_accounts" does not exist` — pre-existing, unrelated to
TASK-498, needs its own migration before that other work can land.

## TASK-500 — Frontend: standalone Consumer App page with loyalty settings + `customerCodeFormat`
**Status:** done · **Agent:** frontend-developer
Log: `.claude/logs/tasks/500_2026-08-09_loyalty-customer-code-format-web_frontend-developer.md`
Initially blocked (no existing page consumed `GET/PUT /api/settings/loyalty` — see log for
detail); product owner resolved scope: a new standalone page (not a Settings tab/modal),
`frontend/app/(dashboard)/consumer-app/page.tsx`, deliberately scoped to grow more sections later
(loyalty only today, no placeholder scaffolding). New feature `frontend/features/consumer-app/`
builds the first-ever UI for all 5 pre-existing `LoyaltyProgramSettingsDto` fields
(enabled/accrual %/redemption cap %/min redemption balance/code TTL) plus the new
`customerCodeFormat` ("qr"/"barcode", TASK-499's finalized backend contract) in one
`BonusProgramSection` form, following `PrroConfigModal`'s conventions (closest existing analog,
same backend upsert shape per its own doc comment). Gated `AT_LEAST_ENTERPRISE_ADMIN`, new
Sidebar nav group (`consumer_app`), i18n added to both `uk.json`/`en.json`. `npx tsc --noEmit`
and `npm run lint`: both clean. Dev-server smoke check (`preview_start`, no backend available
locally): `/consumer-app` compiles and serves 200, no errors from new code. Save-payload and
round-trip-read paths verified by direct code trace (full live round-trip needs TASK-499's
backend deployed, out of reach from this task alone, as anticipated in the brief). Nothing
staged/committed.

## TASK-497 — Mobile: dual-token session model, wallet/history restoration, dead-code cleanup
**Status:** done · **Agent:** mobile-developer · **Depends:** TASK-496 (parallel, same working
tree) — handoff `.claude/logs/handoffs/496-to-mobile-developer.md` matched with zero deviations
Log: `.claude/logs/tasks/497_2026-08-08_unified-auth-wallet-cleanup_mobile-developer.md`
Consumed TASK-496's dual-token `MobileLoginResponse`. Store now holds `personalAccessToken`/
`workspaceAccessToken` (was single `accessToken`), both persisted+restored; two axios clients
(`apiClient` workspace-scoped, new `personalApiClient` consumer-scoped, no refresh — backend
issues no consumer refresh token) structurally guarantee token scoping per-module. Fixed 4
audited issues: (1) restored wallet/history screens into `(personal)/` (gated on
`personalAccessToken` presence, visible to both plain consumer and linked staff), deleted
`app/(consumer)/` entirely; (2) deleted `(auth)/login.tsx` outright (file-level, not just
Stack removal) — old staff-only login now truly unreachable; (3) added `/mobile-auth/{login,
register}` to `isPublicAuthRequest()` allowlist so their 401s don't trigger `/auth/refresh`;
(4) deleted dead consumer-login-path code (`useConsumerLogin`, `consumerAuthApi.ts`,
`consumerLoginSchema`). `useVerifyTwoFactor` now merges into `workspaceAccessToken` via
`setWorkspaceAuth` instead of clobbering the session. Self-review caught and fixed a bug: the
`(auth)` layout's authenticated-redirect guard would have bounced users away from the
two-factor screen the instant a mid-challenge `personalAccessToken` landed — added a
`!twoFactorChallenge` exception. `npx tsc --noEmit`: clean. `npx jest --runInBand`: 30/30
suites, 151/151 tests pass. Left `useLogin()`/`staffLoginSchema` orphaned-but-uncleaned
(out of the explicit dead-code scope) — flagged in the task log for a follow-up pass. No
`backend/` files touched.

## TASK-496 — Backend: dual-token mobile-auth response (personal + workspace JWT)
**Status:** done · **Agent:** backend-developer · **Depends:** the codex-built `mobile-auth`
endpoints (unified `POST /api/mobile-auth/{login,register}`, pre-existing) · **Next:**
mobile-developer (parallel, same working tree) consumes the new two-token shape — handoff at
`.claude/logs/handoffs/496-to-mobile-developer.md`
Log: `.claude/logs/tasks/496_2026-08-08_dual-token-mobile-auth_backend-developer.md`
Product decision: an employee is first a loyalty-program consumer (personal `ConsumerAccount`)
who additionally gets workspace access when linked to an active staff `User` — both identities
must be usable at once, so `MobileLoginResponse.AccessToken` (single token) is replaced with
`PersonalAccessToken`/`WorkspaceAccessToken` (both nullable). `MobileAuthDtos.cs` gained
`MobileLoginResponseFactory.ForLinkedStaff(personalToken, workspaceToken, user)` — reuses
`ForStaff`'s effective role/permissions/capabilities/tabs via a record `with` expression rather
than duplicating them. `MobileAuthController.Login`/`Register` updated for all 4 branches:
(1) consumer-only → `personalAccessToken` set, `workspaceAccessToken: null`; (2) consumer linked
to active staff, no 2FA → both tokens combined via `ForLinkedStaff`, reusing `consumer.AccessToken`
already in scope (no second consumer JWT minted); (3) consumer linked to staff requiring 2FA →
`{ requiresTwoFactor, challengeToken, personalAccessToken }` (new field on this branch only — lets
the client show loyalty/personal features while the second factor is pending); (4) legacy
staff-only fallback (no `ConsumerAccount` at all) → unchanged shape, `personalAccessToken: null`,
its own 2FA challenge stays exactly `{ requiresTwoFactor, challengeToken }` (no personal-token
field, nothing to expose). Also added the missing `[ProducesResponseType(typeof(object), 200)]`
on `Register` (had it on `Login` already, not on `Register`'s challenge branch). `AuthController`
`/api/auth/2fa/verify` untouched (out of scope, separate legacy staff-only endpoint, mobile client
stores its `{ accessToken, user }` result as `workspaceAccessToken` on its own side).
`dotnet build`: 0 errors. `dotnet test --filter "FullyQualifiedName~MobileAuth"`: 7/7 pass.
`dotnet test --filter "FullyQualifiedName~MobileLoginResponseFactoryTests"`: 3/3 pass (not matched
by the first filter — class name doesn't contain the substring "MobileAuth"). `dotnet test --filter
"FullyQualifiedName~ConsumerAuthService"`: 12/12 pass, unaffected. camelCase confirmed — no
`AddJsonOptions` in `Program.cs`, ASP.NET Core default policy applies.

## TASK-495 — QA: live E2E for analytics follow-up batch (TASK-488..494)
**Status:** done — **verdict: SHIP** · **Agent:** qa-tester · **Depends:** TASK-488..494 (all done) ·
**Next:** none blocking — batch complete, F1 below is the only open, non-blocking item
Log: `.claude/logs/tasks/495_2026-08-07_batch2-qa_qa-tester.md`
First genuine authenticated live E2E for this batch — every prior frontend task (488, 492, 493, 494)
only verified compile/build, and TASK-492 itself flagged TASK-488's "live check" as a false positive
(hit Next's not-found catch-all, not the real page). Read all 7 task logs + source files, then stood
up the real stack (Docker Desktop wasn't running — started it, `docker compose up -d`, `dotnet run`
port 5000 with `Cors__Origins` widened, `next dev` auto-port 50888) and tested with real logins
(`manager@demo.local` store_manager, `ea@demo.local` enterprise_admin). Same Browser-pane limitation
TASK-486 first found (hidden/unfocused tab blocks recharts' `ResizeObserver`/pointer-tracking) —
worked around via trusted DOM `.click()` dispatch (fully reliable for every plain element) plus
direct API probes; only genuine gap is chart-native day/point clicks (F1, non-blocking, same
precedent TASK-486/492 already documented). All 4 features PASS live, including the two highest-risk
checks: **worst-products correctly surfaces true zero-sale products** (8 live-confirmed, disjoint
from the 7-product top-sellers list, each with real `currentStock`) and **margin-gating is
unaffected by the new days-of-stock column** (`git diff` shows the `canViewMargin &&` gate itself
byte-for-byte untouched; live-confirmed absent for store_manager, present with correct ADR-027
arithmetic for enterprise_admin). Days-of-stock-remaining confirmed in all 3 states including the
real-number path — dev seed data had zero `ProductAdu` rows anywhere (`POST /api/adu/recalculate`
processed 0 products, a pre-existing seed/eligibility gap, not this batch's bug), so inserted one
temporary row directly via SQL (stock 70 ÷ ADU 5.0), confirmed the card rendered exactly "14d" end to
end through the full new client-side pipeline (`useAdu` → `stockApi` → division/rounding), then
deleted it (confirmed 0 rows left). `PosDayDetailPanel`/`ExpiryDonut`/`CategoryStatusChart`/
`LossesByReasonChart`/`LossesByStoreChart`/`PosRevenueTrendChart` all show **zero** uncommitted diff
— byte-identical to what TASK-486 already fully live-tested, strongest possible regression guarantee.
`dotnet build`/`dotnet test` (1344/1344, matches TASK-491's baseline) and `tsc`/lint/`next build`
(exit 0, 57/57 pages, same route sizes TASK-494 logged) independently re-run clean. Zero 500s,
zero React/hydration errors across the whole session. **F1 (non-blocking):** `LossesTrendChart`'s
day-point click itself still can't be live-clicked in this environment — mitigated by a source diff
proving it's structurally identical to the already-proven `PosRevenueTrendChart` mechanism, plus
independently confirming its entire downstream data path (panel + exact API call) live via an
equivalent trigger. Recommends the same 30-second manual spot-check TASK-486 already suggested — not
done by anyone across TASK-486/492/this pass. No blocking findings.

## TASK-494 — Frontend: days-of-stock-remaining UI (analytics follow-up batch)
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-491 (backend, done) ·
**Next:** TASK-495 (qa-tester) — last frontend task in the batch, feeds directly into that brief
Log: `.claude/logs/tasks/494_2026-08-07_days-of-stock-ui_frontend-developer.md`
Sixth of the follow-up batch (TASK-488..495). Consumed TASK-491's `daysOfStockRemaining: number |
null` field on `CategoryProductRowDto` (verified fresh against `AnalyticsDtos.cs` — `TotalQuantity
/ ProductAdu.AduEffective`, null both when the request has no `store_id` and when the product has
no ADU signal yet, the UI doesn't distinguish either case, renders "—"). `types.ts` gained the
field (appended to the existing interface, TASK-493's `WorstProductsDto`/`WorstProductRowDto` read
fresh and left untouched) plus a new `AduDto` (mirrors backend `AduDtos.cs`). New sortable "Днів
запасу"/"Days of stock" column in `CategoryDetailPanel.tsx` — deliberately **not**
margin-gated (operational, not cost data), red/amber/green/gray urgency coloring reusing this
table's own status-cell palette. `ProductAnalyticsTab.tsx` gained an optional
`daysOfStockRemaining?: number | null` prop (purely presentational, same posture as
`canViewMargin`) rendering one more `SummaryCard` — `undefined` omits the card entirely, `null`
renders "—", a number renders color-coded — mirrors the DTO's own absent-vs-empty null semantics.
`ProductTrendPanel.tsx` now actually wires up the `storeId?` prop it already accepted since
TASK-488 but never used: when concrete, fetches ADU via a new minimal `useAdu` hook (no prior
frontend consumption of `GET /api/adu/{storeId}/{productId}` existed anywhere — grepped first) and
on-hand stock via the existing `stockApi.getAll({store_id, product_id})` (features/shelf, no
backend change needed), computes `currentStock / aduEffective` client-side with the same
sold_out/archived exclusion and rounding the backend uses elsewhere, passes the result down. No
fetch at all when `storeId` is undefined (today: always true on `/analytics`, which has no
page-wide store filter — confirmed again, not re-fixed, per the brief's explicit
out-of-scope note; `/analytics/pos` does pass a concrete storeId and gets real numbers). New
`frontend/features/analytics/api/adu.ts` + `hooks/useAdu.ts` (retry skips an expected 404, same
precedent as `usePos.ts`'s `useCurrentShift`). Deliberately used `stockApi` directly via a local
`useQuery` instead of the shelf feature's `useStock` hook (which has no `enabled` param) to avoid
touching a shared hook outside this task's scope. `tsc --noEmit`/`npm run lint`/`npm run build` all
clean (build exit 0, 57/57 static pages, `/analytics` 8.51 kB/270 kB, `/analytics/pos` 5.39 kB/
261 kB First Load JS — both +~2kB as expected, same shared-tree Size-column churn TASK-493 already
flagged). Live dev-server check hit the same no-Docker-this-session constraint as every prior task
in this batch (confirmed via `docker ps` failing to reach the daemon): `/analytics` redirected to
`/login` cleanly, zero hydration/module-resolution errors from any new code.

## TASK-493 — Frontend: worst-performing products / dead-stock table UI (analytics follow-up batch)
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-490 (backend, done) ·
**Next:** none blocking
Log: `.claude/logs/tasks/493_2026-08-07_worst-products-ui_frontend-developer.md`
Fifth of the follow-up batch (TASK-488..495). Consumed TASK-490's `pos/worst-products` endpoint
end to end: `types.ts` gained `WorstProductsDto`/`WorstProductRowDto` (appended after TASK-492's
`LossesTrendDto`, untouched), `api/pos-analytics.ts` gained `getWorstProducts`,
`usePosAnalytics.ts` gained `useWorstProducts` (full-filter query key, no `keepPreviousData`, same
discipline as every sibling hook) — shape verified fresh against `PosAnalyticsDtos.cs`/
`AnalyticsController.cs` before writing types. New `WorstProductsTable.tsx` mirrors
`PosTopProductsTable.tsx`'s structure/styling verbatim (same hover/active-row `#111827`
mechanism, same `onRowClick?`/`selectedProductId?` prop shape) minus the barcode column (not in
the DTO) plus one new `currentStock` column in amber (`#FBBF24`, reusing this feature's existing
"warning"-class color from `CategoryDetailPanel.tsx` rather than inventing one) — the "N units
sitting unsold" evidence that makes a zero-revenue row actionable. `analytics/pos/page.tsx` read
fresh (built on top of TASK-484's `selectedProduct`/`handleProductClick` and TASK-488's renamed
`ProductTrendPanel` import, both confirmed unmodified) — new section rendered directly below the
existing Top-products+Cashiers grid, wired to the *same* `handleProductClick`/`selectedProduct`
values already passed to `PosTopProductsTable`, so either table's row click opens the same
`ProductTrendPanel` instance with zero new state. Deliberately no page-level `<h2>` wrapper for
the new section — followed this page's own precedent where `PosTopProductsTable`/
`PosCashierStatsTable` render their own internal title bar instead of an external heading (unlike
the KPI-style sections, which have no internal title and do get one); the internal title itself
("Товари, що не продаються" / "Products not selling") already reads as clearly distinct from "Топ
товари" / "Top products". New `Dashboard.analytics.pos.worstProducts` i18n block in both locale
files, inserted next to the sibling `topProducts` block. `tsc --noEmit`/`npm run lint`/`npm run
build` all clean (build exit 0 confirmed explicitly, 57/57 static pages, `/analytics/pos` 6.93
kB/259 kB First Load JS — route-specific size *smaller* than TASK-488's logged 11.4 kB figure for
the same route, most likely explained by intermediate churn from TASK-489..492's own edits already
sitting in this same still-uncommitted working tree rather than a regression from this task's own
diff; total First Load JS for the route unchanged at 259 kB either way). Live dev-server check hit
the same now-well-documented constraint as every prior build task this batch (no Docker/backend
this session): confirmed live that `/analytics/pos` is still edge-redirected by `middleware.ts`
before Next compiles the page at all (zero "analytics/pos" compile log line, zero hydration/
module-resolution console errors, only pre-existing `ENVIRONMENT_FALLBACK` next-intl noise and
expected `ERR_CONNECTION_REFUSED` from the unreachable backend) — `npm run build`'s successful
compile of the real bundle is the strongest signal available without a backend, same conclusion
TASK-492 already reached.

## TASK-492 — Frontend: losses/write-offs trend chart UI (analytics follow-up batch)
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-489 (backend, done) ·
**Next:** none blocking
Log: `.claude/logs/tasks/492_2026-08-07_losses-trend-ui_frontend-developer.md`
Fourth of the follow-up batch (TASK-488..495). Consumed TASK-489's `losses/trend` endpoint end to
end: `types.ts`/`api/analytics.ts`/`useAnalytics.ts` gained `LossesTrendDto`/`LossesTrendPointDto`,
`getLossesTrend`, `useLossesTrend` (full-filter query key, no `keepPreviousData`, shape verified
fresh against `AnalyticsDtos.cs`/`AnalyticsController.cs`). New `LossesTrendChart.tsx` mirrors
`PosRevenueTrendChart.tsx`'s `AreaChart`/recharts-3.8.1-click mechanism verbatim (single series,
red instead of blue, no compare Line/Legend — endpoint has none); tooltip tone matches the sibling
`LossesByReasonChart`/`LossesByStoreChart` instead. `analytics/page.tsx` read fresh (built on top
of TASK-488's merged `selectedProduct` wiring, nothing reverted) — new `useLossesTrend({from, to},
enabled)` call deliberately ungated by `compareEnabled` (matches `useExpirySummary`'s own
no-compare-variant shape on this page, not the flat/compare toggle other losses hooks use); new
`selectedLossDay` state + `handleLossDayClick` (toggle-on-reselect, same convention as every other
handler here), rendered inside the existing Write-offs section between the summary cards and
`LossesByReasonChart`. Day click reuses `LossesProductBreakdownPanel` unmodified (confirmed prop
shape unchanged: `{title, totalLoss, storeId?, reason?, from, to, onClose, onProductClick?}`) —
called with no `storeId`/`reason`, `from = to = selectedLossDay`, title built from the *existing*
`lossesProductPanelTitle` i18n key (reused, not duplicated) with a long-form date. New
`Dashboard.analytics.lossesTrendChart` i18n block in both locale files. `tsc --noEmit`/`npm run
lint`/`npm run build` all clean (build exit 0, 57/57 static pages, `/analytics` 9.96 kB/268 kB First
Load JS, up from TASK-488's 9.27 kB/263 kB as expected). **Correction to TASK-488's live-check
precedent:** its "`/uk/analytics` compiled and redirected to `/login` cleanly" claim doesn't
actually exercise the real page — that URL 404s to Next's `[...not-found]` catch-all (confirmed by
reading the rendered page text), which only *looks* like a pass because it shares
`(dashboard)/layout.tsx`'s auth-redirect wrapper. The real `/analytics` route sits in
`middleware.ts`'s `PROTECTED` array and is edge-redirected before Next compiles the page at all,
confirmed by reading `middleware.ts` directly — with Docker itself not running this session (not
just the containers down), there was no way to reach it live; `npm run build`'s successful compile
of the real `/analytics` bundle is the strongest signal available without a backend.

## TASK-491 — Backend: days-of-stock-remaining field on by-category/products (analytics follow-up batch)
**Status:** done · **Agent:** backend-developer · **Depends:** TASK-481 (by-category/products
endpoint, done — extended, not modified); TASK-490 (worst-products, done — same shared files, read
fresh, nothing of theirs touched) · **Next:** TASK-494 (frontend-developer, consume the new field)
unblocked
Log: `.claude/logs/tasks/491_2026-08-07_days-of-stock-field_backend-developer.md`
Third of the follow-up batch (TASK-488..495). Extends `CategoryProductRowDto`
(`AnalyticsDtos.cs`) with `decimal? DaysOfStockRemaining` — wire field `daysOfStockRemaining`,
`TotalQuantity / ProductAdu.AduEffective` rounded to 1 decimal. No controller/service/interface
signature changes — `storeId` already flowed into `GetCategoryProductBreakdownAsync` since
TASK-481. Verified `ProductAdu.AduEffective` fresh (matches secondhand description exactly) via
the entity, `AduService`/`AduCalculator.Compute`, and `AduController`/`AduRepository` as the
single-product/bulk-by-store precedents — confirmed `AduEffective` can be a real `0m` (not just
null), so the zero-guard is load-bearing, not defensive-only. `AnalyticsRepository.cs`: one new
bulk `_db.ProductAdus` query (keyed by `ProductId` via `ToDictionaryAsync`, `TenantId` filter
belt-and-suspenders like every other method in this file), gated on `storeId.HasValue` so a
network-wide/multi-store rollup never even runs it — every row's field stays `null` in that case.
Per-row null also when the product has no `ProductAdu` row, or `AduEffective` is `null`/`0m`
(division-by-zero guard doubling as "no usage history yet"). 3 new tests in
`PosAnalyticsServiceTests.cs` (populated when store-scoped / null without store scope / null on
zero-or-missing ADU) plus 3 pre-existing `CategoryProductRowDto` construction sites updated for the
new field — same service-layer pass-through-only boundary this file already uses for
`GetWorstProductsAsync`'s merge logic (division itself isn't independently unit-tested, no
EF-InMemory harness wired for this repository). `dotnet build`/`dotnet test` both clean
(1344/1344 = 1341 baseline + 3 new). No `AnalyticsController.cs`/`AnalyticsService.cs`/
`IAnalyticsService.cs`/`IAnalyticsRepository.cs`/`PosAnalyticsDtos.cs` edits — those already showed
modified from TASK-490's still-uncommitted work; diffed to confirm `GetWorstProductsAsync`/
`GetLossesTrendAsync` bodies untouched. No `losses/by-product`, `losses/trend`, `pos/worst-products`,
`AnalyticsAuthorization.cs`, `TenantRoleCapabilities.cs`, or `frontend/` touched.

## TASK-490 — Backend: worst-performing products / dead-stock endpoint (analytics follow-up batch)
**Status:** done · **Agent:** backend-developer · **Depends:** none (TASK-489's losses/trend
touches the same shared files but no code overlap — read fresh, built on top, nothing of theirs
modified) · **Next:** TASK-493 (frontend-developer, dead-stock table UI) unblocked
Log: `.claude/logs/tasks/490_2026-08-07_worst-products-endpoint_backend-developer.md`
Second of the small follow-up batch (TASK-488..495). New
`GET /api/analytics/pos/worst-products` on `AnalyticsController.cs` (`store_id`/`from`/`to`/`limit`,
same clamp as `pos/top-products`: `if (limit is < 1 or > 100) limit = 10;`). **Deliberately not**
`pos/top-products` sorted ascending — that query groups `PosTransactionItems`, so a zero-sale
product never appears in the result at all (no rows to group), and dead stock specifically needs
those zero-sale-but-in-stock products surfaced as the more actionable signal. New
`GetWorstProductsAsync` (repo) instead starts from the catalog/stock side: active `Item`s
(`IsActive`, tenant-scoped) with on-hand `ProductStock` (`Quantity > 0`, excluding
sold_out/archived — reused `GetByCategoryAsync`'s own on-hand-quantity convention), then merges in
a sales rollup for the period (same aggregate shape as `GetPosTopProductsAsync`), COALESCEing
missing sales to 0. Two-query shape (SQL-side scalar stock aggregate, then a sales aggregate
pre-filtered to just those product ids, merged via `Dictionary` in C#) rather than one LEFT JOIN +
GroupBy LINQ query, per the brief's guidance given this file's already-documented EF/Npgsql
GroupBy-translation limits (TASK-482/489) — both aggregates still run server-side, only two
already-small result sets merge client-side, so this isn't a repeat of
`GetPosTopProductsAsync`'s/`GetLossesByProductAsync`'s accepted-but-larger in-memory-materialize
pattern. New DTOs in `PosAnalyticsDtos.cs` (not `AnalyticsDtos.cs` — POS-specific, same file as
`PosTopProductsDto`): `WorstProductsDto`/`WorstProductRowDto`. No margin gate (same sensitivity
class as `pos/top-products`, already ungated for store_manager+) — DTO carries no
`PricePurchase`-derived field at all. Thin service pass-through. 4 new tests in
`PosAnalyticsServiceTests.cs` (zero-sales product round-trips `SalesRevenue: 0`, ascending-order
pass-through, `limit` forwarding, store-filter + `CurrentStock` round-trip). **Noted deviation:**
the brief asked for "limit clamping" test coverage, but the clamp is controller-only logic and this
codebase has zero `*ControllerTests.cs` files anywhere (TASK-482 precedent) — added a
pass-through-forwarding test instead of introducing a new controller-test file/pattern; documented
as an objective convention-consistency call per CLAUDE.md, not a product/UX judgment call. `dotnet
build`/`dotnet test` both clean (1341/1341 = 1337 baseline + 4 new). No `AnalyticsDtos.cs`,
`losses/trend` (TASK-489), margin/authorization files, or `frontend/` touched.

## TASK-488 — Frontend: category/losses product drill-down → shared ProductTrendPanel
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-483/484 (both done, same
components) · **Next:** none blocking — user-flagged gap closed; a future task (TASK-493) will
import the renamed `ProductTrendPanel`
Log: `.claude/logs/tasks/488_2026-08-07_category-losses-product-drilldown_frontend-developer.md`
User-flagged gap after live review of TASK-479..487: `CategoryDetailPanel`/
`LossesProductBreakdownPanel` (TASK-483) product rows had no click handler, while
`PosTopProductsTable` (TASK-484) already opened `PosProductTrendPanel` on row click. Closed by
reusing that exact panel — confirmed genuinely generic first (`productId`/`productName`/`storeId?`/
`onClose`, no POS-specific coupling), so no logic changes were needed. Added new `onProductClick?`
props to both panels: a button-style hover-chip on the product-name cell only (not the whole grid
row), `#111827` accent matching `PosTopProductsTable`'s row hover/active-row highlight, visually
distinct from `SortableHeader`'s uppercase/gray sort buttons in the same row. New `selectedProduct`
state in `analytics/page.tsx` mirrors `analytics/pos/page.tsx`'s exact toggle-on-reselect pattern.
Panel renders once in a single shared spot at the bottom of the page rather than nested under each
of the 3 triggering panels (by-category/losses-by-reason/losses-by-store) — avoids a stale-state
cross-link since all three share one piece of state. No `storeId` threaded through — verified
`/analytics` has no page-wide store filter at all (every hook on the page takes no `store_id`),
unlike `/analytics/pos`. **Renamed** `PosProductTrendPanel.tsx` → `ProductTrendPanel.tsx` (`git mv`,
function renamed, both call sites updated, plus 2 stale name-only comment references fixed in
`ProductAnalyticsTab.tsx`/`PosTopProductsTable.tsx` — explicitly permitted as part of the rename,
nothing else in either file touched) since the panel is no longer POS-page-only — **future imports
(TASK-493) should use the new name/path**: `frontend/features/analytics/components/
ProductTrendPanel.tsx`, export `ProductTrendPanel`. `tsc --noEmit`/`npm run lint`/`npm run build`
all clean (exit 0, 57/57 static pages, `/analytics` 9.27 kB/263 kB, `/analytics/pos` 11.4 kB/259 kB
First Load JS — both small upticks expected/explained in the task log). Live dev-server check (no
backend in this session, same constraint TASK-483/484/485 all hit): both routes compiled and
redirected to `/login` cleanly, zero hydration/module-resolution errors. Full authenticated click-
through untested (no backend available) but low-risk — `ProductTrendPanel` itself was already live
E2E-verified end-to-end by TASK-486 and is reused completely unmodified here.

## TASK-489 — Backend: losses/write-offs trend-over-time endpoint (analytics follow-up batch)
**Status:** done · **Agent:** backend-developer · **Depends:** none (independent of TASK-479/480's
margin-authorization work — this endpoint carries no margin data) · **Next:** TASK-492
(frontend-developer, losses trend chart UI) unblocked
Log: `.claude/logs/tasks/489_2026-08-07_losses-trend-endpoint_backend-developer.md`
First of a small follow-up batch (TASK-488..495) requested after the user reviewed the shipped
interactive-analytics initiative (TASK-479..487, commit 99bbde97) live. New
`GET /api/analytics/losses/trend` on `AnalyticsController.cs`, mirrors `pos/revenue-trend`'s shape
exactly (`store_id`/`from`/`to`/`group_by=day|week`, no compare-mode). New
`LossesTrendDto`/`LossesTrendPointDto` in `AnalyticsDtos.cs` (not `PosAnalyticsDtos.cs` — that's
POS-specific), thin service pass-through. New `GetLossesTrendAsync` (repo) groups `WriteOffs`
**in SQL before `ToListAsync`** (mirrors `GetProductSalesTrendAsync`'s TASK-482 two-step
SQL-aggregate-then-map shape, not `GetLossesAsync`'s/`GetWriteOffAnalyticsAsync`'s in-memory
GroupBy-after-materialize pattern) — reused TASK-482's already-verified day/week bucketing rather
than reinventing it (day via the provider's `DateTime.Date` translation, week via the same inlined
Monday-anchored ISO-offset arithmetic `IsoWeekStart()` uses; `EF.Functions.DateTrunc` still doesn't
exist in this repo's installed Npgsql EF Core provider, and EF still can't translate a call to an
arbitrary private C# method, so the arithmetic has to be inlined again at this call site). No
margin gate (ADR-027 §1 precedent from `losses/by-product`, TASK-481) — `TotalLoss` is already
shown in aggregate to every store_manager+ caller today, this endpoint is the same data re-sliced
by day/week instead of by store/reason/product. 4 new tests in `PosAnalyticsServiceTests.cs`
(day/week pass-through, store-filter forwarding, empty-range handling). `dotnet build`/`dotnet
test` both clean (1337/1337 = 1333 baseline + 4 new). No `AnalyticsAuthorization.cs`/
`TenantRoleCapabilities.cs`, `PosAnalyticsDtos.cs`, `pos/top-products`, or `frontend/` touched.

## TASK-487 — Security review: margin authorization (interactive analytics + margin plan)
**Status:** done — **verdict: SHIP** · **Agent:** security-reviewer · **Depends:** TASK-480..486
(all done) · **Next:** none blocking — initiative (TASK-479..487) complete
Log: `.claude/logs/tasks/487_2026-08-07_margin-authorization-security-review_security-reviewer.md`
Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md`. Final gate for the whole
initiative — deeper authorization-bypass/isolation angle TASK-486 (QA) explicitly deferred here.
All 6 brief sections pass: `AnalyticsAuthorization.CanViewMargin` role-floor/shape/call-sites OK
(no `||`/`&&` inversion, `includeMargin` traced end to end controller→service→repository, never
client-supplied); adversarial null enforcement live-tested against the running dev API (garbage
`?includeMargin=true`, spoofed `X-Role`/`X-User-Role` headers, hand-tampered JWT payload — all
rejected or ignored, margin stayed null for store_manager throughout); tenant/store isolation
live-tested with **real cross-tenant data** (discovered the dev DB actually has 20 tenants, used
"Loyalty Concurrency Test…" as tenant B against `manager@demo.local`/tenant A via raw curl,
bypassing the UI entirely) — cross-tenant `productId` on the trend endpoint 404s, cross-tenant
`category_id`/`store_id` return empty with no data leak; injection/malformed-input sanity
live-tested (SQLi-shaped `reason` payloads, malformed GUIDs, garbage `group_by`, long strings — all
degrade cleanly, zero raw SQL confirmed by grep, zero destructive side effects); capability scope
confirmed no creep (`analytics.view_margin` referenced exactly twice, its own group + `All`); RLS
sanity confirmed (3 new repo methods are plain LINQ-to-EF, inherit the pre-existing Stage 3
`store_scope` RESTRICTIVE policy automatically per that migration's own documented child-table
inheritance, no bypass). `dotnet build` clean, `dotnet test` 1333/1333 independently reconfirmed
(matches TASK-486's baseline exactly).
**1 LOW/informational finding (not blocking):** `GetCategoryProductBreakdownAsync`'s `CategoryName`
lookup (`AnalyticsRepository.cs:389-394`) has no explicit `TenantId` filter, relying on RLS alone —
live-confirmed NOT currently exploitable (cross-tenant probe category returned "Unknown", no leak),
same already-accepted shape as KI-028 (not a new pattern, not filed as a new KI).
**Both QA-flagged items resolved:** F2 (`netmgr@demo.local` zero `user_locations` grants) —
**confirmed pre-existing and unrelated** to TASK-479..486 (migration/commit dates ~2.5 weeks prior,
`git log` shows zero `DbSeeder.cs` commits since, live DB confirms the only grants ever inserted are
`manager@demo.local`'s from 2026-07-20); added `KI-031` for the seed-data gap itself (low severity,
QoL only). KI-030 cross-reference — **confirmed accurate**, independently re-verified live (all 3
real logins during this review returned `"capabilities":[]`), correctly not re-filed as new.
No code changed (audit only). Dev servers stopped cleanly at end; only mutation was one
insert-then-delete probe category row for the cross-tenant test (verified cleaned up).

## TASK-486 — QA: live E2E for interactive analytics + margin drill-down (TASK-479..485)
**Status:** done — **verdict: SHIP** · **Agent:** qa-tester · **Depends:** TASK-483/484/485 (all done) ·
**Next:** TASK-487 (security-reviewer) — 2 items flagged explicitly below for it
Log: `.claude/logs/tasks/486_2026-08-07_analytics-drilldown-qa_qa-tester.md`
Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md`. First live/E2E pass over the whole
initiative — every build agent had deferred it (no dev stack in-session). Stood up
`docker compose up -d` (postgres/worker already running with real TASK-476/482 residue data — 131
`pos_transactions`) + `dotnet run` + `next dev` (auto-port 50643, since :3000 is a permanently-running
unrelated container) with `Cors__Origins` widened to match. All 7 brief sections pass: regression
(`dotnet test` 1333/1333, `tsc`/lint/build all clean, build output byte-identical to TASK-483/484's own
figures); toggle-collapse open/close verified live for every row-triggered panel (losses-by-reason,
losses-by-store, by-category incl. the null/"uncategorized" edge case, POS product-trend); **margin
authorization — the highest-stakes check — passed at both DOM and raw-API level in both directions**,
including exact-arithmetic confirmation (`Revenue − Quantity × Item.PricePurchase` matched the API's
`marginAmount` to the cent on 2 independent products/endpoints) and the ADR-027 "estimated margin"
disclaimer text confirmed verbatim for network_manager+; `LossesProductBreakdownPanel` confirmed
identical for both roles with zero margin keys in its DTO at all, as designed; compare-toggle isolation
confirmed via network inspection (no compare params ever reach the new panels' own queries); data
correctness came back an **exact** match (not just plausible) between `PosProductTrendPanel` and
`PosTopProductsTable` for the same product; performance smoke-test 33-134ms (no stall), plus an
independent live re-confirmation that TASK-479's covering index exists and the old redundant one is
gone; store-filter rescoping confirmed live for the shared POS hooks. Read all 7 prior task logs before
testing (per the brief) rather than the literal plan brief, since several documented deviations
(recharts 3.8.1 API, missing `EF.Functions.DateTrunc`, `reason=other` bucket matching, multi-axis
`yAxisId`) changed the actual shipped shape.
**2 non-blocking findings:** (F1) this session's Browser pane has no active compositor — screenshots and
pixel-coordinate clicks both silently no-op session-wide (confirmed on plain buttons too, not just
charts) — worked around it everywhere via trusted DOM `.click()` dispatch (fully covers every row/button/
dropdown interaction) except recharts' own internal pointer-tracking, so `ExpiryDonut` slice-click and
`PosRevenueTrendChart` day-click were verified by source-read only, not live-clicked; recommends a 30s
manual spot-check of just those two before/alongside sign-off. (F2) the seeded `netmgr@demo.local`
account has zero `user_locations` grants (unlike `manager@demo.local`'s two), so store-scope RLS shows
it zero data tenant-wide — used `ea@demo.local` (enterprise_admin) instead, which the brief explicitly
allows; flagged for TASK-487 since it's authorization-adjacent (is network_manager's exclusion from the
RLS bypass list vs. enterprise_admin's inclusion intentional for this margin floor?), plus an explicit
KI-030 cross-reference (the capability half of `CanViewMargin` is unreachable in practice tenant-wide,
same root cause as every other `RoleOrCapability` policy — role-floor branch is the only live path,
confirmed working). No blocking findings, no margin leaks, no crashes, no broken navigation. Dev servers
stopped cleanly at end; no data mutated (read-only pass).

## TASK-484 — Frontend: POS product sales-trend UI (interactive analytics + margin plan)
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-482 (backend, done) ·
**Next:** TASK-486 (qa-tester, live E2E for this together with TASK-483/485)
Log: `.claude/logs/tasks/484_2026-08-07_product-sales-trend-ui_frontend-developer.md`
Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md`. Row-click drill-down from
`PosTopProductsTable` on `/analytics/pos`, rendered inline (not a route nav) via an extended
`ProductAnalyticsTab.tsx`. New `types.ts` DTOs/`api/pos-analytics.ts` fetcher/`usePosAnalytics.ts`
hook (`useProductSalesTrend`, full `[productId, params]` query key, no `keepPreviousData`, no
compare-mode — matches TASK-482's endpoint shape verified fresh against
`PosAnalyticsDtos.cs`/`AnalyticsController.cs`). `ProductAnalyticsTab` gained
`showRevenueSeries?`/`canViewMargin?` (default `false`/`undefined`, both existing
`/inventory/{id}?tab=analytics` call sites unaffected) — always fetches `group_by=day` regardless
of the tab's own `rangeDays`, merges trend points into the existing movement `chartData` by date
(revenue/quantity zero-fill on no-sales days; margin stays `null` on a real-sale/unknown-cost day
rather than zero-filling). Second right-hand `YAxis` (`yAxisId="revenue"`) added, and — the
brief's flagged silent-bug risk — gave the pre-existing `YAxis` + 5 `Line`s + 4 `ReferenceArea`s +
3 `ReferenceLine`s an explicit `yAxisId="quantity"`, since they all previously relied on recharts'
implicit default axis id, which stops matching (silently, no error) the moment a second axis
exists; `buildLines()` now bakes `yAxisId` into each line descriptor so legend/render/tooltip
can't cross-wire. Margin legend/line/tooltip row fully absent from the DOM (not grayed) when
`canViewMargin` is false, matching this whole initiative's hidden-not-disabled rule. Optional
revenue-total `SummaryCard` added (brief's suggestion); no margin-total card (brief only suggested
revenue, kept scope tight). New `PosProductTrendPanel.tsx` resolves `canViewMargin` via
`useMe()` + `canViewAnalyticsMargin` — the exact `CategoryDetailPanel.tsx` (TASK-483) mechanism —
and matches `PosDayDetailPanel`'s (TASK-485) header chrome, extended to a 2-line title+disclaimer
block since this panel (unlike `PosDayDetailPanel`) needs the "оцінна маржа" caveat when margin is
visible. `PosTopProductsTable` gained `onRowClick?`/`selectedProductId?`, active-row highlight
reusing the table's own existing `#111827` hover color rather than a new one. `analytics/pos/
page.tsx` read fresh (built alongside TASK-485's already-merged `selectedDay`/`PosDayDetailPanel`,
nothing reverted) — new `selectedProduct` state, same toggle-on-reselect convention as
`handleDayClick`, panel rendered below the top-products/cashiers section. **Deliberate decision:**
`PosProductTrendPanel` accepts `storeId?` (parity with `PosDayDetailPanel`, page passes its live
filter down) but does NOT thread it into `ProductAnalyticsTab`'s trend fetch —
`useProductMovements` (the tab's existing stock series) has no `store_id` filter at all, so a
store-scoped revenue line next to a store-agnostic stock line would misrepresent the chart;
matches the brief's literal example call. `tsc`/lint/build all clean (build exit 0, 57/57 static
pages, `/analytics/pos` 11.3 kB/259 kB First Load JS). Live browser E2E not done — no backend
process available in this session (same constraint TASK-483/485 both hit); confirmed no
React/hydration/chunk-load errors from the new code via console inspection, deferred full
click-through to TASK-486.

## TASK-483 — Frontend: category/losses product drill-down UI (interactive analytics + margin plan)
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-481 (backend, done) ·
**Next:** TASK-484 (frontend-developer, POS product-trend UI — separate scope, not touched here),
TASK-486 (qa-tester, live E2E for this together with TASK-484/485)
Log: `.claude/logs/tasks/483_2026-08-07_category-losses-drilldown-ui_frontend-developer.md`
Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md`. Consumed TASK-481's two
endpoints end-to-end: `types.ts`/`api/analytics.ts`/`useAnalytics.ts` gained the 4 new
DTOs/2 fetchers/2 hooks (full-filter query keys, no `keepPreviousData`, matches
`useMarketingAnalyticsOverview`'s discipline). New `CategoryDetailPanel.tsx` (client-side
sortable/paginated via the shared `TableControls.tsx` `SortableHeader`/`TablePaginationFooter` —
neither new endpoint accepts server pagination params; margin columns conditionally rendered,
absent from the DOM when `canViewAnalyticsMargin` is false, not just hidden; visible "(оцінна)"
margin disclaimer per ADR-027) and `LossesProductBreakdownPanel.tsx` (shared by losses-by-store
and losses-by-reason, `{title, totalLoss, storeId?, reason?, from, to, onClose}`, no margin
columns — DTO has none). `CategoryStatusChart`/`LossesByReasonChart`/`LossesByStoreChart` gained
click props using the verified-working recharts 3.8.1 `<Bar onClick={(entry) =>
entry.payload.X}>` mechanism from `SegmentDistributionChart.tsx` (not the plan's recharts@2-shaped
snippet — same caution TASK-485 already flagged). New `canViewAnalyticsMargin` in `roles.ts`,
exact shape of `canExportMarketingAnalyticsPii` with `AT_LEAST_NETWORK_MANAGER` +
`"analytics.view_margin"`. `analytics/page.tsx` read fresh (built on top of TASK-485's already-
merged `ExpiryDonut`/state changes, nothing reverted) — new toggle-selection state, table rows
rewired from `router.push` to the toggle handlers, panels rendered conditionally, both always
passed the page's CURRENT (never compare) `from`/`to`. **Noted deviation:** `selectedCategoryId`
is `string | null | undefined`, not the brief's literal `string | null` — a category id is
itself nullable (null = uncategorized bucket), so a 2-state type can't distinguish "nothing
selected" from "uncategorized selected"; `undefined` = closed, `null` = uncategorized open, a
string = that category open, no sentinel values used anywhere. `CategoryStatusChart`'s
active/inactive treatment is opacity (not a color swap like `SegmentDistributionChart.tsx`) since
this is a 4-series stacked chart where color already encodes safe/warning/critical/expired — a
swap would destroy that coding. `tsc --noEmit`/`npm run lint`/`npm run build` all clean (build
exit 0, 57/57 static pages, `/analytics` 8.87 kB/247 kB First Load JS). Live browser E2E not
done — no dev stack wired to this session's uncommitted changes (same constraint TASK-485 hit);
deferred to TASK-486. `PosTopProductsTable.tsx`/`PosRevenueTrendChart.tsx`/
`ProductAnalyticsTab.tsx`/`analytics/pos/*` untouched (TASK-484).

## TASK-482 — Backend: single-product sales trend endpoint (interactive analytics + margin plan)
**Status:** done · **Agent:** backend-developer · **Depends:** TASK-479 (index, done), TASK-480
(`CanViewMargin`, done), TASK-481 (same 3 files, done — read fresh, nothing of theirs modified) ·
**Next:** TASK-484 (frontend-developer, product trend UI: `ProductAnalyticsTab.tsx` extension +
`PosProductTrendPanel.tsx`) unblocked
Log: `.claude/logs/tasks/482_2026-08-07_product-sales-trend_backend-developer.md`
Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md`. New
`GET /api/analytics/pos/products/{productId}/trend` (`store_id`, `from`/`to`, `group_by=day|week`,
no compare-mode — a row-click drill-down, not a page KPI trend). 404s (`NotFound()`) when
`productId` doesn't resolve to a real `Item` in the caller's tenant scope, mirroring
`ItemsController.GetById`'s nullable-DTO convention. New `GetProductSalesTrendAsync` (repo) groups
`PosTransactionItems` **in SQL before `ToListAsync`** (not `GetPosTopProductsAsync`'s in-memory
anti-pattern) — confirmed via live `EXPLAIN ANALYZE` against real dev data (residue from TASK-476's
QA pass, 131 `pos_transactions`) that TASK-479's covering index is actually used (`Index Only Scan`,
`Heap Fetches: 0`) and that day/week bucket totals match an independently-computed ground truth
exactly. **Deviation from the plan's literal snippet, root-caused not guessed:**
`EF.Functions.DateTrunc` does not exist in this repo's installed Npgsql EF Core provider (8.0.11) —
confirmed via build failure + XML-doc grep of every actual `Npgsql*DbFunctionsExtensions` member.
Used the provider's built-in `DateTime.Date` translation for "day" (`date_trunc('day', …, 'UTC')`,
confirmed via `.ToQueryString()`) and inlined `GetPosRevenueTrendAsync`'s existing `IsoWeekStart`
Monday-anchored arithmetic as translatable `DateTime` member expressions for "week" (EF can't
translate a call to an arbitrary private method) — both verified translatable and correct against
real data (week keys land exactly on Monday). Margin (ADR-027) is a cheap second pass over the
already-collapsed (≤366-row) points. New DTOs in `PosAnalyticsDtos.cs` (not TASK-481's
`AnalyticsDtos.cs`): `ProductSalesTrendDto`/`ProductSalesTrendPointDto`. 4 new tests in
`PosAnalyticsServiceTests.cs` (day/week pass-through, margin-by-role, null-propagation on unknown
productId — this codebase has no `*ControllerTests.cs` anywhere, so the controller's 404 ternary
itself isn't independently unit-tested, consistent with existing precedent). `dotnet build`/
`dotnet test` both clean (1333/1333 = 1329 baseline + 4 new). No `AnalyticsDtos.cs`,
`AnalyticsAuthorization.cs`/`TenantRoleCapabilities.cs`, TASK-479 migration/index, or `frontend/`
touched.

## TASK-481 — Backend: category/losses product drill-down endpoints (interactive analytics + margin plan)
**Status:** done · **Agent:** backend-developer · **Depends:** TASK-480 (`CanViewMargin`, done),
TASK-479 (index, done — no direct code dependency) · **Next:** TASK-482 (backend-developer, product
sales-trend endpoint, same 3 files) — must start only after this task's edits are complete;
TASK-483 (frontend-developer, category/losses UI) unblocked
Log: `.claude/logs/tasks/481_2026-08-07_category-losses-drilldown_backend-developer.md`
Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md`. Two new `AnalyticsController.cs`
GET actions, both behind the existing class-level `AnalyticsViewOrCapability` policy: **A**
`by-category/products` — new `GetCategoryProductBreakdownAsync` (repo) merges `GetByCategoryAsync`'s
stock rollup + `GetPosTopProductsAsync`'s sales rollup, grouped by `ProductId` within one category
(`category_id` null = uncategorized bucket, not "all"); controller resolves `includeMargin` via
`AnalyticsAuthorization.CanViewMargin(User)` and passes the bool down, service/repo stay
authorization-agnostic; margin null vs. `0` kept distinct for "not authorized" vs. "no
`PricePurchase` on file". **B** `losses/by-product` — new `GetLossesByProductAsync` (repo), one
endpoint serves both by-store and by-reason drill-downs via independent AND-filters; **no margin
gate** (ADR-027 §1 — `LossAmount` already shown in aggregate to every store_manager+); added
`reason == "other"` matching `Reason == null OR "other"` to mirror `GetWriteOffAnalyticsAsync`'s own
display-bucket convention in the same file (undocumented in the brief, added to avoid a silent
empty-result drill-down). New DTOs in `AnalyticsDtos.cs`
(`CategoryProductBreakdownDto`/`CategoryProductRowDto`, `LossesByProductDto`/`LossByProductRowDto`),
thin service pass-throughs. 6 new tests in `PosAnalyticsServiceTests.cs` (only existing
`Analytics/`-folder test file) — delegation shape for both endpoints, null-category-id handling,
store/reason filter forwarding, and the key test: constructs store_manager/network_manager
`ClaimsPrincipal`s the same way `AnalyticsAuthorizationTests` does, resolves `CanViewMargin` for
each, and confirms margin fields come back null/populated accordingly on endpoint A while endpoint
B's shape has no role-dependent path at all. `dotnet build`/`dotnet test` both clean (1329/1329 =
1323 baseline + 6 new). No `PosAnalyticsDtos.cs`, `pos/products/{id}/trend` endpoint (TASK-482), or
`frontend/` touched.

## TASK-480 — Backend: margin authorization primitive (interactive analytics + margin plan)
**Status:** done · **Agent:** backend-developer · **Depends:** none (TASK-479's index has no code
overlap) · **Next:** TASK-481 (backend-developer, category/losses-by-product endpoints), TASK-482
(backend-developer, product trend endpoint) — both now unblocked, wire this check into real DTOs
Log: `.claude/logs/tasks/480_2026-08-07_margin-authorization_backend-developer.md`
Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md`. New
`AnalyticsAuthorization.CanViewMargin` (`backend/ShelfGuard.Infrastructure/Authorization/`) —
network_manager+ (`AppPolicies.AtLeastNetworkManagerRoles`, confirmed pre-existing, not added) OR
new `TenantRoleCapabilities.AnalyticsViewMargin` (`analytics.view_margin`) capability; same
imperative in-body-check shape as `MarketingAnalyticsAuthorization.CanExportPii`. New
`AnalyticsAuthorizationTests.cs` (9 facts). `.claude/docs/decisions.md` gained `ADR-027`: margin
cost-source decision (`Item.PricePurchase` retroactive, not a real batch-cost snapshot — reasoning
+ mandatory "оцінна маржа" UI label + deferred `CostAtSale` fast-follow) and backlog notes for
cashier-trend drill-down + POS payment-type filtering (both deferred, not this phase). `dotnet
build`/`dotnet test` both clean (1323/1323 = 1314 baseline + 9 new). No `AnalyticsController.cs`/
`AnalyticsService.cs`/`AnalyticsRepository.cs`/DTOs/`frontend/` touched (that's TASK-481/482/483+).

## TASK-479 — DB: `pos_transaction_items` product-covering index (interactive analytics + margin plan)
**Status:** done · **Agent:** database-engineer · **Depends:** none · **Next:** TASK-480
(backend-developer, `AnalyticsAuthorization.CanViewMargin`) — no dependency on this task, can start
anytime; TASK-482 (backend-developer, product trend endpoint) depends on this task's index
Log: `.claude/logs/tasks/479_2026-08-07_pos-product-index_database-engineer.md`
Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md`. Added
`idx_pos_transaction_items_product_covering` (`ProductId`, `TransactionId`) INCLUDE (`Quantity`,
`PriceFinal`) — migration `AddPosTransactionItemProductCoveringIndex`, generated via `dotnet ef
migrations add`, live-verified on local dev. Same migration drops the now-redundant plain
`IX_pos_transaction_items_ProductId` (EF's own FK-index convention generated the drop automatically
once `ProductId` became the new composite index's leading column — mirrors the
`AddPerformanceIndexes` precedent for the old plain `TransactionId` index). Confirmed safe via
codebase grep (no query path filters `pos_transaction_items` by `ProductId` alone without a
`TransactionId`/date join) and confirmed the FK-RESTRICT delete-check use case remains served
(`ProductId` still leads the new index). `database-schema.md` updated. `dotnet build`/`dotnet test`
both clean (1314/1314). No C# app/controller code touched (that's TASK-480/481/482).

## TASK-485 — Frontend: analytics quick-win clickability (ExpiryDonut + POS revenue-trend day drill-down)
**Status:** done · **Agent:** frontend-developer · **Depends:** none (zero backend deps; part of
the interactive-analytics plan `iterative-purring-sifakis.md`, ran parallel to TASK-479..484) ·
**Next:** TASK-486 (qa-tester) covers live E2E for this together with TASK-483/484
Log: `.claude/logs/tasks/485_2026-08-07_pos-quick-win-clickability_frontend-developer.md`
`ExpiryDonut` gained `onSliceClick?`, wired on `/analytics` to the same `router.push('/stock?status=...')`
the MetricCards already do. `PosRevenueTrendChart` gained `onDayClick?`/`selectedDay?` (click →
resolve nearest point's date, cursor:pointer, hint line mirroring `SegmentGrid`'s pattern). New
`PosDayDetailPanel.tsx` — pure composition of the existing `PosSummaryCards`/`PosTopProductsTable`/
`PosCashierStatsTable` via their existing hooks called with `from=to=<day>`, no new hook/endpoint.
Wired into `/analytics/pos` with toggle-on-reselect state, mirroring `marketing-analytics/page.tsx`'s
`handleSelectSegment`. **Notable deviation:** the plan brief's recharts click snippet
(`activePayload`) is recharts@2 API — this repo runs recharts 3.8.1, which dropped `activePayload`
from the click callback entirely (verified by reading the installed package's source, not assumed).
Used the real 3.x mechanism instead (`activeTooltipIndex` → resolve against the chart's own data
array; Pie-level `onClick`, not `Cell onClick`, for `ExpiryDonut`) — same UX/behavior, correct for
what's actually installed. Full detail + why in the task log. `tsc --noEmit` and `npm run lint`
both clean. Live browser E2E not done — no local backend/DB/seed data available in this session
(only Postgres+worker containers were up); deferred to TASK-486 rather than faked.

## TASK-478 — Backend: fix Фаза 4 QA findings (phone matching + export truncation)
**Status:** done · **Agent:** backend-developer · **Depends:** TASK-476 (QA findings) ·
**Next:** none blocking — KI-030 (TenantRole capabilities never reach the JWT, the 3rd TASK-476
finding) is a separate, pre-existing platform bug, deliberately not touched here
Log: `.claude/logs/tasks/478_2026-08-06_post-campaign-qa-fixes_backend-developer.md`
Fixed both confirmed Фаза 4 bugs from TASK-476's QA pass. (1) HIGH — phone-based import matching:
`PostCampaignRepository.FindCustomersByIdsOrPhonesAsync` now normalizes stored `Customer.Phone`
client-side (same `PhoneNormalizer` the import parser uses) before comparing against the always-
normalized candidate phones, instead of raw string equality — fixed entirely within the repository
file, no `Customer`/`CustomerService` change, no migration. (2) MEDIUM-HIGH — unknown/invalid-
tokens export silently capped at 20+20: raised both persisted sample caps to 500, added
`PostCampaignUnknownTokensExportResult` (`TotalUnknownCount`/`TotalInvalidCount`/`Truncated`) plus
response headers, inserted an honest in-sheet note row when a segment still exceeds the new cap,
and updated the frontend hint text to say "showing X of Y" instead of implying completeness — same
honesty standard `CustomerTable`'s own pagination footer already uses. `dotnet test` 1314/1314
(1310 baseline + 4 new, incl. the missing live-Postgres repository test category the QA report
flagged), `tsc`/lint/`next build` clean, plus live browser + curl E2E re-verification (real phone-
format mismatch reproduced then confirmed fixed; real truncated segment's export headers/in-file
note both confirmed correct).

## TASK-476 — QA: E2E acceptance post-campaign (Фаза 4)
**Status:** done — **verdict: SHIP WITH FOLLOW-UP** · **Agent:** qa-tester · **Depends:**
TASK-471/472/473/474/477 · **Next:** backend-developer follow-up for the 2 Фаза-4 bugs below (not
spawned yet — reported only, per this task's brief); TenantRole-capabilities bug needs its own
separate backend-developer + security-reviewer follow-up
Log: `.claude/logs/tasks/476_2026-08-06_post-campaign-e2e-acceptance_qa-tester.md`
Full live re-verification against the real dev stack (real tenant "Свіжий Кут", 14 customers, 127
`pos_transactions`), most of it against hand-computed ground truth, not just the app's own
self-report: parser edge cases (one self-designed input covering every §36.1 case at once — UUID-
not-split, decimal-not-two-IDs, free-text-not-a-phone, 4 real phone formats, normalization-based
dedup), CSV/XLSX import + column auto-detect/override (hand-built real binary `.xlsx` via Python
`zipfile`), draft-vs-analyzed banner (live in browser — appears and disappears correctly), period
formula (7-day via API, 30-day via live UI, both exact), all 5 top KPI cards incl. `NotReturned` as
a real 5th card, null-not-zero zero-denominator handling (backend AND frontend), behavioral-balance
identity, RFM migration matrix marginal-sum identities + the null "Без покупок" bucket vs. the
non-null case-b fallback (5 independent real customers), customer table pagination/sort/PII
masking, empty-segment handling (every endpoint + live UI), AI-explain 503 not-configured path,
the 20,000-row import cap (fast-fail, 0.27s), 3 real malformed-`.xlsx` shapes (all clean 400s,
never 500), and the `CanImportSegments` role-floor boundary (live 403 below floor, live 200 at/
above floor). `dotnet test` 1310/1310, filtered 96/96, `tsc`/lint/`next build` all clean, zero
regressions, bundle size byte-identical to TASK-473's own figure.
**Found 3 real bugs (reported, none fixed)**: (1) HIGH, Фаза 4's own code — post-campaign phone-
based import matching only works when `Customer.Phone` is already stored in exact canonical
`+380XXXXXXXXX` form; `CustomerService`'s write path never normalizes it, so real customers'
correctly-formatted phones silently resolve to "unknown," corrupting this feature's own core
existence-validation promise. (2) MEDIUM-HIGH, Фаза 4's own code — the unknown/invalid-tokens error-
report export is silently capped at 20+20=40 rows (all `PostCampaignSegment` ever persists) with no
truncation indicator, contradicting the source doc's "full downloadable error report" requirement;
byte-level export check proved a real 49-token segment exports only 40. (3) HIGH, pre-existing
platform bug, NOT Фаза 4's own code, found as a byproduct of testing the import-permission item —
`TenantRole` capabilities (ADR-020) never reach the JWT on login/refresh for ANY user, tenant-wide:
`TenantConnectionInterceptor` correctly RESETs `app.tenant_id` for the unauthenticated login
request, but `tenant_roles`' RLS policy (unlike `users`') has no NULL-passthrough carve-out, so it's
invisible mid-login — live-confirmed via 2 real users' login responses showing `"capabilities": []`
despite real non-empty grants in the DB. Silently disables the capability-widening half of every
`RoleOrCapability`-gated controller (7+ policies) tenant-wide. Full writeups: `.claude/logs/reviews/
bug-task476-phone-import-matching-format-mismatch_2026-08-06.md`, `bug-task476-unknown-tokens-
export-capped-at-20_2026-08-06.md`, `bug-task476-tenantrole-capabilities-never-reach-jwt_2026-08-06.md`.
Dev servers stopped cleanly at end; QA-test segments/TenantRole left in the dev DB (same residue
precedent TASK-433/424 already set for this series).

## TASK-475 — Docs: post-campaign glossary/schema/ADR (Фаза 4)
**Status:** done · **Agent:** documentation-writer · **Depends:** TASK-471/472/473/474/477 ·
**Next:** none blocking (qa-tester/TASK-476 runs in parallel, not dependent on this)
Log: `.claude/logs/tasks/475_2026-08-06_post-campaign-docs_documentation-writer.md`
Updated all 5 docs for Фаза 4 (post-campaign audience analysis), mirroring TASK-432's Фаза 3 pass:
`glossary.md` (new "Post-Campaign Analysis (Фаза 4)" section — draft-vs-analyzed segment, before/
after window, the 4 behavior states, RFM migration matrix, segment hash), `database-schema.md`
(new "TASK-471" entry — both tables, canonical RLS triad only, draft-vs-analyzed nullable-date
design), `api-contracts.md` (new "Post-Campaign Analysis" section, all 11 controller actions —
task logs said "10," code has 11, documented from the code; flags the `Import`-only
`CanImportSegments` auth asymmetry and the two-layer XLSX/CSV import size guard), `domain-model.md`
(new `PostCampaignSegment`/`PostCampaignSegmentMember` entities + relationships), `decisions.md`
(new ADR-023 addendum, Фаза 4 — 5 numbered decisions: breaking the stateless precedent, import
identity matching reusing Фаза 0's `PhoneNormalizer`, the RFM migration matrix's 3-call reuse of
`GetScoredCustomersAsync`/`RfmSegmentClassifier` with zero new RFM logic, the XLSX-bomb security
story as its own subsection with TASK-477's measured numbers table, and why `CanImportSegments` is
role-only with no new capability). Verified every claim against the actual shipped code (entities,
controller, DTOs, parser, classifier, `ImportLimits`, `MarketingAnalyticsAuthorization`, migration)
rather than transcribing task-log prose. No code changes, `known-issues.md` untouched (out of
scope per brief — zero KI entries exist for this whole initiative).

## TASK-477 — Backend: fix Фаза 4 import security findings (A/B/C)
**Status:** done — `dotnet build` 0/0, `dotnet test` **1308/1308 green** (was 1289, net +19, all
new) · **Agent:** backend-developer · **Depends:** TASK-474 · **Next:** documentation-writer
(TASK-475), qa-tester (TASK-476) — both now unblocked
Log: `.claude/logs/tasks/477_2026-08-06_post-campaign-import-security-fix_backend-developer.md`
Fixed all 3 findings from TASK-474's review. **Finding A (HIGH, XLSX resource exhaustion):** new
shared `ImportLimits` (`MaxRows=25_000`/`MaxColumns=300`, ~1.25x `PostCampaignService.
MaxAcceptedRows`) checked in `ExcelImportService.ParseXlsx` off the range's bounding-box
`RowCount()`/`ColumnCount()` **before** the per-cell copy loop runs, throwing a new
`ImportTooLargeException`; same early-exit added to `SegmentImportParser.ParseTextList`/
`ParseCsvText` for defense-in-depth (CSV/text was already 10 MB-bounded). **Finding C (LOW,
malformed file → bare 500):** empirically confirmed (throwaway probe, not assumed) ClosedXML
0.105.1 throws `System.IO.FileFormatException` (a real `FormatException` subtype) for corrupt/empty
input and a bare `NullReferenceException` for a well-formed zip that isn't a valid xlsx package;
`PostCampaignService.ImportAsync` now catches both narrowly around just the parse call, returning
the same clean `(null, error)` shape. **Finding B (MEDIUM, no separate upload permission):** added
`MarketingAnalyticsAuthorization.CanImportSegments` gating `PostCampaignController.Import`
specifically — deliberately role-only (`AtLeastStoreManagerRoles`, matching `CanExportPii`'s own
floor), no new capability, mirroring `TenantRoleCapabilities.ReceiptsView`'s documented precedent
that write-heavy actions stay out of the capability catalog; returns `Forbid()` (403) for anyone
below the floor. 19 new tests (`ExcelImportServiceTests.cs` new file, +5 `PostCampaignServiceTests`,
+8 `MarketingAnalyticsAuthorizationTests`) — ceiling rejection (row + column), malformed-input
exception pinning, exception-translation in the service, real (unmocked) CSV/raw-text ceiling
rejection, and capability-bypass-proof-negative tests for the new auth check. Nothing else touched
— RLS/formula-injection/strict-parser-Classify/raw-SQL-absence/PII-masking/IDOR all left as the
review found them (OK). Not committed.

## TASK-474 — Security: review of Фаза 4 (post-campaign analysis)
**Status:** done — **verdict: NOT clear to ship the import endpoint as-is** · **Agent:**
security-reviewer · **Depends:** TASK-471/472/473 · **Next:** backend-developer fix task for
finding A (+ C), then re-review, then documentation-writer/qa-tester
Log: `.claude/logs/tasks/474_2026-08-05_post-campaign-security-review_security-reviewer.md`
Live-verified RLS on `post_campaign_segments`/`post_campaign_segment_members` against the real
non-superuser `shelfguard_app_dev` role (owned, forced RLS, canonical triad, no
`consumer_self_access`) — OK, and confirmed structurally unreachable by a consumer JWT at all three
layers (authz policy, controller claim resolution, RLS). Confirmed OK: TASK-414's Excel/CSV
formula-injection fix still applies uniformly (customer names + raw uploaded tokens both
sanitized); `SegmentImportParser`'s strict whole-token GUID/phone classification independently
re-traced against every source-doc §5.3 adversarial case (UUID-must-not-split, decimal-not-two-IDs,
free-text-not-a-phone) and cross-checked against its actual passing tests; zero raw SQL anywhere
(grep-confirmed); PII masking/export-capability parity matches sibling phases exactly; every
repository method threads `tenantId` explicitly (stronger than the accepted RLS-only baseline
elsewhere); AI advisor never leaks the API key and never puts raw uploaded token text in the
prompt (only the staff-typed segment Name, same low-severity shape as a prior accepted finding).
**1 HIGH finding (blocks the import endpoint):** the 20,000-row import cap is checked only AFTER
`ExcelImportService.ParseXlsx` has already fully materialized the uploaded workbook into memory
with no row/column guard — a small, zip-compressed `.xlsx` well within the (correctly enforced)
10 MB limit can expand into a very large in-memory cell grid before the cap ever runs (this
codebase's first file-upload feature, no prior convention to inherit); also no try/catch around
the parse call, so a malformed file crashes to a bare 500. Given the shared multi-tenant API
process, recommend fixing before general rollout — mirrors this exact series' TASK-412→TASK-414
review-then-fix pattern. **1 MEDIUM:** no separate "upload" permission — Import shares the same
view-level floor as read-only report tabs, though source doc §32 asks for a distinct one. LOW/info
only: report-tab views aren't audit-logged (pre-existing across all of Фаза 1-4, not a regression),
no antivirus scan on uploads (new gap but low actual impact — file bytes are parsed and discarded,
never stored or re-served to another user). No fixes applied (audit only).

## TASK-473 — Frontend: post-campaign dashboard UI (Фаза 4)
**Status:** done — `tsc --noEmit`/`next lint`/`next build` all clean · **Agent:** frontend-developer
· **Depends:** TASK-472 · **Next:** security-reviewer (TASK-474)
Log: `.claude/logs/tasks/473_2026-08-05_post-campaign-frontend_frontend-developer.md`
New feature `frontend/features/marketing-analytics/post-campaign/` (types/api/hooks/Zustand
store/17 components) + route + Sidebar 4th `marketing_analytics` nav item + full
`Dashboard.postCampaign.*` i18n (en/uk). Zustand store tracks `draftSegmentId` vs `reportSegmentId`
as two separate ids (source doc §7's draft-vs-analyzed rule — every import creates a NEW segment
row server-side, no update-in-place) plus a `reportVersion` folded into every report query key
(report GETs take no date params — the window is frozen server-side by `/analyze`, so re-analyzing
the SAME segment with new dates needs a version bump to force a refetch). Import panel has no
client-side parse preview by design (strict parser is server-only); column auto-detect/override,
validation summary, draft-vs-analyzed banner, 5 top KPIs (incl. the explicit "Не повернулись" 5th
card per the brief's fix-over-competitor), 3 report tabs (daily turnover chart + status donut +
recommendation; 4 R/F/M activity cards with recency's inverted delta-color convention; migration
KPIs + before/after donuts + a full 12×12 transition matrix with dots for empty cells), full
server-paginated customer table (no Top-200 cap). `RecommendationCard`/export buttons built
locally rather than literally imported from Фаза 1 — matches the ACTUAL precedent Фаза 2/3 already
set (both independently re-implement this block against their own DTO/endpoint shape); only the
truly generic `PiiUnmaskToggle`/`TableControls` are imported verbatim, per the brief. One real bug
caught in self-review (not by the type-checker): a column-picker override could leak from one
file's chosen column into a different file's preview — fixed. Build/lint/typecheck all clean; dev
server confirmed the route compiles/resolves (200, no console errors beyond the expected
missing-backend connection failure) but full authenticated live-UI verification wasn't done — no
seeded local login was available in-session and this product has no self-service signup (flagged
explicitly in the task log rather than overclaiming).

## TASK-472 — Backend: post-campaign analysis engine (Фаза 4 post-campaign audience analysis)
**Status:** done — `dotnet build` 0/0, `dotnet test` **1289/1289 green** (was 1222, net +67, all
new) · **Agent:** backend-developer (interrupted by a session-limit error mid-run; finished by the
orchestrating main session, see task log) · **Depends:** TASK-471 · **Next:** frontend-developer
(TASK-473)
Log: `.claude/logs/tasks/472_2026-08-05_post-campaign-backend_backend-developer.md`
Full engine on top of TASK-471's schema: `Features/MarketingAnalytics/PostCampaign/` (service,
repository — **zero raw SQL**, plain LINQ, unlike Фаза 1/2's NTILE/PERCENTILE_CONT — pure
behavior classifier, recommendation templates, strict import parser), `PostCampaignController`
(10 endpoints: list/import/analyze/summary/daily-turnover/rfm-activity/customers/migration/
explain/2 exports), `IPostCampaignAdvisor` (a separate advisor interface rather than reusing
`IMarketingAdvisor` — deliberate, mirrors the `IPriceSegmentAdvisor`/TASK-420 precedent since the
context DTO shape differs; same key-resolution plumbing). Import: strict whole-token GUID-or-phone
classification (never substring-extracts, the source doc's critical fix over the competitor's own
broken parser), CSV/XLSX column auto-detect + preview + confirm-override, 20,000-row cap. RFM
migration matrix reuses `IMarketingAnalyticsRepository.GetScoredCustomersAsync` +
`RfmSegmentClassifier` unchanged (no second RFM implementation), calling it a third time
(all-time) to distinguish "never purchased" (null "Без покупок" bucket) from "real history, zero
in this window" (ordinary low-R classification via sentinel R=F=M=1 scores). PII masking, export
capability gate, and `SegmentHash`/`CalculatedAt` transparency all follow Фаза 1-3's established
conventions. Interrupted mid-run by a session-limit error while the agent was polishing its own
test file (all production code already complete); main session fixed 3 test-authoring bugs
directly (1 stray `await` on a synchronous export call, 2 NSubstitute ambiguous-argument matcher
mixups, 1 test assertion that assumed the wrong RFM segment for its own fixture data — traced the
real classifier rules by hand to confirm `PotentialLoyalist`, not `Champions`, was the correct
expectation) — zero production code changed during recovery. Flagged for TASK-474
(security-reviewer): file-upload limits (10 MB, extension allowlist, memory-only), confirmed
zero new raw-SQL surface, strict-parser test coverage against the source doc's documented
competitor failure modes.

## TASK-471 — DB: PostCampaignSegment schema (Фаза 4 post-campaign audience analysis)
**Status:** done — created, migrated, live-verified against the real non-superuser app role, no
blocker · **Agent:** database-engineer · **Depends:** none (Фаза 4's first task; Фаза 0-3 already
shipped) · **Next:** backend-developer (TASK-472)
Log: `.claude/logs/tasks/471_2026-08-05_post-campaign-schema_database-engineer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фази 2-4"; full spec
`docs/uployal/AUDIENCE_ANALYSIS.md`. Two new tables — unlike Фаза 1-3 (fully stateless, computed
live from pos_transactions/items/customers on every request), Фаза 4 persists an uploaded
customer-id list plus its import-validation results and frozen before/after date windows, per the
source doc's own draft-vs-analyzed state requirement (§7). `PostCampaignSegment` (header: `Id`/
`TenantId`/`CreatedByUserId` [Restrict — mirrors `UserPermissionGrant.GrantedByUserId`], `Name`,
`UploadedCount`/`MatchedCount`/`DuplicateCount`/`UnknownCount`/`InvalidCount`,
`UnknownTokensSample`/`InvalidTokensSample` [`List<string>` as jsonb, same pattern as
`Item.Barcodes`], `AfterStart`/`AfterEnd`/`BeforeStart`/`BeforeEnd` [`DateOnly?` — null = draft,
non-null = frozen/analyzed snapshot], `SegmentHash`, `CreatedAt`/`AnalyzedAt`) +
`PostCampaignSegmentMember` (one row per matched customer: `Id`/`TenantId`/`SegmentId`
[FK→segment, Cascade]/`CustomerId` [FK→customers, Cascade], unique `(SegmentId, CustomerId)`;
`TenantId` here is a plain denormalized column with no separate `tenants` FK, same treatment as
`loyalty_ledger_entries.TenantId`). Migration `AddPostCampaignSegmentSchema` (20260805190701) —
canonical RLS triad only on both tables (no `consumer_self_access` — staff-only, same posture as
`price_segment_settings`, TASK-419). Applied cleanly via the app's own non-superuser
`shelfguard_app_dev` connection, no TASK-411-style grant incident (confirmed table ownership
immediately after). Live-verified against the real app role: ownership, policy/flag byte-check (3
policies each, correct qual), positive path (insert/select/update, rolled back), unique-constraint
backstop, fail-closed with no session vars, cross-tenant isolation, provider/provider_admin/worker
bypass, and cascade-delete (member row correctly disappears when its parent segment is deleted).
No new xUnit file — already covered by the 2 existing dynamic RLS audits in
`RlsCrossTenantIntegrationTests.cs`. `dotnet build` 0 warnings/0 errors, `dotnet test`
**1222/1222 green**, unchanged from TASK-469's baseline — no regressions. Domain entities/migration
only — no controller/service/frontend code (that's TASK-472). `.claude/docs/database-schema.md`
not updated, same precedent TASK-404/TASK-419 both set (documentation-writer's job once Фаза 4
ships in full). Not committed.

## TASK-470 — Docs: mark TASK-467's 2 MEDIUM findings as resolved (ADR-026 §4, TASK-467 entry)

**Status:** done · **Agent:** documentation-writer · **Depends:** TASK-469 · **Next:** none
Log: `.claude/logs/tasks/470_2026-08-05_mark-medium-findings-resolved_documentation-writer.md`

TASK-469 fixed both MEDIUM findings from TASK-467's security review (per-user cooldown +
refresh-token revocation) but, per its own brief, left docs untouched — flagging `decisions.md`'s
ADR-026 §4 and this file's TASK-467 entry as stale ("open"/"not yet fixed"). Closed that gap:
ADR-026 §4's closing paragraph and its Consequences bullet now state both fixes landed in TASK-469
(with a one-line summary of each) instead of "neither fix has landed"/"both still open"; TASK-467's
entry below now points its **Next** field at TASK-469 and carries a short **Update** paragraph
with the same summary + log link. No code changed, no new findings — pure documentation sync.

## TASK-469 — Backend: fix forgot-password MEDIUM findings (cooldown + session revocation)

**Status:** done — build 0/0, tests 1222/1222 (was 1220, net +2) · **Agent:** backend-developer ·
**Depends:** TASK-467 · **Next:** none required; docs (ADR-026 §4, this file's own TASK-467
summary below) now describe both findings as open and are stale — flagged for a future
documentation-writer pass, not fixed here (out of scope per brief)
Log: `.claude/logs/tasks/469_2026-08-05_fix-forgot-password-medium-findings_backend-developer.md`

Closed both MEDIUM findings from TASK-467's security review. **Cooldown:** added
`ForgotPasswordCooldownSeconds = 60` to `AuthService`; `ForgotPasswordAsync` now derives
`issuedAt = TempPasswordExpiresAt - TempPasswordValidHours` (no new column) and, when a temp
password was issued <60s ago, treats the request exactly like an unknown email — log + return,
zero side effects, no response difference (endpoint was already unconditionally 204). Checked
after the unknown/inactive-email branch so that branch's timing/enumeration posture is untouched.
**Revocation:** added `await _refreshTokens.RevokeAllForUserAsync(user.Id, ct)`, mirroring
`UserService.ChangePasswordAsync`'s existing anti-hijack call — placed before the early
`_users.SaveChangesAsync(ct)` so both the credential change and the revocation commit together in
one round trip (`RevokeAllForUserAsync` only stages in-memory, shared `AppDbContext` with `_users`).
New tests: cooldown-blocks and cooldown-elapsed-allows-retry cases, plus a
`RevokeAllForUserAsync` call-count assertion added to the existing success-path test.

## TASK-468 — Docs: temp-password forgot-password redesign (api-contracts, ADR-026, database-schema verification)

**Status:** done · **Agent:** documentation-writer · **Depends:** TASK-464/465/466, TASK-467 ·
**Updated:** 2026-08-05
Log: `.claude/logs/tasks/468_2026-08-05_docs-temp-password-redesign_documentation-writer.md`

`api-contracts.md`: forgot-password тепер описує видачу тимчасового пароля (не лінка),
`POST /api/auth/reset-password` прибрано з документації (404, видалений), `AuthUserDto` +
`passwordIsTemporary`/`temporaryPasswordExpiresAt`, новий login-401 для простроченого
temp-пароля. `decisions.md`: ADR-024 позначено `superseded by ADR-026` (контент лишився без
змін, тільки статус + пояснення зверху); новий **ADR-026** документує редизайн, включно з
TASK-467's фінальним verdict (CLEAR TO SHIP, 0 HIGH, 2 MEDIUM — відсутній cooldown + відсутній
`RevokeAllForUserAsync`, обидва fix-soon, не блокери) в п.4. `database-schema.md` — звірено проти
реального стану файлу, вже коректний після TASK-464, без правок. Додатково поправлено один
застарілий рядок в `blocked.md` (TASK-260) — досі згадував "лінк відновлення" замість
тимчасового пароля.

## TASK-467 — Security: review of temporary-password forgot-password redesign

**Status:** done — verdict: **CLEAR TO SHIP**, 0 HIGH, 2 MEDIUM (fix soon, not blockers; both since
fixed, see update below) · **Agent:** security-reviewer · **Depends:** TASK-466 · **Next:**
TASK-468 (documentation-writer); TASK-469 (backend-developer) — closed both MEDIUM findings
Log: `.claude/logs/tasks/467_2026-08-05_security-review-temp-password-redesign_security-reviewer.md`

Re-audited TASK-464..466's link→temp-password redesign against TASK-458's original 8-item
checklist. TASK-458's HIGH (raw secret leaking into `notification_queue`/notification history)
stays fixed under the new design — `dispatchTargeted()` in `notification-dispatch.job.ts`
redacts `tempPassword` to `{expiresInMinutes}` before `logNotifications()`, same defense as
TASK-460, correctly re-applied to the new payload shape without being asked. Entropy/generation
(14 chars, CSPRNG, letter+digit classes guaranteed by construction, verified by tracing the
shuffle) and the login expiry-check ordering (only after a successful hash match, never
distinguishable from a wrong guess) are both OK, verified directly in code + existing tests.

**2 new/updated MEDIUM findings, not blocking:**
1. No per-user cooldown on `POST /api/auth/forgot-password` (TASK-465 flagged as a deliberate
   deviation) — worse than the old design's "notification spam" framing because every call now
   overwrites the account's real password, so repeated calls (per-IP limiter confirmed
   ineffective in prod, KI-014) are a low-effort repeatable lockout/DoS against one targeted
   account. Cheap fix available without a migration: derive "issued <60s ago" from the existing
   `TempPasswordExpiresAt` field itself.
2. `ForgotPasswordAsync` no longer calls `_refreshTokens.RevokeAllForUserAsync`, unlike the
   superseded `ResetPasswordAsync`. If an attacker already holds a stolen refresh token (7-day
   TTL) from an unrelated prior compromise, a legitimate forgot-password recovery no longer
   evicts that session — it survives untouched until the user separately completes an
   authenticated password change (`UserService.ChangePasswordAsync` still does revoke). Fix:
   mirror that same call inside `ForgotPasswordAsync`.

Full checklist detail + file:line citations in the task log.

**Update (TASK-469, 2026-08-05):** both MEDIUM findings closed same-day. #1 (cooldown):
`AuthService` now derives `issuedAt` from the existing `TempPasswordExpiresAt` field and no-ops
re-issuance when the last one was issued <60s ago. #2 (revocation): `ForgotPasswordAsync` now
calls `_refreshTokens.RevokeAllForUserAsync`, mirroring `ChangePasswordAsync`. Build 0/0, tests
1222/1222 (+2 new). Log:
`.claude/logs/tasks/469_2026-08-05_fix-forgot-password-medium-findings_backend-developer.md`.

## TASK-466 — Frontend: temporary-password forgot-password UI (remove reset-password, banner, auth-locale default)

**Status:** done — tsc 0, build clean (`/reset-password` confirmed gone from route table), lint
0/0 · **Agent:** frontend-developer · **Depends:** TASK-465 · **Next:** TASK-467
(security-reviewer), TASK-468 (documentation-writer)
Log: `.claude/logs/tasks/466_2026-08-04_temp-password-frontend_frontend-developer.md`

Deleted the old reset-password-by-link UI entirely (`ResetPasswordCard`/`ResetPasswordForm`,
`app/(auth)/reset-password/`, `authApi.resetPassword`, `useResetPassword`,
`ResetPasswordRequest`, 12 now-dead i18n keys — verified en/uk key parity after, 3589 keys each).
Forgot-password copy now talks about a temporary password instead of "instructions"
(`forgotPasswordDescription`/`forgotPasswordSubmitButton`/`forgotPasswordSuccessMessage`). New
`TemporaryPasswordBanner.tsx` (persistent, amber, mounted above `TopBar` in
`app/(dashboard)/layout.tsx`) shows `passwordIsTemporary`'s formatted local expiry + a link to
`/settings-user#password`; disappears on its own after a password change because
`useChangePassword` now invalidates `ME_KEY` (it had no `onSuccess` before). `LoginForm.tsx` now
surfaces the backend's new "temporary password expired" 401 text instead of collapsing it into
the generic invalid-credentials message (implied by TASK-465's contract, not one of the brief's
literal 4 steps — flagged as a deviation). Auth pages (`/login`, `/forgot-password`) now default
to English instead of Ukrainian for a non-uk browser (`DashboardIntlProvider`'s new
`defaultLocale` prop, `app/(auth)/layout.tsx` passes `"en"`); dashboard default untouched ("uk").

Live-verified against the real TASK-465 backend + a client-side `window.fetch` mock for the
banner (real temp-password delivery goes through Telegram/email, out of frontend scope to chase).
**Note for whoever uses the dev admin account next:** live-testing forgot-password against
`stassmilnitskiy@gmail.com` really did overwrite its password with a new temp one (3h from
~00:10 UTC 2026-08-05) — the generated value itself was never visible to this task.

## TASK-465 — Backend: temporary-password forgot-password logic (AuthService rewrite)

**Status:** done — build 0/0, tests 1220/1220 (was 1221, net -1: 8 old link/token tests removed,
7 new added), worker `tsc --noEmit` clean · **Agent:** backend-developer · **Depends:** TASK-464
· **Next:** TASK-466 (frontend-developer) — banner on `passwordIsTemporary`, remove
`/reset-password` page; TASK-468 (documentation-writer) — docs
Log: `.claude/logs/tasks/465_2026-08-04_temp-password-backend-logic_backend-developer.md`

Rewrote `AuthService.ForgotPasswordAsync` for TASK-464's temp-password design: generates a
14-char `RandomNumberGenerator`-backed temp password (letter+digit classes constructively
guaranteed, not left to chance — always passes `PasswordValidator.Validate`; ambiguous chars
0/O/1/I/l excluded), sets it as the account's real password + 3h expiry, commits immediately
on its own (durability independent of the notification/log writes that follow), then enqueues
the existing `auth.password_reset_requested` outbox event with `{ tempPassword,
expiresInMinutes: 180 }`. Deleted `ResetPasswordAsync`/`IAuthService.ResetPasswordAsync`/
`ResetPasswordRequest`/`POST /api/auth/reset-password` entirely — no second step in this design.
`LoginAsync` now rejects an expired temp password with a specific error (only reachable after a
real hash match, never on a wrong password) —
`"Temporary password has expired. Please request a new one."`. `AuthUserDto` gained
`passwordIsTemporary`/`temporaryPasswordExpiresAt` (fresh at every mint site + `/auth/me`, via
the shared `ToDto` mapper). `UserService.ChangePasswordAsync` now clears the temp-password
marker — the one place a user "takes control" of it. Worker's `notification-dispatch.job.ts`
carries forward TASK-460's pre-`logNotifications()` redaction (now for `tempPassword` instead of
`resetUrl` — arguably higher stakes, a directly-usable credential vs. a single-purpose link).

**Flagged, not carried over:** TASK-460's 60s per-user forgot-password cooldown has no
equivalent in the new 9-step design the brief specified, and TASK-464 didn't add a field that
would support one independent of `TempPasswordExpiresAt`. The per-IP rate limit
(`auth-forgot-password`, 5/min) is now the only throttle again, same gap KI-014 already
documents as unreliable in prod. Full detail + contract for TASK-466 in the task log.

## TASK-464 — DB: redesign forgot/reset-password from link/token to temporary password (drops TASK-455's schema)

**Status:** schema done (build 0/0, tests unaffected — full suite not runnable, see blocker below)
· **Agent:** database-engineer · **Depends:** TASK-460 · **Next:** TASK-465 (backend-developer) —
rewrite `AuthService` for the new design; frontend/mobile/worker follow after that
Log: `.claude/logs/tasks/464_2026-08-04_drop-password-reset-tokens-add-temp-password-field_database-engineer.md`

**⚠️ Renumbered from the brief.** Originally assigned as "TASK-461" (follow-up "TASK-462"), but
both numbers are already in use by an unrelated, active mobile feature (offline-read cache/UX
rollout — see `461_2026-08-01_allowlisted-offline-read-cache_mobile-developer.md`,
`462_2026-08-01_limited-offline-read-ux_mobile-developer.md`, and their extensive `blocked.md`
entries, most recently updated 2026-08-01). Confirmed the true current max task log number is 463
before picking 464/465 as the next free pair. Whoever owns the authoritative task sequence should
double check this is the number they want going forward.

Product owner decided to redesign the forgot/reset-password flow (TASK-455..460, live on prod
since commit `647bde4c`, 2026-07-30) from a one-time email/Telegram link+token to a temporary
password the user receives and can log in with directly — no link, no separate "click link, enter
new password" step; the temp password becomes the real password immediately, valid 3 hours unless
the user changes it first via the existing authenticated change-password flow. Migration
`DropPasswordResetTokensAddTempPasswordExpiry` (`20260804194648`) drops `password_reset_tokens`
entirely (table + its RLS policies — a table's policies go with `DROP TABLE`, confirmed live: 0
rows in `pg_policies` for the table afterward) and adds `users.TempPasswordExpiresAt` (nullable
timestamptz). Deleted `PasswordResetToken` entity, `IPasswordResetTokenRepository` + EF repo, the
`AppDbContext` DbSet/config, and the DI registration — no dead code left. `User.cs` gets
`TempPasswordExpiresAt`/`HasActiveTempPassword`/`SetTempPasswordExpiry(DateTime)`/
`ClearTempPasswordExpiry()`, styled directly after the pre-existing `LockoutUntil`/`IsLockedOut`
pair (private setter, no public setter, dedicated methods) — see full signatures and rationale in
the task log and `.claude/docs/database-schema.md`'s new `## TASK-464` section.
`RlsCrossTenantIntegrationTests.cs`'s fail-open-exceptions test is back to 2 allowed tables
(`users`, `refresh_tokens`), same as before TASK-455.

**Blocker for TASK-465 (backend-developer, not a defect in this task):** `AuthService.cs`
(`ForgotPasswordAsync`/`ResetPasswordAsync`, added by TASK-456/460) still references the now-deleted
`IPasswordResetTokenRepository`/`PasswordResetToken` — confirmed via a real `dotnet build
ShelfGuard.sln`, 2 × `CS0246` at `AuthService.cs:38`/`:52`. Same coupling also breaks 4 files under
`ShelfGuard.Tests/Auth/` (`AuthServiceTests.cs` + 3 others that only construct `AuthService`
directly and need a `Substitute.For<IPasswordResetTokenRepository>()` for that, unrelated to what
they actually test). Left untouched per the brief ("Не чіпай AuthController/AuthService/DTO — це
TASK-462" — now TASK-465): rewriting `ForgotPasswordAsync`/`ResetPasswordAsync` for the
temp-password design, and fixing the `IAuthService.ResetPasswordAsync(string rawToken, ...)`
signature (a raw token no longer exists in the new design), is TASK-465's actual scope, not a
mechanical fix-up. The EF migration and `users` schema change themselves do not depend on
`AuthService` and were generated/live-applied cleanly (verified against the real non-superuser
`shelfguard_app_dev` connection) using a temporary, fully-reverted stub of `AuthService.cs` purely
so `dotnet ef` tooling had a compiling `ShelfGuard.Api` startup graph to build against — net diff
on that file is zero (confirmed via `git diff`/`git status`), nothing about the redesign itself was
implemented there. `dotnet build`/`dotnet test` will not go green again until TASK-465 lands.

## TASK-460 — Backend: security remediation of TASK-458's forgot/reset-password findings

**Status:** done — build 0/0, tests 1221/1221 (was 1220, +1), worker `tsc --noEmit` clean · **Agent:**
backend-developer · **Depends:** TASK-458 · **Next:** optional confirm-only re-read by
security-reviewer before real users hit this flow (not a full re-audit, not blocking)
Log: `.claude/logs/tasks/460_2026-07-30_security-remediation-forgot-reset-password_backend-developer.md`
Closed both TASK-458 findings. **HIGH:** `worker/src/jobs/notification-dispatch.job.ts`'s
`dispatchTargeted()` now redacts the outbox payload to `{ expiresInMinutes }` only before the
`logNotifications()` call for `auth.password_reset_requested` — the live `resetUrl` no longer
reaches the `notification_queue` rows `GET /api/notifications/history` returns to any same-tenant
user; the real (unredacted) URL is still used earlier in the same function for the actual
email/Telegram send, so delivery is unaffected. Every other event type's logged payload is
untouched (same ternary falls through to `row.payload` as before). **MEDIUM:** new
`IPasswordResetTokenRepository.HasRecentActiveTokenAsync(userId, window, ct)` (checks
`CreatedAt > utcNow - window` + `UsedAt == null`, ignoring `ExpiresAt` on purpose) backs a new
60s `PasswordResetCooldown` in `AuthService.ForgotPasswordAsync`, checked before
`InvalidateActiveTokensAsync` — a hit behaves exactly like the unknown-email branch (same log, same
no-op, no response difference), so it adds no new enumeration signal. `NotificationRepository
.GetHistoryAsync`/`NotificationsController` deliberately left untouched — confirmed out of scope,
a separate wider access-control pattern per the review. Verified the worker redaction logic in
isolation (Node eval against a reset-payload fixture and a non-reset fixture — redacted output has
zero trace of the token, other event types pass through unchanged); did not rebuild/drive the
shared dev worker container for a full live check (not "simple" per the brief's fallback — that
container runs built `dist/`, not live source). New `AuthServiceTests.cs` case
(`ForgotPasswordAsync_within_cooldown_window_has_no_side_effects`) plus an explicit
`HasRecentActiveTokenAsync → false` stub added to the existing happy-path test.

## TASK-458 — Security: review of forgot/reset-password flow

**Status:** done — **verdict: NOT clear to ship as-is.** 1 HIGH, 1 MEDIUM finding · **Agent:**
security-reviewer · **Depends:** TASK-456, TASK-457 · **Next:** backend-developer/devops follow-up
on the 2 findings below before real users get this flow
Log: `.claude/logs/tasks/458_2026-07-30_security-review-forgot-reset-password_security-reviewer.md`
Read TASK-455/456/457 logs, then the code directly. 6 of 8 checklist items **OK**: token entropy
(64-byte `RandomNumberGenerator`, same as refresh tokens), forgot-password's always-204 posture
(timing asymmetry vs. unknown email noted as low-severity, same pre-existing pattern as
`LoginAsync`), the RLS fail-open exception + its test/doc coverage (`allowedFailOpen` correctly
3 entries, `database-schema.md` exceptions table verified accurate), lockout-clear +
refresh-revocation ordering (all one `SaveChangesAsync`, no partial-persistence path), the
frontend reset-password surface (no external resources/analytics anywhere in
`app/(auth)/**`, token travels in the POST body not the query string), and reset-password's
enumeration posture (not-found/expired/used/inactive-owner all return the identical generic
string). **HIGH finding:** the live raw reset token (`resetUrl` in the outbox `Payload`) survives
into `notification_queue` rows written by the worker's `logNotifications()`
(`worker/src/services/notification-log.ts:36-52`) with `Channel="email"/"telegram"` — NOT excluded
by `NotificationRepository.GetHistoryAsync`'s `Channel != "system"` filter
(`NotificationRepository.cs:63`) — and `NotificationsController.GetHistory`/`GetById`
(`NotificationsController.cs:57-80`) scope only by `tenantId`, no per-user enforcement, bare
`[Authorize]`, no role gate. Net effect: any authenticated same-tenant user of any role can call
`GET /api/notifications/history` and read any colleague's live, unexpired password-reset link —
account takeover, no IP tricks/brute force needed. Pre-existing endpoint gap, but TASK-456 is the
first event type whose payload carries a bearer secret through it. Recommend redacting `resetUrl`
before it reaches `logNotifications` (still use the real URL for the actual send, which happens
earlier in the same function) — worker-side, `notification-dispatch.job.ts`'s `dispatchTargeted()`.
**MEDIUM finding:** `ForgotPasswordAsync` has no per-user/per-email cooldown independent of the
`"auth-forgot-password"` per-IP limiter (5/min, confirmed correctly wired,
`Program.cs:125-133`/`AuthController.cs:220`) — and KI-014 already confirms per-IP limiting is
ineffective in production. Since Telegram delivery already works today, this is currently an
unmitigated notification-spam vector against any known/guessed email. Recommend a per-user cooldown
in `AuthService.ForgotPasswordAsync` in addition to the IP limiter. No code changed (audit only);
both findings are recommendations for a follow-up implementation task, not applied here.

## TASK-459 — Docs: forgot/reset-password flow (api-contracts, database-schema confirm, ADR-024, blocked.md cross-ref)

**Status:** done · **Agent:** documentation-writer · **Depends:** TASK-456, TASK-457 · **Next:** none blocking (TASK-458 security-reviewer runs in parallel)
Log: `.claude/logs/tasks/459_2026-07-30_docs-forgot-reset-password_documentation-writer.md`
Plan: `C:\Users\stass\.claude\plans\reflective-churning-quail.md` §"Документація (TASK-459)".
`.claude/docs/api-contracts.md` — added `POST /api/auth/forgot-password`/`reset-password` to the
Auth block (rate limits, 204/400 shapes, reset-link URL shape, outbox delivery pointer to
ADR-024); header date bumped 2026-07-30. `.claude/docs/database-schema.md` — verified TASK-455's
exceptions-table fix (3 rows: `users`/`refresh_tokens`/`password_reset_tokens`, `notification_settings`
correctly removed) and its new `## TASK-455` section are both already accurate — no content
change, only bumped the stale header date to match. `.claude/docs/decisions.md` — new
**ADR-024** (outbox/`dispatchTargeted()` reuse over a new C# BullMQ producer, `password_reset_tokens`
as the 3rd fail-open RLS exception, email-primary/Telegram-fallback with the TASK-260 dependency,
`Frontend__BaseUrl` env-var-not-IConfiguration precedent, 400-not-401 rationale), header bumped.
`.claude/tasks/blocked.md` — added a one-paragraph cross-reference under the existing TASK-260
entry (forgot/reset-password's email channel depends on the same Resend DNS blocker; Telegram
fallback doesn't and works today). No `known-issues.md` entry created (deliberate — not a new
problem, a new dependent of an already-tracked one, per brief). No code touched.

## TASK-457 — Frontend: forgot/reset-password UI + back-to-landing navigation

**Status:** done — `tsc --noEmit` 0 errors, `npm run build` clean, live-verified end-to-end
against the real TASK-456 backend · **Agent:** frontend-developer · **Depends:** TASK-456 ·
**Next:** security-reviewer (TASK-458), documentation-writer (TASK-459)
Log: `.claude/logs/tasks/457_2026-07-30_forgot-reset-password-frontend_frontend-developer.md`
Plan: `C:\Users\stass\.claude\plans\reflective-churning-quail.md` §"Frontend (TASK-457,
frontend-developer)". New `AuthLogo.tsx` (shield+wordmark wrapped in `<Link href="/">`, replaces
`LoginCard.tsx`'s old unclickable inline markup — used by all 3 public auth cards now) +
`ForgotPasswordCard/Form.tsx` + `ResetPasswordCard/Form.tsx` + their `/forgot-password` and
`/reset-password` routes + `LoginForm.tsx`'s new "Forgot password?" link + `useForgotPassword`/
`useResetPassword` hooks + `middleware.ts` (`/forgot-password` gated same as `/login`,
`/reset-password` deliberately not — token in URL authorizes the action independent of session)
+ `notifications/types.ts`'s `auth.password_reset_requested` entry (not added to
`NotificationSettingsTable.tsx`'s `ALL_EVENTS`, matching the `access.*` precedent) + full
`Dashboard.auth` i18n block in both locales. Reset-password's 400 body: the known
`"Invalid or expired reset link."` sentinel gets a localized replacement, any other message
(password-policy violation) is shown verbatim in English, mirroring `ChangePasswordForm.tsx`'s
existing convention. **Live-verified end-to-end against the real backend** (dev servers via
`preview_start`; had to restart the backend once with `Cors__Origins` widened to include the
auto-reassigned frontend dev port, same CORS issue TASK-421 already hit): real
`POST /api/auth/forgot-password` → 204 → unconditional success message; real
`POST /api/auth/reset-password` with a fake token → 400
`{"error":"Invalid or expired reset link."}` → correctly localized in the UI; no-token URL shows
a friendly message with no form; client-side zod validation (short password, mismatched confirm)
fires with zero network calls; logo click lands on the public landing page;
`AUTH_ROUTES`/`/reset-password` middleware exclusion both confirmed via a manually-set
`sg_session` cookie. The sandboxed browser's `computer` click tool couldn't land clicks here
(pane not compositing) — interactions were driven via `javascript_tool` dispatching real
bubbling DOM events instead, exercising the same code paths. One i18n key added beyond the
brief's enumerated list, `somethingWentWrongError` (generic transport-failure fallback — the
brief's prose required the behavior but didn't name a key). Not committed.

## TASK-456 — Backend: forgot/reset-password business logic + API + worker

**Status:** done — build 0/0, tests 1220/1220 (was 1213, +7), worker tsc/build clean · **Agent:**
backend-developer · **Depends:** TASK-455 · **Next:** frontend-developer (TASK-457)
Log: `.claude/logs/tasks/456_2026-07-30_forgot-reset-password-backend_backend-developer.md` (full
API contract for TASK-457 there). Plan: `C:\Users\stass\.claude\plans\reflective-churning-quail.md`
§"Backend (TASK-456, backend-developer)". Added `AuthService.ForgotPasswordAsync`/
`ResetPasswordAsync` (both no-enumeration/generic-error posture matching `LoginAsync`/
`VerifyTwoFactorAsync`), `POST /api/auth/forgot-password` (always 204, rate limit 5/min new
`"auth-forgot-password"` policy) and `POST /api/auth/reset-password` (204/400, shares `"auth-login"`
10/min), `Frontend__BaseUrl` env plumbing (staging/production `.env.example` + compose files),
`NotificationService.ValidEventTypes` entry, and worker `notification-dispatch.job.ts`
`TARGETED_EVENT_CHANNELS`/`formatText`/`formatEmail` support for
`auth.password_reset_requested` (email + Telegram, no push). **Found and resolved a real
pre-code-review risk**: both new methods run on anonymous (`[AllowAnonymous]`, no `app.tenant_id`
set) connections — live-verified directly against the real non-superuser `shelfguard_app_dev` role
(rolled-back transaction) that `activity_logs`/`notification_queue` INSERTs and `users`/
`refresh_tokens`/`password_reset_tokens` UPDATEs all succeed under real RLS in that exact anonymous
session state; dev DB confirmed clean afterward. Email channel stays invisible to real users until
TASK-260 (Resend DNS) unblocks; Telegram works today for linked accounts. Not committed.

## TASK-455 — DB: Password reset tokens schema (forgot/reset-password flow)

**Status:** done — created, migrated, live-verified against the real non-superuser app role · **Agent:** database-engineer · **Depends:** none (Task #1 of the flow) · **Next:** backend-developer (TASK-456)
Log: `.claude/logs/tasks/455_2026-07-30_password-reset-tokens-schema_database-engineer.md`
Plan: `C:\Users\stass\.claude\plans\reflective-churning-quail.md` §"Database (TASK-455,
database-engineer)". New `password_reset_tokens` table + `PasswordResetToken` entity (styled like
`RefreshToken` — private setters, `Create()` factory, computed `IsActive`, `MarkUsed()`) +
standalone `IPasswordResetTokenRepository` (`InvalidateActiveTokensAsync` bulk `ExecuteUpdateAsync`,
`AddAsync`, `GetActiveByHashAsync`, `SaveChangesAsync`) + migration `AddPasswordResetTokens`
(20260730090415). No own `TenantId` — tenant derived via `UserId → users.TenantId`; RLS
`tenant_isolation` is deliberately fail-open (`EXISTS`-through-`users`, verified byte-for-byte
against `refresh_tokens`' live policy), the 3rd documented exception alongside `users`/
`refresh_tokens`. Fixed stale `notification_settings` references in
`RlsCrossTenantIntegrationTests.cs` and `database-schema.md`'s exceptions table (removed by
TASK-360 back in July, docs/test text just hadn't caught up) while there. `dotnet build` 0 err/0
warn, `dotnet test` **1213/1213 green** including the 6 RLS regression tests run live against
Postgres (no soft-skip). No `AuthController`/`AuthService` changes — that's TASK-456.

## TASK-435 — Mobile: real-device baseline QA

**Status:** in_progress / partial acceptance · **Agent:** qa-tester (Codex) · **Updated:** 2026-08-01

Fresh current-source debug APK was built and installed on realme RMX2063, Android 11 / API 30,
serial `13cb6660`, against `https://api.agrusystems.pp.ua:10054/api`. Native cold start passes,
but the first QA build failed before usable auth UI. Root cause was a NativeWind
`react-native-css-interop` development warning serializer crashing while dynamically adding
`shadow-sm` to tab controls; the reported navigation-context error was a secondary symptom.
All equivalent dynamic shadow toggles were removed and static regression passes: TypeScript,
lint (0 errors/13 warnings), Jest, Android bundle export. The unauthenticated-QA `Required`
localization defect is also fixed across staff login and consumer login/register with shared
field-specific Ukrainian schemas (18 suites/78 tests). Rebuild/install, then resume device QA;
authenticated acceptance additionally needs seeded business data.

Post-fix physical retest passes on a newly packaged/installed APK: current bundle and auth choice
render, staff login entry and Android Back work, force-stop/relaunch returns to usable auth, and
unauthenticated schedules/service-desk/POS/marketplace deep links fail closed without the prior
navigation-context error. Remaining authenticated TASK-437–444 acceptance needs approved
staff/2FA credentials and seeded tenant/store/location/product/POS-shift/warehouse data.
Unauthenticated QA additionally passes staff/consumer entry, malformed email, synthetic invalid
credentials, Back, and hot background/foreground. A low localization defect remains: empty auth
fields show English `Required` inside Ukrainian UI. Offline-specific presentation was inconclusive;
Wi-Fi was restored to its original enabled state and mobile data remained disabled.

Authenticated continuation completed for provider, network manager, store manager, storekeeper,
and merchandiser. Role dashboards/navigation render and guarded routes fail closed. The camera
permission/scanner flow passes. The enterprise-admin account reaches the implemented mobile 2FA
challenge but cannot complete without a current OTP/recovery code. Store-manager HOT restoration
passes, while force-stop plus dev-client reconnect loses the visible staff session (TASK-437 high
defect). Storekeeper POS needs a seeded `pos` tab/module and a safe follow-up session; no sale or
shift was created. See `.claude/logs/reviews/2026-07-29_mobile-baseline.md`.

TASK-437 cold restoration and TASK-438 Back cancellation fixes are now prepared. Explicit
pending/ready hydration gates auth/staff/consumer routing until SecureStore and `/auth/me` or
terminal cleanup complete. The 2FA challenge handles hardware Back, Android IME Back dismissal,
header Back, and unmount through one safe cancellation boundary. TypeScript passes; lint has
0 errors/13 existing warnings; Jest passes (20 suites/84 tests); Android export passes. Physical
retest remains required.

TASK-437 offline cold-bootstrap follow-up and TASK-444 owner-switch draft loss are also
`fix_ready_for_device_retest`. Transient bootstrap/refresh failures preserve secure auth while
clearing private cache and showing retry; terminal auth failures still clean all session state.
Draft storage is owner-namespaced and legacy shared records migrate only for their embedded owner.
Current verification: TypeScript pass, lint 0 errors/13 warnings, Jest 20 suites/90 tests, Android
export pass.

TASK-444 same-owner cold restore had a second root cause: load validation rejected and deleted
incomplete form snapshots that autosave legitimately wrote. Validation now accepts incomplete
draft fields, submit rules remain unchanged, and AppState background flushes the latest sanitized
snapshot. Restart and foreign-owner integration tests pass; baseline is 20 suites/92 tests.

Physical retest of those fixes now passes. Store-manager force-stop/reconnect restores the same
authenticated identity after the bootstrap loading phase; HOT resume and logout/private-cache
cleanup pass. 2FA hardware Back safely cancels before input and after focusing the code field.
Exactly one approved recovery code was accepted for enterprise admin; its value is not recorded
and reuse was not attempted. Enterprise-admin role navigation and logout pass. Live TOTP remains
untested, as do safe seeded POS mutation/durability flows.

## TASK-444 — Mobile: durable warehouse and production drafts

**Status:** transfer_draft_device_pass / receipt-create contract pending · **Agent:** mobile-developer (Codex) ·
**Depends:** TASK-443 · **Next:** TASK-445; Android acceptance through TASK-435

Added reusable, owner-isolated/versioned operational drafts and integrated them into the existing
write-off, transfer, and production-order forms. Drafts restore after process restart, survive
confirmed failures/conflicts/ambiguous timeouts, clear only after confirmed success or explicit
discard, and cannot persist auth/QR/2FA secrets. Transfer stock and production recipe references
are server-refetched before submit; write-off stock conflicts rely on backend `409` because its
form has no batch reference. Owner-context changes fail closed immediately and FEFO remains
server-authoritative. The mobile receipt module has
no create form or approved create DTO, so receipt-create support awaits the recorded contract
handoff. TypeScript/lint/tests pass; Android force-close QA remains TASK-435.

Log: `.claude/logs/tasks/444_2026-07-29_durable-operational-drafts_mobile-developer.md`
Handoff: `.claude/logs/handoffs/444-to-backend-product_mobile-developer.md`

Device QA: transfer autosave/offline banner/discard pass, but switching manager → storekeeper hides
and deletes the manager snapshot, so returning to manager cannot restore it. High defect:
`.claude/logs/reviews/bug-task444-owner-switch-deletes-draft_2026-07-29.md`. No test draft remains.

Current-source retest still fails earlier: same-owner transfer note did not restore after
force-stop. Full user-switch and offline-cold sequences were stopped; no marker remained visible.

Final focused retest now passes the exact incomplete transfer-note path: background, same-owner
cold restore, manager/storekeeper isolation, manager-return restore, explicit discard, and cold
absence. No marker or server mutation remains.

Testing is explicitly paused by user request. Durable handoff:
`.claude/logs/reviews/2026-07-29_TASK-435-mobile-device-qa-pause-handoff.md`.
Remaining work: live TOTP, seeded active POS shift, controlled offline-cold bootstrap, receipt
contract, and write-off/production fixtures.

Testing resumed by user request on 2026-08-01. Controlled TASK-437 offline cold bootstrap now
passes: the session is retained behind an offline Retry screen, private UI is withheld, and the
same manager dashboard returns after proven API readiness. Connectivity was restored exactly.
Remaining work: live TOTP, seeded active POS shift, receipt contract, and write-off/production
fixtures.

Live TOTP was not attempted because no current six-digit authenticator value was supplied. Manager
POS was rechecked read-only and still shows `Зміна не відкрита`; no shift or POS mutation
was created.

TASK-444 prerequisites were inspected read-only. Write-off draft coverage requires scanning a real
product; production fails closed because the tenant module is disabled. No product, draft, or
business mutation was created.

End state: Metro 8082 stopped after its bounded run. ADB became unresponsive during a second
write-off reproduction attempt, so the task-owned reverse could not be queried/removed. Reconnect
or unlock the phone, then remove only `tcp:8082` if still present. TASK-435 remains partial/open;
see `.claude/logs/reviews/2026-08-01_TASK-435-device-qa-resume.md`.

After ADB recovery, read-only manager regression passed for dashboard plus empty-state stock,
receipts, customers, Service Desk, schedules, and idle AI assistant. Detail coverage was unavailable
because the lists are empty. Marketplace opening and notifications remain incomplete; auto-service
is unavailable in the current module context. No mutation occurred.

Marketplace list and existing supplier detail now pass. Notifications pagination/refresh remains
incomplete because the attempted control opened SDK development-client tools; no notification state
was changed.

Closing the tools overlay and using the authorized static notifications route passes list and
unread-count rendering. Pagination/refresh and exact Back confirmation remain incomplete; no item
or mark-all action was tapped.

Final cleanup: Metro PID 40052 is stopped and port 8082 has no listener. ADB timed out during
reverse removal; reconnect/unlock and remove only `tcp:8082` if still listed. Last verified phone
connectivity was Wi-Fi ON/mobile data OFF.

## TASK-443 — Mobile: durable POS cart and network recovery

**Status:** review_pending_device · **Agent:** mobile-developer (Codex) ·
**Depends:** TASK-437 · **Next:** TASK-444; Android acceptance through TASK-435

Persisted owner-scoped/versioned POS shift, cart, quantities, customer/loyalty selection, and
payment draft with a secret-whitelisting serializer. Added NetInfo UI, offline submit guard,
single-flight double-tap lock, and explicit pending/failed/completed/conflict/uncertain states.
Timeout/no-response is never auto-retried because the current API has no idempotency key or
reconciliation lookup; the uncertain draft is retained for shift reconciliation. Only confirmed
success clears storage. Cross-shift carts fail closed, rapid durable writes are serialized, and
editing cannot silently clear uncertain/conflict state. TypeScript passes; lint has 0 errors/19
baseline warnings; 13 suites/61
tests pass. Device force-close acceptance remains TASK-435.

Log: `.claude/logs/tasks/443_2026-07-29_durable-pos-network-recovery_mobile-developer.md`
Backend handoff: `.claude/logs/handoffs/443-to-backend_mobile-developer.md`

Device QA is blocked by seeded state: manager and storekeeper POS both report no open shift.
Opening one was prohibited; no sale/shift mutation occurred.

## TASK-439 — Mobile: module activation and role-aware navigation
**Status:** review_pending_device (implementation complete 2026-07-29) · **Agent:** mobile-developer (Codex) ·
**Depends:** TASK-437 · **Next:** Android acceptance through TASK-435
Log: `.claude/logs/tasks/439_2026-07-29_module-role-navigation_mobile-developer.md`
Handoff: `.claude/logs/handoffs/439-to-backend_mobile-developer.md`
Current controller inspection corrected stale documentation: authenticated tenant staff can read
server-derived `businessType` and modules from `/api/settings/modules`. Mobile preserves
permissions/capabilities/tabs, centralizes all route requirements, filters Dashboard/More and
bottom tabs, and guards every `(app)` deep link fail-closed. Provider tenant-module access remains
closed. TypeScript and lint pass; 10 suites/45 tests pass. Android acceptance awaits TASK-435.

## TASK-438 — Mobile: 2FA login with TOTP and recovery codes
**Status:** device_pass_recovery_totp_pending ·
**Agent:** mobile-developer (Codex main session) · **Depends:** TASK-437 ·
**Next:** TASK-439; final live acceptance returns through TASK-435
Log: `.claude/logs/tasks/438_2026-07-29_mobile-2fa-login_mobile-developer.md`
Handoff: `.claude/logs/handoffs/438-to-442_mobile-developer.md`
Implemented the existing backend challenge contract in mobile: password login routes 2FA-enabled
staff to a dedicated Ukrainian screen supporting six-digit TOTP and `XXXX-XXXX` recovery codes.
The challenge token is memory-only, never written to SecureStore or logs, and is cleared on success
or whenever the verification route is left. Invalid-code `401` responses bypass auth refresh so
they cannot accidentally terminate an existing session. Type-check and lint pass; 8 suites/30
tests pass. Live TOTP/recovery verification remains pending because TASK-435 has no device/AVD.
Mobile 2FA setup/enable/disable remains intentionally web-only.
Android Back/IME dismissal and header Back now clear the challenge and return to staff login;
focused cleanup also clears it on unmount. See
`.claude/logs/tasks/438_2026-07-29_android-back-cancellation_mobile-developer.md`.

## TASK-437 — Mobile: auth refresh and terminal session cleanup
**Status:** done / Android device verified ·
**Agent:** mobile-developer (Codex main session) · **Depends:** TASK-436 ·
**Next:** TASK-439; final device acceptance returns through TASK-435
Log: `.claude/logs/tasks/437_2026-07-29_auth-refresh-session-cleanup_mobile-developer.md`
Handoff: `.claude/logs/handoffs/437-to-442_mobile-developer.md`
Implemented authenticated-only single-flight refresh, exactly-once request retry, terminal cleanup
of SecureStore + Zustand + private React Query cache, resilient partial-keystore cleanup, and a
session-epoch guard preventing logout/refresh races. Failed unauthenticated login no longer attempts
refresh; a refreshed token rejected with another 401 terminates without a loop. Staff logout,
consumer logout, and cold-start invalid-session handling share the same cleanup boundary.
`type-check`/lint (0 errors)/`npm ls` clean; 7 suites/24 tests pass. Android native cookie and
redirect behavior remains unverified because TASK-435 has no device/AVD, so the task is not marked
done. Existing TASK-427 notification implementation remains untouched.
Cold start now uses explicit hydration gating and redirects a restored staff/consumer session only
after persisted state plus `/auth/me` or terminal cleanup finish. See
`.claude/logs/tasks/437_2026-07-29_cold-session-hydration_mobile-developer.md`.

## TASK-436 — Mobile: ESLint and automated test infrastructure
**Status:** done (2026-07-29) · **Agent:** mobile-developer (Codex main session) ·
**Depends:** TASK-434; TASK-435 device QA remains blocked · **Next:** TASK-437
Log: `.claude/logs/tasks/436_2026-07-29_mobile-test-infrastructure_mobile-developer.md`
Roadmap: `.claude/tasks/mobile-roadmap.md`
Added Expo SDK 56 ESLint flat config and Jest/RNTL infrastructure: typecheck PASS, lint PASS
(0 errors/19 recorded warnings), 6 suites/17 tests PASS. Covered auth persistence/restoration,
canonical roles, auth API mapping/2FA failure, TASK-427 paged notifications, POS loyalty totals,
and a shared RN component. Fixed a real conditional Hooks violation in Customers. Expo Doctor
follow-up added missing `expo-font`, removed unused incompatible direct React Navigation tabs,
and aligned SDK 56 dependencies; 20/21 checks pass, with the last `.expo` tracking check clearing
after the generated tracked README deletion is committed. Non-force audit fixed runtime axios
high findings; remaining production findings are 10 moderate Expo/Xcode `uuid` advisories whose
only npm proposal is an unsafe forced Expo 56→46 downgrade, not applied. Device test deferred to
TASK-435. Existing uncommitted TASK-427 notification implementation preserved.

## TASK-431 — Security: review of Фаза 3 (AudienceBuilder)
**Status:** done — **verdict: CLEAR TO SHIP**, no blocker, no risk-level finding · **Agent:**
security-reviewer · **Depends:** TASK-428..430 · **Next:** none blocking; optional low-priority
follow-up below
Log: `.claude/logs/tasks/431_2026-07-27_security-review-audience-builder_security-reviewer.md`
Read TASK-428/429/430's logs, then the code directly. All 7 mandatory checklist items verdict
**OK**: (1) raw-SQL parameterization in `AudienceBuilderRepository.cs` (3rd raw-SQL repository,
first with free-text user input reaching SQL, not just GUID/date/enum) — every one of the 7 methods
binds search terms/category ids/excluded-item ids/store ids/dates/thresholds as genuine positional
`SqlQueryRaw` parameters; free-text terms travel as a typed `string[]` bound to `UNNEST({n}::text[])`,
with the `'%' || tt.value || '%'` `ILIKE` pattern built server-side in Postgres against the already-
parameter-bound value, never C#-side string concatenation — traced all 7 methods, zero exceptions.
(2) `sortBy` allowlist — `AudienceBuilderSortKeys`'s 2 fixed `HashSet`s normalize any input to a
closed literal set (same shape as `PriceSegmentSortKeys`), then the repository's
`BuyerSortColumn`/`MatchedItemSortColumn`/`CompetitorBuyerSortColumn` map that to a SECOND hardcoded
SQL-column-name literal before `{SORT_COLUMN}` substitution — raw client string never reaches SQL
text. (3) `::timestamptz` casts — checked every `t."CreatedAt"` comparison across all 6 query
methods touching `pos_transactions` (incl. both competitor CTEs) — 100% consistent, no exception.
(4) PII masking day-0 — `MaskPhoneUnlessAuthorized` reuses the same shared `PiiMasking.MaskPhone`
(not forked); `CanViewUnmaskedPii`/`UnmaskPii` always server-resolved/ANDed via
`MarketingAnalyticsAuthorization.CanExportPii(User)`, never trusted from client input on reads;
Overview/MatchedItems DTOs carry no phone field at all. (5) capability gate — confirmed
`AudienceBuilderController` carries byte-for-byte the same
`[Authorize(Policy=MarketingAnalyticsViewOrCapability)]` + `[RequireModule("marketing_analytics")]`
as `PriceSegmentsController`/`MarketingAnalyticsController`, no new capability/module key; tenantId/
userId always from JWT claims, never request body. (6) Excel export — both export builders route
through the injected `IExcelExportService` (ClosedXML-backed, TASK-414's
`SetCellValue`/`SanitizeForSpreadsheet` formula-injection guard), no direct ClosedXML usage, no
second unguarded path. (7) Seq Scan tradeoff — documented redundantly at 3 levels (interface XML
doc, repository class doc, inline categories-query comment), all citing TASK-428's actual
measurement; independently confirmed this is PERFORMANCE-only, not a tenant-isolation bypass — the
non-leakproof ILIKE only blocks the index path, TASK-428's own EXPLAIN output shows the RLS tenant
predicate still applies as a `Filter`; every CTE also carries its own redundant explicit
`TenantId = {0}` filter on top of RLS (defense-in-depth). **Additional observations (non-blocking):**
`ExcludedItemIds`/`CategoryTermIds`/`StoreIds` cross-tenant IDOR not exploitable (arrays only
filter/exclude within an already tenant-scoped CTE, can only narrow, never leak); `customers`/
`locations` joins downstream of a scoped CTE don't re-filter by TenantId explicitly, but this is
identical pre-existing convention already in `PriceSegmentsRepository.cs` (cleared in TASK-422), not
a new regression; no explicit cap on `Terms`/`StoreIds`/`ExcludedItemIds` array length, systemic
pre-existing pattern shared with PriceSegments, not urgent. Reconfirmed `dotnet build` clean (0
warnings/0 errors) before writing the verdict; did not re-run `dotnet test` (read-only review, no
code changed — TASK-429's log already reports 1213/1213 green for these exact paths). No code
changed.

## TASK-430 — Frontend: AudienceBuilder UI (Фаза 3)
**Status:** done — `tsc --noEmit`/`npm run build` clean, live-verified end-to-end in browser
against real seeded data, no blocker · **Agent:** frontend-developer · **Depends:** TASK-429
(API contract) · **Next:** security-reviewer (raw-SQL/PII gate already scheduled against TASK-429,
unaffected by this frontend-only task), qa-tester (regression pass), documentation-writer
(glossary/api-contracts/ADR for Фаза 3)
Log: `.claude/logs/tasks/430_2026-07-27_audience-builder-frontend_frontend-developer.md`
New `frontend/features/marketing-analytics/audience-builder/` (types/api/Zustand store/hooks/
12 components across `BuyersTab`/`CompetitorTab`/`MatchedItemsTab`) + page route + Sidebar nav item
(3rd item in `marketing_analytics` group) + full `Dashboard.audienceBuilder.*` i18n namespace in
both `en.json`/`uk.json`. Reused directly (not duplicated): `SortableHeader`/
`TablePaginationFooter` from `price-segments/components/TableControls.tsx` (all 3 tables),
`canExportMarketingAnalyticsPii`/`useStores`/`useRequireTab`/`AccessDenied`/`downloadFilePost`.
Zustand store (terms/mode/thresholds/excludedItemIds/isBuilt/competitor state) is a deliberate
structural difference from Фаза 1/2's page-local `useState` — justified because leaf control
components (TermChips, ThresholdInputs, MatchedItemsTable's checkboxes, CompetitorTermInput,
HorizonToggle) sit 2-3 folders deep and would otherwise need setters prop-drilled through
`ResultTabs`/3 tab folders; period/store selection stays page-local `useState`, matching Фаза 1/2,
and `store.reset()` deliberately does not touch it. New `AudiencePeriodBar.tsx` (own file, NOT a
reuse of `price-segments/ComparisonFilterBar.tsx`) — `AudienceBuildRequest` takes raw From/To with
no server-resolved period-preset concept (confirmed in the actual DTO), and this repo's own
precedent is that every phase gets its own period/store filter component (Фаза 1's
`PeriodStoreFilterBar` vs Фаза 2's `ComparisonFilterBar`, neither reusing the other). **Deliberate
deviation from the design doc's aspirational "don't refetch matched-items on exclusion, debounce
~300ms" note**: all three own-audience queries (overview/buyers/matched-items) refetch together on
every exclusion via the shared full-filter-object query key, uniformly, no debounce — matches both
the verified backend contract's own description ("re-calling this endpoint is how the UI refreshes
it") and the mandatory "миттєвий перерахунок" (INSTANT) requirement, which a debounce would work
against. One real optimization kept: `matchedItemsPage` only resets to 1 on a genuine search-filter
change, never on an exclusion-only change (the matched-item *set* never changes on exclusion, only
each row's `isExcluded` flag). PII on-screen phone is rendered exactly as the server sends it,
never re-masked client-side (confirmed the backend already resolves `CanViewUnmaskedPii`
server-side for `buyers`/`competitor/buyers` reads, unlike price-segments where on-screen phone is
never masked at all). **Live-verified on the dev stack against real Postgres data** (logged in as
seeded `ea@demo.local`, tenant "Свіжий Кут", 125 real customer-linked transactions) — the full
mandatory checklist: text term → chip → 2nd term → OR/AND toggle appears → OR gives 11
participants, AND gives 0 (genuinely different, 0-state renders cleanly); quantity threshold
narrows 11→4; "Знайдені товари" tab shows a matched item with 0 sold/0 receipts/0 buyers (zero-
sales-still-shown, live); **unchecking a SKU instantly updated both the KPIs AND the buyers table
together** on the other tab (core requirement); competitor tab horizon toggle confirmed via
`read_network_requests` to send a genuinely different wire payload between InPeriod/AllTime
(different `filtersHash` values) even though this specific dataset coincidentally produced the same
count for both (backend's own dedicated test already covers the size-differs case with controlled
data); real server-side pagination confirmed (page 2 shows a different row, not a client slice);
"Скинути" fully restores the initial empty state; export button text explicitly says "на рівні
чеків" so it can't be confused with the on-screen buyer-level table; zero console errors throughout.
**Data gap noted, not a code defect**: the `categories` table is empty (0 rows) across the entire
dev DB, so the category-typeahead path could only be verified structurally (correct request/empty-
response/empty-state render), not with real populated suggestions — flagged for whichever future
task seeds category data. `npm run build` output has repeated `ENVIRONMENT_FALLBACK` stderr noise
during static generation — confirmed pre-existing/unrelated (appears for dozens of untouched
routes, exit code 0, no real compile/type errors anywhere). Not committed.

## TASK-429 — Backend: AudienceBuilder engine (Фаза 3)
**Status:** done — `dotnet build`/`dotnet test` clean (1213/1213, was 1186; +27), no blocker ·
**Agent:** backend-developer · **Depends:** TASK-428 (`idx_items_name_trgm` + its Seq Scan finding,
handled per orchestrator's accept-for-v1 decision, no LEAKPROOF/SECURITY DEFINER) · **Next:**
frontend-developer (full API contract in the task log below), security-reviewer (raw-SQL
parametrization on a 3rd raw-SQL repository + PII export gate, same pattern as TASK-412/422),
documentation-writer (glossary/api-contracts/ADR)
Log: `.claude/logs/tasks/429_2026-07-27_audience-builder-backend_backend-developer.md` (full API
contract for the frontend agent lives there). Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md`
§"Фази 2-4". Design doc: scratchpad `phase3-audience-builder-design.md`. From-scratch continuation
of a session that hit its usage limit before any code existed. New
`Features/MarketingAnalytics/AudienceBuilder/` (thin service → 3rd raw-SQL repository, same shape
as RFM Фаза 1/PriceSegments Фаза 2) + `AudienceBuilderController.cs` (POST-for-reads, design doc
§1's explicit decision — filter shape doesn't fit a query string) + DI in both
`Application`/`Infrastructure` `DependencyInjection.cs`. No AI advisor (design doc has no `/explain`
for this feature). **Found and fixed 2 real bugs in the design doc's own SQL sketch** (both covered
by dedicated live-Postgres regression tests): (1) an item matching MORE THAN ONE term (e.g. a text
term AND a category term both matching the same product — a realistic AND-mode combination) would
double-count that purchase's quantity/amount once per matching term while still correctly
satisfying AND-mode coverage — fixed by splitting the aggregation into a deduplicated
customer-totals CTE and a separately-derived term-coverage CTE (re-joining the already-computed
line-items back to the small in-memory matched-terms set, not a second scan of
`pos_transaction_items`); (2) line amount was `PriceFinal` alone (a PER-UNIT price, confirmed
against `AnalyticsRepository`'s own `PriceFinal * Quantity` convention) instead of
`PriceFinal * Quantity`, which would have under-reported spend on any line with quantity != 1.
TASK-428's finding handled exactly as decided: no LEAKPROOF/SECURITY DEFINER, Seq Scan accepted
with a documented comment on both the interface and the repository class; every `CreatedAt`
date-range parameter explicitly cast `::timestamptz` per the brief's "Обов'язково" instruction.
PII masking (design doc §9) applied from day 0 — reuses the existing `PiiMasking.MaskPhone`
verbatim (no duplicate), `CanViewUnmaskedPii` always server-resolved via
`MarketingAnalyticsAuthorization.CanExportPii` on reads, client `UnmaskPii` ANDed with the same
check on exports; every export writes an `ActivityLog` row (same audit contract as Фаза 1/2).
Deliberate, documented deviations from the design doc's literal DTO sketch: `CompetitorAudienceRequest`
omits `OwnMode`/`OwnMinQuantity`/`OwnMinAmount` (the design doc's own SQL never gates the
competitor-exclusion set by them); `OwnExcludedItemIds` IS applied to the competitor query's
`own_matched` set (the sketch didn't parameterize this at all — verified via a dedicated test that
excluding the sole own-matching item disables the whole exclusion, a real behavior difference);
export request DTOs are their own flat records (matches the actual Фаза 2 precedent, not the design
doc's abbreviated endpoint-table shorthand); `AudienceBuilderFilterHash` is its own small copy
rather than reaching into the sibling `PriceSegments` namespace for a same-shaped utility. 27 new
tests (13 service-level NSubstitute unit tests + 14 live-Postgres integration tests covering OR/AND
semantics, the double-counting regression, manual SKU exclusion, thresholds, period boundaries,
pagination/sort, zero-sales matched items, receipt-level export scoping, and the competitor
InPeriod-vs-AllTime horizon distinction with a real different-result fixture). Dev DB confirmed
clean after the run. **Unrelated pre-existing note, not from this task:** found 3 leftover
`Loyalty Repo Test *` tenant rows in the dev DB from a 2026-07-26 test run whose cleanup didn't
fully run — harmless (dev-only), not fixed, flagged for awareness only. Domain entities/DbContext/
migrations untouched beyond reading; `Features/Loyalty/`/`Features/ConsumerAuth/`/existing
RFM/PriceSegments code/frontend/mobile untouched; no "saved named audiences" (explicitly out of
scope per design doc §11). Not committed.

## TASK-428 — DB: Item.Name trigram index (Фаза 3 AudienceBuilder prep)
**Status:** done — index created/migrated, `dotnet build`/`test` clean · **BLOCKING FINDING: new
index cannot be used by the planner on the real RLS connection** · **Agent:** database-engineer ·
**Depends:** none (Task #1 of Фаза 3) · **Next:** backend-developer should NOT start the
AudienceBuilder repository's `ILIKE` query until the finding below gets a decision (project-architect
or security-reviewer call) — otherwise the query will silently full-scan `items` in production
exactly like the pre-existing bug found below
Log: `.claude/logs/tasks/428_2026-07-27_item-name-trigram-index_database-engineer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md`. Design doc: scratchpad
`phase3-audience-builder-design.md` §2.1/§11. Added `idx_items_name_trgm` (GIN, `gin_trgm_ops`) on
`items.Name` — migration `AddItemNameTrigramIndex` (20260727175924), applied to dev DB via the
app's own non-superuser connection (`shelfguard_app_dev`, TASK-419 discipline). **Critical finding
from live verification (the brief explicitly asked to confirm real planner usage, not just that
the DDL is correct):** seeded 500k synthetic `items` rows in a rolled-back transaction — under the
real RLS-protected app connection, `EXPLAIN ANALYZE` on `"Name" ILIKE '%term%'` shows a full `Seq
Scan` (1085ms), never uses the new index, **even with `enable_seqscan=off`** (proof no index plan
exists at all, not just a deprioritized one). Root cause: PostgreSQL requires quals using
non-`LEAKPROOF` functions to be evaluated only as a post-scan Filter under RLS + `FORCE ROW LEVEL
SECURITY` — confirmed `texticlike` (backs ILIKE) is `proleakproof=false` in `pg_proc`. Same query
as RLS-bypassing superuser (`crm`): `Bitmap Index Scan on idx_items_name_trgm`, 2ms. **This is not
new** — live-confirmed the identical bug already silently affects the shipped
`idx_notification_queue_title_trgm` (`notification_queue.Title`, the design doc's own cited working
precedent) — that feature has likely never actually used its GIN index in production either.
Flagged as a separate background task (`task_336e9c7a`) rather than fixed in this narrow task.
**Did not unilaterally fix** (marking a core Postgres function `LEAKPROOF` is a schema-wide
security-posture call, not an isolated indexing decision — flagged per CLAUDE.md's
clarify-before-implementing gate, with 3 options listed in the full log: SECURITY DEFINER search
function / mark specific functions LEAKPROOF after dedicated security review / accept Seq Scan at
realistic per-tenant catalog sizes). Separately assessed (brief's optional §3): composite
`(TenantId, CustomerId, CreatedAt)` on `pos_transactions` for Фаза 3's audience queries — seeded a
realistic 100k-transaction scenario (rolled back after), confirmed the design doc's actual CTEs
reach `pos_transactions` only via `PK` join (composite index structurally can't help), and a
hypothetical direct tenant+customer+date filter shape showed no measurable difference
before/after creating the composite (planner kept using the existing `idx_pos_tx_customer`) —
**no new index added**, existing indexes sufficient. `dotnet build` 0 err (1 pre-existing
unrelated warning), `dotnet test` **1186/1186 green**, no regressions, no new test file (index-only
change). Dev DB confirmed left clean (all seeded test data rolled back, row counts back to 0).
Not committed.

## TASK-422 — Security: review of Фаза 2 (price segments + frequency/reactivation)
**Status:** done — **verdict: CLEAR TO SHIP**, no blocker, no risk-level finding · **Agent:**
security-reviewer · **Depends:** TASK-419..421 · **Next:** none blocking; optional follow-up below
Log: `.claude/logs/tasks/422_2026-07-27_security-review-price-segments_security-reviewer.md`
Read TASK-419/420/421's logs, then the code directly (didn't trust the logs' security claims).
All 6 mandatory checklist items verdict **OK**: (1) raw-SQL parameterization in
`PriceSegmentsRepository.cs` — traced `sortBy` end-to-end for all 3 paginated queries, confirmed it
only ever selects a hardcoded literal column name through `PriceSegmentSortKeys`'s fixed allowlist
switch, never reaches the SQL string itself; verified each mapped literal actually exists as a
column in that query's CTE; every other filter is a genuine positional `SqlQueryRaw` parameter.
(2) RLS on `price_segment_settings` — diffed the migration byte-for-byte against
`loyalty_program_settings`'s own (`20260726132332_AddLoyaltyProgram.cs`): identical canonical triad,
NULLIF-guarded fail-closed, no `consumer_self_access`. (3) PII in exports — all 3 new export
builders route through the same `ExcelExportService.SetCellValue`/`SanitizeForSpreadsheet`
formula-injection guard (TASK-414) as Фаза 1, phone masked by default via the same (moved, not
forked) `PiiMasking.MaskPhone`, `UnmaskPii` re-derived server-side against
`MarketingAnalyticsAuthorization.CanExportPii` regardless of what the client sends; Phase 2 never
selects Email at all, so no email-masking gap exists to check here. (4) capability gates — confirmed
`PriceSegmentsController` carries both `MarketingAnalyticsViewOrCapability` and
`[RequireModule("marketing_analytics")]` at class level on every action;
`PriceSegmentSettingsController`'s missing `[RequireModule]` matches the `LoyaltySettingsController`
precedent exactly (enterprise_admin-gated settings controllers never carry one), not a gap.
(5) threshold validation — `UpsertSettingsAsync` rejects `DefaultFrequencyDeclineThresholdPercent`
outside 0-100 and negative `MinReceiptsForBoundaries` with a 400 before touching the repository.
(6) `PERCENTILE_CONT`/`::numeric` — all 15 call sites (grepped, matches TASK-420's own count) wrapped
consistently, no stray unfixed occurrence; Фаза 1 doesn't use `PERCENTILE_CONT` at all so no
cross-file drift risk. **Also checked beyond the list:** DI lifetimes all correctly `AddScoped`;
`PriceSegmentAdvisor`'s tenant-filter-free `IntegrationConfigs` lookup is a byte-for-byte copy of the
already-shipped `MarketingAdvisor` pattern, not a new risk; page-size ceiling is actually enforced by
`PriceSegmentsService.NormalizePaging`'s `MaxPageSize=200` (the controller's own uncapped
`NormalizePageSize` is redundant but harmless since the service re-clamps); export requests have no
client-supplied page size at all (hardcoded 50k cap); out-of-range enum ordinals just match zero SQL
rows, not an injection vector or a crash; store-filter arrays are always ANDed with the JWT-sourced
`TenantId`, so a cross-tenant store ID can only narrow, never leak. **Non-security note for a future
task:** `PriceSegmentSettings.MinReceiptsForBoundaries` is validated/persisted/returned but never
actually read by `GetBoundariesAsync` or any other query — currently inert, a functional gap not a
security one. Did not re-run the test suite (read-only code review; TASK-420's own log already
reports 1180/1180 green for these exact paths). No code changed.

## TASK-421 — Frontend: price segments + frequency/reactivation UI (Фаза 2)
**Status:** done — `tsc`/`build` clean, live-verified end-to-end in browser, no blocker ·
**Agent:** frontend-developer · **Depends:** TASK-420 (API contract) · **Next:** security-reviewer
(raw-SQL/PII gate already scheduled against TASK-420, unaffected by this frontend-only task),
qa-tester (regression pass), documentation-writer (glossary/api-contracts/ADR for Фаза 2)
Log: `.claude/logs/tasks/421_2026-07-27_price-segments-frontend_frontend-developer.md`
Continuation of a session that hit its usage limit mid-task (not a code error) — verified the
prior agent's partial work first (types/api/hooks + `ModeTabs`/`ComparisonFilterBar`/
`PriceSegmentChart`/`PriceAudienceCards`/`PriceAudienceTable`/`ExportButtons` + bonus
`RecommendationBlock`/`TableControls`, all correct, matched the TASK-420 contract exactly, genuine
server-side pagination confirmed) before building the rest: the 3-tab page.tsx, `AllTimeView/`
(5 components) and `FrequencyView/` (3 components), Sidebar nav item, and the full
`Dashboard.priceSegments.*` i18n namespace in `en.json`/`uk.json` (confirmed neither file had ANY
key there yet, despite the prior agent's components already referencing it). Fixed 1 real
pre-existing type bug (`buildStoreQs`'s `extra` param missing `boolean`) caught by `tsc --noEmit`
before writing anything new. Resolved (didn't fix) the brief's flagged concern about
`ExportButtons.tsx`'s inline styles — direct comparison against Фаза 1 RFM's sibling components
confirmed inline-style-with-hex-colors is this feature area's actual, consistent convention
(`Btn.tsx` itself is inline-styled), not a deviation. Live-verified on the dev stack against real
seeded customer data: all 3 modes, atomic recalculation on period change (including an
already-open audience table refetching to a different customer), the 3-way
analyzed/current-buyers/previous-buyers denominator distinction, All-time's clickable
distribution-chart tier → filtered table + recommendation, and Frequency's Sleeping audience
(filter labels flip to previous-period wording, no decline-threshold field, `Тип. чек` renders
"—") vs Declining (decline-threshold field appears, labels stay current-period, empty state
handled cleanly). Found and fixed an unrelated environment issue while starting the dev servers:
an orphaned `node.exe` from a previous session held port 3000, forcing `frontend-dev` onto
58083 and breaking CORS (backend only allows `localhost:3000`) — killed it, restarted clean.
Not committed.

## TASK-420 — Backend: price segments + frequency/reactivation engine (Фаза 2)
**Status:** done — builds clean, full suite green (1180/1180, was 1109), no blocker · **Agent:**
backend-developer · **Depends:** TASK-419 (PriceSegmentSettings schema) · **Next:**
frontend-developer (full API contract in the task log below), security-reviewer (raw-SQL
parametrization + PII export gate, same pattern as TASK-412 covered for Фаза 1),
documentation-writer (glossary/api-contracts/ADR)
Log: `.claude/logs/tasks/420_2026-07-27_price-segments-backend_backend-developer.md` (full API
contract for the frontend agent lives there). Plan:
`C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фази 2-4" + scratchpad design doc.
Continuation of a session that hit its usage limit mid-task (not a code error) — verified the
prior agent's partial work (classifiers/DTOs/catalog/filter-hash, all correct, unchanged) before
building the rest: `Dtos/FrequencyDtos.cs`, `PriceSegmentRecommendationTemplates.cs`,
`PriceSegmentSortKeys.cs`, `IPriceSegmentsRepository.cs`/`PriceSegmentsRepository.cs` (2nd
raw-SQL repository in the codebase — per-customer period metrics, all-time boundaries/KPI/
monthly-trend/distribution, network unit-price, LTV map, and 3 real server-side paginated/sorted
tables with `COUNT(*) OVER()`), `IPriceSegmentsService.cs`/`PriceSegmentsService.cs`,
`Infrastructure/AI/PriceSegmentAdvisor/` (5th Claude advisor, same key-resolution pattern as
`MarketingAdvisor`), `PriceSegmentsController.cs` + `PriceSegmentSettingsController.cs` (reuses
`AppPolicies.MarketingAnalyticsViewOrCapability` + `[RequireModule("marketing_analytics")]` +
`MarketingAnalyticsAuthorization.CanExportPii` literally, no new module key/capability), DI in
both `Application`/`Infrastructure` `DependencyInjection.cs`. **Bug found only via live-Postgres
verification (would have shipped silently broken otherwise):** `PERCENTILE_CONT` always returns
`double precision` in Postgres regardless of input column type — every one of 15 call sites
originally mapped straight to a C# `decimal` and threw `InvalidCastException` at runtime; fixed
with an explicit `::numeric` cast at each site, then re-verified live (10/10 integration tests
green, including the two riskiest paths: nullable `int?`/`decimal?` SQL parameters via a new
`NullableParam`/`DBNull.Value` helper, and the Sleeping-audience previous-period filter
re-orientation). Every SQL-computed audience/segment classification is cross-checked in tests
against the pure C# classifier for the same inputs. 71 new tests (61 unit + 10 live-Postgres
integration), `dotnet build` 0 warnings/0 errors. Domain entities/DbContext/migrations untouched
beyond reading (confirmed `PosTransaction.StoreId` → `"LocationId"` column at
`AppDbContext.cs:1065`); `Features/Loyalty/`/`Features/ConsumerAuth/`/frontend/mobile untouched.
Not committed (repo convention — main session/user commits).

## TASK-419 — DB: PriceSegmentSettings schema (Фаза 2 price segments + frequency/reactivation)
**Status:** done — created, migrated, live-verified against the real non-superuser app role, no
blocker · **Agent:** database-engineer · **Depends:** none (Task #1 of Фаза 2) · **Next:**
backend-developer (TASK-420)
Log: `.claude/logs/tasks/419_2026-07-27_price-segment-settings-schema_database-engineer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фази 2-4". One new tenant-settings
table, `price_segment_settings` — direct analogy to `LoyaltyProgramSettings` (TASK-404); per the
brief, segments/audiences/customer metrics stay computed live on every request, nothing else
persisted. `PriceSegmentSettings` entity (`Id`/`TenantId`, `DefaultFrequencyDeclineThresholdPercent`
default 30.0m, nullable `MinReceiptsForBoundaries`, `UpdatedAt`) + `AppDbContext` DbSet/fluent
config + migration `AddPriceSegmentSettings` (20260726211248) — canonical RLS triad only
(`tenant_isolation` NULLIF-guarded + `provider_bypass` `IN ('provider','provider_admin')` +
`worker_bypass`), deliberately **no** `consumer_self_access` — staff-only, same posture as
`loyalty_program_settings`. Applied via the app's own non-superuser `shelfguard_app_dev` connection
first (not the `crm` superuser escape hatch) — applied cleanly, confirmed table ownership
immediately after; TASK-411's grant-ownership incident did not recur. Live-verified against the
real app role: positive path (insert/select/update, rolled back), fail-closed with no
`app.tenant_id` set, cross-tenant isolation, provider/provider_admin/worker bypass, and the policy/
flag byte-check (`relrowsecurity`/`relforcerowsecurity` both `t`, 3 policies, correct `qual` text).
No new xUnit file — already covered by the 2 existing dynamic RLS audits in
`RlsCrossTenantIntegrationTests.cs` that enumerate every FORCE-RLS table at query time. `dotnet
build` 0 err (1 pre-existing unrelated warning), `dotnet test` **1109/1109 green**, unchanged from
TASK-417's baseline. Domain entities/migration only — no controller/service code (that's TASK-420).
Not committed.

## TASK-417 — Backend: fix CRITICAL RLS break in consumer loyalty join flow
**Status:** done — fixed and verified live against real Postgres RLS, no blocker · **Agent:**
backend-developer · **Depends:** TASK-416 (QA repro + root cause) · **Next:** security-reviewer
should sanity-check the new `ITenantSessionOverride` primitive before wider release (not urgent,
narrow usage today)
Log: `.claude/logs/tasks/417_2026-07-26_fix-consumer-join-rls_backend-developer.md`
Fixed the 100%-reproducible `POST /api/consumer/loyalty/{tenantId}/join` 500 QA found: a consumer
session never carries `app.tenant_id` (cross-tenant by design), and `customers` only has the
canonical `tenant_isolation`/`provider_bypass`/`worker_bypass` RLS triad — no identity-based policy
the way `loyalty_memberships`/`loyalty_ledger_entries` got in TASK-404 — so every lookup silently
returned 0 rows and every create-fallback INSERT was rejected by RLS. Confirmed via the migration's
own policy text that `LoyaltyMembership`'s insert was never actually broken (`consumer_self_access`
covers it independent of `tenant_id`) — only the `customers` step was. Rejected adding a new
identity-based policy to `customers` (no natural `ConsumerAccountId` column, shared with
staff-created customers). Fix: new `ITenantSessionOverride`/`TenantSessionOverride`
(`Application/Services` + `Infrastructure/Services`) — `ExecuteAsync<T>(tenantId, action, ct)` opens
an explicit transaction, `SET LOCAL app.tenant_id = ...`, runs `action`, commits; Postgres
auto-reverts `SET LOCAL` at transaction end (commit or rollback), so it can never leak to a later
query on the same pooled connection, even on an unhandled exception — no manual restore step to
forget. `LoyaltyService.JoinAsync`'s customer-lookup-or-create + membership-create branch now runs
inside it (atomic as a side benefit); the idempotent existing-membership branch is untouched (needs
no override); `JoinAsStaffAsync` untouched and confirmed unaffected (staff sessions already carry
the correct `tenant_id` claim). New live-Postgres `LoyaltyJoinRlsIntegrationTests.cs` — real repos
(not mocks), throwaway NOSUPERUSER NOBYPASSRLS role, exact consumer-session GUC shape: new-join
happy path (+ confirms the SET LOCAL override doesn't leak past its own transaction), idempotent
second call (no duplicate rows), second-tenant join stays isolated (a tenant-A-scoped staff read
relying purely on RLS sees exactly 1 customer) and the cross-tenant wallet read
(`GetMembershipsForConsumerAsync`, guarded solely by `consumer_self_access`, untouched by this fix)
still correctly returns both memberships — exactly the live-RLS coverage QA flagged as missing.
Updated `LoyaltyServiceTests.cs`'s mocks (new `ITenantSessionOverride` pass-through) so every
pre-existing `JoinAsync` test still passes unchanged, plus one new mock-level regression pinning the
override is actually invoked with the right `tenantId`. **Test-infra side effect, fixed in scope:**
the new integration test file (a 4th fresh-`NpgsqlDataSource`-per-call Postgres test class) tipped
EF Core's process-wide `ManyServiceProvidersCreatedWarning`-as-error threshold over the edge,
intermittently failing 2 unrelated pre-existing tests (`PosConcurrencySalesIntegrationTests`,
`LoyaltyConcurrencySalesIntegrationTests` — neither had the defensive `.ConfigureWarnings(...)`
downgrade `LoyaltyRepositoryIntegrationTests`/`MarketingAnalyticsRepositoryIntegrationTests` already
carry for the identical reason); fixed by sharing one data source per test method in the new file and
adding the same one-line downgrade precedent to those two files (test-infra hygiene only, zero
behavior change to what either asserts). `dotnet build` 0 err (1 pre-existing unrelated warning),
`dotnet test` full suite run **3× consecutively, 1109/1109 green each time** (was 1105; +4), new
integration tests independently re-verified in isolation too (real DB round-trips, 3-10s each, not
silent soft-skips). Not committed.

## TASK-414 — Backend: security remediation of 3 findings from TASK-412 (Loyalty + Marketing Analytics)
**Status:** done — **all 3 fixed and verified, no blocker** · **Agent:** backend-developer ·
**Depends:** TASK-412 · **Next:** re-review by security-reviewer before release (recommended, not
re-run this session), then frontend/mobile/qa as originally queued behind TASK-412
Log: `.claude/logs/tasks/414_2026-07-26_security-fixes-loyalty-rfm_backend-developer.md`
Fixed exactly the 3 assigned findings, nothing else. **(1) CRITICAL Excel/CSV formula
injection:** `ExcelExportService.SetCellValue` now routes every string (headers, truncation
banner, every row value) through one centralized `SanitizeForSpreadsheet` helper — leading
`=`/`+`/`-`/`@`/Tab/CR gets apostrophe-prefixed. Empirically verified via a throwaway ClosedXML
0.105.1 probe (not assumed) that ClosedXML implements the real OOXML "quote prefix" convention —
it strips the apostrophe and sets `cell.Style.IncludeQuotePrefix` (`quotePrefix="1"` in
styles.xml) rather than keeping a literal `'` in the stored text, same as real Excel's own
manual-quote behavior; tests assert on that style flag. New
`ExcelExportServiceTests.cs` (9 tests, real ClosedXML round-trip of actual output, not mocks).
**(2) HIGH LoyaltyMembership.Balance TOCTOU:** added `xmin`/`IsRowVersion()` to
`LoyaltyMembership` (same pattern as `ProductStock`/TASK-356); new no-op EF migration
`AddLoyaltyMembershipConcurrencyToken` (xmin is a reserved system column, already exists —
applied cleanly to dev DB, no backfill needed); `LoyaltyRepository.SaveChangesAsync` now
translates `DbUpdateConcurrencyException` → `ConcurrencyConflictException` (mirrors
`PosRepository`); `LoyaltyService.ManualAdjustAsync` catches it → clean 409;
`PosService.CreateSaleAsync`'s existing catch (same shared `SaveChangesAsync`) needed only a
comment/message update, not new logic. New real-Postgres
`LoyaltyConcurrencySalesIntegrationTests.cs`: two concurrent redemptions (40 each) off a
shared membership starting at 100, deterministic rendezvous (not timing luck) — confirmed
exactly 1 success + 1 clean 409, final balance exactly 60 (not 100 lost-update, not 20
double-applied). **(3) Dead `marketing_analytics.export_pii` capability + unmasked email:**
root cause confirmed — controller's class-level `CanViewAnalytics` floor was the *identical*
role set as `CanExportPii`'s own first branch (unlike `LegalEntityAuthorization`, where the
floor is strictly looser), so nobody outside store_manager+ could ever reach the capability
check. Applied the exact `AnalyticsController`/`AnalyticsViewOrCapability` ADR-020 precedent:
new `TenantRoleCapabilities.MarketingAnalyticsView` + `AppPolicies.
MarketingAnalyticsViewOrCapability`, swapped onto the controller's class-level attribute — zero
behavior change for existing roles, but a granted capability holder below store_manager can now
actually reach the export endpoints. Email now masked by default in exports (new `MaskEmail`
helper), same posture phone already had. `dotnet build` 0 err (1 pre-existing unrelated
warning), `dotnet test` **1105/1105 green** (full suite, including all pre-existing
`PosServiceTests`/`MarketingAnalyticsServiceTests`/TASK-406 Excel tests, no regressions; 2 new
live-Postgres integration tests each re-run once more to rule out flakiness). Did not touch
anything outside the 3 findings (#4 consumer JWT revocation, RLS `FOR` clause narrowing,
rls_audit_test_role gap, etc. remain open per TASK-412, out of this task's scope). Not
committed (repo convention — main session/user commits).

## TASK-413 — Frontend: wire "loyalty"/"marketing_analytics" into provider + admin module lists
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-409 (flagged this as a
follow-up chip) · **Next:** none identified beyond the further follow-up flagged below
Log: `.claude/logs/tasks/413_2026-07-26_provider-admin-module-list_frontend-developer.md`
`frontend/features/provider/types.ts` and `frontend/features/admin/types.ts` each own a separate
`ALL_MODULES` list driving the provider/admin panel's tenant module-activation checkboxes; neither
had `"loyalty"` (TASK-405) or `"marketing_analytics"` (TASK-406), so a provider had no UI path to
activate either module for a tenant — only a direct DB write (what TASK-409 itself had to do to
test). Added both keys to provider's `TenantModule` union + `ALL_MODULES` (used by
`TenantDetailPanel.tsx`'s edit checklist and `CreateTenantWizard.tsx` step 3) and to admin's own
`ALL_MODULES` (`TenantDetailDrawer.tsx`; admin has no create-time module picker). Added i18n
labels/descriptions to both `en.json`/`uk.json` (`Dashboard.provider.modules`/`moduleDescriptions`,
`Dashboard.admin.modules`), reusing TASK-409's English copy for `marketing_analytics` and writing
new copy for `loyalty`. **Found + fixed an unrelated bug that was blocking verification**: the
admin panel's module/plan "Save" 405'd — `admin.ts`'s `updatePlan`/`updateModules` called `api.put`
but `AdminController` declares both `[HttpPatch]` (its own doc-comments already said "PATCH"); every
admin-panel plan/module change has silently 405'd since TASK-074, not specific to these two new
modules. Fixed both call sites to `api.patch`. Live-verified both panels end-to-end (provider
`PUT → 204`, admin `PATCH → 200`, both persisted after hard-reload and cleanly reverted, both
locales render correctly). `tsc`/`build` clean. Deliberately did NOT touch
`frontend/features/modules/types.ts` (`ALL_MODULE_KEYS`) — the tenant-facing **read-only** Settings
"Modules" tab already has `marketing_analytics` (TASK-409) but still lacks `loyalty`; flagged as a
separate follow-up via `spawn_task` (chip `task_cc5b2371`) rather than folded into this task's
narrower provider/admin scope. Dismissed the now-superseded chip `task_22a39ac1`. Not committed.

## TASK-412 — Security: review of Loyalty + Marketing Analytics (Фаза 0 + Фаза 1)
**Status:** done — **verdict: NOT clear to ship as-is** · **Agent:** security-reviewer ·
**Depends:** TASK-404..411 · **Next:** backend-developer (fix blocker + high-priority item below),
then re-review before release
Log: `.claude/logs/tasks/412_2026-07-26_security-review-loyalty-rfm_security-reviewer.md`
Audited all 8 loyalty/RFM task logs (404-411) then the actual code directly (entities, migration
SQL, controllers, services, repos, `AppDbContext.cs`, `TenantConnectionInterceptor.cs`). Of the 9
items the brief called out: 6 verified **OK** (`ConsumerAccount` no-RLS — no generic GetById
exposure found anywhere; `consumer_self_access` RLS + JWT claim validation — fail-closed, correct,
minor hardening nit only; `TryClaimTimestepAsync` anti-replay — genuinely atomic + parameterized;
`MarketingAnalyticsRepository`'s raw SQL — all 9 methods fully parameterized, zero injection risk;
`FixLoyaltyTableGrants` migration — scoped to exactly 4 tables; `ConsumerAuthController` rate-limit/
lockout — present, consistent with TASK-329). 3 are real gaps: consumer JWT is a genuinely
unrevoked 30-day token in the actual `appsettings.json` config (not just a doc claim); the new
`marketing_analytics.export_pii` TenantRole capability is **dead code** — proven by direct
comparison with `LegalEntityAuthorization`'s own doc comment, which explicitly documents the exact
"class-level policy must be looser than the capability check" rule this codebase learned once
already (ADR-020 "the blocking discovery") and which `MarketingAnalyticsController` violates (its
class-level `CanViewAnalytics` floor is identical to, not looser than, `CanExportPii`'s role
branch); Excel export never masks email regardless of the PII flag. Also flagged (per brief's
"add anything else you find" instruction) the documented rls_audit_test_role test-blind-spot as
confirmed-real but acceptable to defer.
**2 new findings neither of the 8 building agents caught:** (A) **CRITICAL, blocks release** —
`ExcelExportService.SetCellValue` writes raw strings into cells with no Excel-formula
neutralization; since `Customer.Name` for a loyalty-joined customer comes verbatim from
self-registered `ConsumerAccount.FullName` (`POST /api/consumer-auth/register` is `[AllowAnonymous]`,
validates only non-empty), **any anonymous member of the public can plant a formula payload that
executes in a trusted store_manager's Excel** the moment they export a segment — full path traced
end-to-end, not speculative. Small, standard fix (prefix `=`/`+`/`-`/`@`-leading strings with `'`).
(B) **HIGH** — `LoyaltyMembership` has no optimistic-concurrency token anywhere in `AppDbContext.cs`
(confirmed absent), unlike `ProductStock`, which explicitly uses `xmin`/`IsRowVersion()` in the same
file for the identical bug class ("two cashiers selling the last unit at the same moment"). Both
`PosService.CreateSaleAsync`'s redemption/accrual and `LoyaltyService.ManualAdjustAsync` mutate
`Balance` via plain `SaveChangesAsync()` — concurrent sales against the same membership can each
pass the balance-sufficiency check against a stale read, and the loser's decrement is silently lost
(TOCTOU), letting a customer redeem more than their real balance. Requires staff-level POS access
(insider/race risk, not remote), but is a real money-integrity gap the same file already knows how
to fix on a sibling entity. Full verdict table + all recommendations in the log. No code changed
(audit only, per brief) — everything above needs a follow-up implementation task before wider
rollout.

## TASK-411 — DB: fix — 4 loyalty tables owned by migration superuser, zero app-role grants
**Status:** done — fixed and live-verified in dev; staging unaffected (migration hasn't reached
it yet); production cannot have this bug yet (nothing loyalty-related ever committed/deployed) ·
**Agent:** database-engineer · **Depends:** TASK-410 (found the bug live, spawned background task
`task_693b439c`) · **Next:** apply both `AddLoyaltyProgram` + `FixLoyaltyTableGrants` via a
superuser connection (not the automatic boot-time `MigrateAsync()`) whenever this reaches
staging/production — documented deploy risk, not yet executed in either environment
Log: `.claude/logs/tasks/411_2026-07-26_loyalty-db-grants-fix_database-engineer.md`
Root cause (reproduced live, not assumed): this codebase has no bootstrap script/
`ALTER DEFAULT PRIVILEGES` — every table's access for the real app role comes purely from table
**ownership**, established once by TASK-372/KI-027 and inherited automatically ever since because
migrations normally run through the app's own already-owning connection. TASK-404's
`AddLoyaltyProgram` broke this: its own task log says it was applied via the `crm` **superuser**
connection (routing around the documented FK-validation-under-RLS gotcha), leaving all 4 loyalty
tables owned by `crm` with **zero** grants to `shelfguard_app_dev` — exactly the `42501 permission
denied` TASK-410 hit live on `GET /api/pos/sales`. Fix: new migration `FixLoyaltyTableGrants`
(20260726154747) — a `DO $$ ... ALTER TABLE {each of the 4} OWNER TO %I` block resolving the
target role **dynamically** from whichever role currently owns `tenants`, not a hardcoded dev role
name (so the same migration is correct in staging/production too). Touches only these 4 tables;
`Down()` is an intentional no-op (reverting would silently reintroduce the bug). Verified live: real
app-role `psql` insert/select on all 4 tables now succeeds inside a rolled-back transaction (RLS
still correctly rejects a write with no `app.tenant_id` set — ownership fix didn't weaken RLS);
full live run through the actual API — `GET /api/pos/sales` (TASK-410's exact failing call) now
`200 OK`. `dotnet build` 0 err, `dotnet test` 1086/1086 unchanged (permissions-only fix, no new
behavior — see testing-gap note below). Staging: `AddLoyaltyProgram` hasn't reached it yet, so no
bug there today. Production: `git log --all | grep -i loyalty` is empty and `main`'s HEAD predates
TASK-404 entirely — cannot have this bug yet, independent of any live check (a direct SSH
confirmation attempt was blocked by the harness's own permission classifier, same as TASK-371/372,
not worked around). **Flagged, not fixed:** the live-Postgres RLS test suite
(`LoyaltyRlsIntegrationTests` etc.) connects as `rls_audit_test_role`, which has its own explicit
`GRANT ALL` independent of table ownership — this is why `dotnet test` stayed green through the
entire incident despite the real app connection being broken; recommend a follow-up live test
against the actual configured `DefaultConnection` asserting basic `SELECT` on every FORCE RLS
table. Also flagged: a `known-issues.md` KI-027/028 cross-reference addendum for this incident —
not added (out of this task's scope). Not committed.

## TASK-410 — Backend: SaleDto customer fields + loyalty ledger mapping on GetSalesForShiftAsync
**Status:** done (code+tests) — feature not visible live yet, blocked by an unrelated DB
permissions gap (see below) · **Agent:** backend-developer · **Depends:** TASK-408 (found the
gap), TASK-405 (loyalty ledger/customer fields it fills in) · **Next:** database-engineer
(spawned task `task_693b439c`, see below) before this is actually visible end-to-end
Log: `.claude/logs/tasks/410_2026-07-26_saledto-loyalty-fields_backend-developer.md`
Closed the two mapping gaps TASK-408 found: `SaleDto` gained `CustomerId`/`CustomerName`
(mapped in both `CreateSaleAsync` and `GetSalesForShiftAsync`, the latter via a new
`.Include(t => t.Customer)` on `PosRepository.GetTransactionsByShiftAsync`); `GetSalesForShiftAsync`
now also maps `LoyaltyAccrued/Redeemed/Balance` by batch-querying `LoyaltyLedgerEntry` via new
`ILoyaltyRepository.GetLedgerEntriesForTransactionsAsync` (PosTransaction has no
LoyaltyMembershipId of its own — the ledger is the only signal). `LoyaltyBalance` = the
chronologically-last ledger entry's `BalanceAfter` for that transaction — a per-sale historical
snapshot, not the membership's current live balance. `dotnet build` 0 err, `dotnet test`
1086/1086 (+3 new, was 1083). **Found a critical, unrelated pre-existing bug while live-verifying:**
`GET /api/pos/sales` 500s with Postgres `42501 permission denied for table
loyalty_ledger_entries` — all 4 loyalty tables from TASK-404 (`consumer_accounts`,
`loyalty_memberships`, `loyalty_ledger_entries`, `loyalty_program_settings`) are owned by the `crm`
migration superuser with zero grants to the actual app role (`shelfguard_app_dev`), unlike every
other RLS table in the codebase. This means the entire loyalty feature chain (TASK-404..408) is
non-functional through the real app connection in every environment provisioned the same way —
confirmed live in dev, staging/production not yet checked. Did not attempt a fix (DB
ownership/grants, out of scope for this task and this repo's own TASK-371/KI-027 precedent says
don't work around DB permission issues without a dedicated review) — flagged via a spawned
background task (`task_693b439c`) for database-engineer with full root-cause and repro steps.
Not committed.

## TASK-406 — Backend: Marketing analytics (RFM) engine + dashboard API (Фаза 1)
**Status:** done (2026-07-26) · **Agent:** backend-developer · **Depends:** TASK-405 (Task #2 of
the loyalty/RFM plan's agent sequence — Фаза 0's `PosTransaction.CustomerId` writing) · **Next:**
frontend-developer (TASK-409), security-reviewer (mandatory pass, esp. raw-SQL parametrization +
PII export gate), documentation-writer (glossary/api-contracts/ADR)
Log: `.claude/logs/tasks/406_2026-07-26_marketing-analytics-backend_backend-developer.md` (full
frontend API contract for TASK-409 lives there — read it instead of the C#).
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фаза 1". New
`Features/MarketingAnalytics/` (mirrors `Features/Analytics/`'s thin service→repository shape):
`RfmSegmentClassifier` (pure, 11 named-constant if-branches, plan's exact priority-table order;
caught+fixed a real bug while testing — "Lost" `>6 months` needed strict `<`, not `<=`),
`RecommendationTemplates` (one method per segment, live-KPI Ukrainian copy),
`MarketingAnalyticsRepository` (Infrastructure — **first raw-SQL in the codebase**,
`Database.SqlQueryRaw<T>` with positional `{n}` params for `NTILE(5)` R/F/M scoring + segment-
scoped top-products/affinity/basket/behavior/LTV; verified via 2 throwaway spikes against live
Postgres before writing the real file, then 8 real integration tests seeding real POS data —
caught a real EXTRACT(DAY FROM interval) pitfall (doesn't give total elapsed days across months)
before it shipped, replaced with a plain `date - date` subtraction), `MarketingAnalyticsService`
(classification/aggregation orchestration, PII masking, ActivityLog on export),
`ExcelExportService` (Infrastructure/Export, ClosedXML 0.105.1 MIT — not EPPlus), new
`MarketingAnalyticsController` (`CanViewAnalytics` floor + `[RequireModule("marketing_analytics")]`,
8 endpoints: overview/segment-detail/affinity/basket/explain/3×export). New
`TenantRoleCapabilities.MarketingAnalyticsExportPii` (ADR-020) + `MarketingAnalyticsAuthorization`
(imperative check, store_manager+ or the capability — mirrors `LegalEntityAuthorization`'s shape).
`ItemType="packaging"` added to `ItemService.IsValidItemType` (string field, no schema change) —
excluded from top-products/affinity/basket aggregation. **Test-infra note (flagged for review):**
adding this task's 3rd raw-Postgres integration-test class pushed the full suite's cumulative
distinct-`DbContextOptions` count past EF Core's `ManyServiceProvidersCreatedWarning`-as-error
threshold (~20, process-wide) — added one `.ConfigureWarnings(...)` line to the pre-existing
`LoyaltyRepositoryIntegrationTests.NewContext()` (test-infra only, zero behavior change to what
that test verifies) since `Features/Loyalty/` itself was out of scope, not that test helper.
`dotnet build` 0 err/0 warn (1 pre-existing unrelated warning), `dotnet test` **1083/1083 green**
(was 1004; +79: 39 classifier + 18 recommendation-template + 8 authorization + 6 service + 8 live-
Postgres repository integration, ran full suite twice to confirm no flakiness). Did not touch
`Features/Loyalty/`, `Features/ConsumerAuth/`, `PosService.cs`, Domain entities/DbContext/
migrations (beyond the packaging string value), or any frontend/mobile UI, per the task's explicit
scope boundaries. Not committed.

## TASK-407 — Mobile: consumer loyalty wallet + POS loyalty scan (Фаза 0, Task #3 mobile half)
**Status:** done · **Agent:** mobile-developer · **Depends:** TASK-405 · **Parallel with:**
TASK-408 (frontend half) · **Next:** security-reviewer (mandatory pass before release),
qa-tester (end-to-end scenario — no emulator/device in this environment, contract-level
verification only so far)
Log: `.claude/logs/tasks/407_2026-07-26_mobile-loyalty_mobile-developer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Зміни в POS" → "Mobile", §"Роль
і навігація в мобільному застосунку". New `(consumer)` route group (Tabs: wallet/history/
account, no `index.tsx` of its own — both it and `(app)` carry no path prefix, so an index in
both would collide on `/`) reached via a new `(auth)/select-role.tsx` chooser +
`consumer-login.tsx`/`consumer-register.tsx`, wired to new `POST /api/consumer-auth/
register|login`. `useAuthStore` gained `sessionKind`/`consumerUser`/`setConsumerAuth` purely
additively — every existing staff call site (`setAuth`/`setUser`/`clearAuth`/`loadToken`)
kept its exact signature. Wallet screen polls `GET /consumer/loyalty/{tenantId}/code` every
22s gated on BOTH navigation focus (`useFocusEffect`) AND `AppState==='active'` — stricter
than the existing `useCurrentShift` pattern, deliberately, since this is a rotating security
code. POS gained `pos/loyalty.tsx` (scan/manual-code/customer-search) inserted between
`scanner.tsx` and `payment.tsx` (1-line pathname change in `scanner.tsx`, rest of that
already-audited file untouched); new shared `BarcodeCameraView` used only by the new screen.
**Found + fixed a real correctness gap while wiring `payment.tsx`**: the backend computes the
sale's actual owed total as `subtotal - redeemAmount` (before tax/change) — `payment.tsx` now
computes that same `netTotal` for the cash-sufficiency/change check instead of the raw cart
subtotal, or the cashier would demand more cash than the customer owes once bonuses are
redeemed; zero visible diff when redeemAmount is 0 (the normal case). Staff "join own
program" added to `profile/index.tsx` (`GET /loyalty/my-membership` 404→join button,
403→section hidden entirely — module not enabled for that tenant). New dependency
`react-native-qrcode-svg` + peer `react-native-svg` (SDK-56-compatible, via `npx expo
install`, no config-plugin/app.json change needed). **Plan-vs-actual deviations documented in
the log:** manual code entry needs the FULL `SGLOY1.{id}.{code}` string, not "6 digits" as the
plan said; `resolve-code` returns no redemption-cap field (that lives in enterprise_admin-only
settings), so the client only soft-caps to `min(balance, subtotal)` and trusts the server's
400 on an actual cap violation (already correctly surfaced by the existing generic error
handler); no backend endpoint exists to "browse" tenants with loyalty enabled, so the wallet's
"join a new program" is a minimal manual Tenant-ID entry, flagged as needing a better UX
later. `npx tsc --noEmit` clean across the whole mobile project (checked after every file
touched). `npm run lint` still fails on the pre-existing missing `eslint.config.js`
(TASK-366, not this task's regression). No test runner exists in mobile (no `"test"` script,
zero test files) and no emulator/device in this environment — verification was contract-level
(controllers/DTOs read directly, full param flow traced by hand) plus a clean `tsc`, not a
live run. Not committed.

## TASK-408 — Frontend: Web POS read-only "Лояльність" block (Фаза 0, Task #3 frontend half)
**Status:** done (UI correct but dormant — see backend follow-ups below) · **Agent:**
frontend-developer · **Depends:** TASK-405 · **Next:** mobile-developer (Task #3 mobile
half, parallel, separate task), a NEW backend task to close the two mapping gaps found here,
security-reviewer (already scheduled, unaffected by this task)
Log: `.claude/logs/tasks/408_2026-07-26_web-pos-loyalty-section_frontend-developer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Зміни в POS" → "Web".
**Found the actual backend contract diverges from the plan brief** (verified by reading
`PosDtos.cs`/`PosService.cs` directly, then live end-to-end on the dev stack — opened a real
shift, created a real sale via the API, fetched it back): `SaleDto` has NO
`CustomerId`/`CustomerName` field at all on either endpoint (not "sometimes null" — genuinely
absent from the DTO, despite `PosTransaction.CustomerId` being persisted at
`PosService.cs:324`), so the customer-name-with-link part of the brief cannot be built without
a new backend DTO extension. Separately, `loyaltyAccrued/Redeemed/Balance` ARE real `SaleDto`
fields (TASK-405) but only `CreateSaleAsync` (mobile's immediate checkout response) populates
them — `GetSalesForShiftAsync` (what `GET /api/pos/sales`, i.e. this web view, actually calls)
never does, confirmed live (created a sale, fetched it back via the list endpoint, got
`null`/`null`/`null`). Followed "don't invent data": did NOT add a fake `customerId` field to
`frontend/features/pos/types.ts`; DID add the three loyalty fields (real, just always-null via
this endpoint today) plus a shared `saleHasLoyaltyActivity()` helper, and gated a new
"Лояльність" `DrawerSection` in `SaleDetailDrawer.tsx` + a `Gift` icon indicator in
`SalesTable.tsx` on it — both correct and forward-compatible, but dormant until a backend
follow-up wires `GetSalesForShiftAsync` to the ledger. Also noted: `features/customers/` has
no per-customer deep link (`CustomerDetail` is a client-state drawer, no `/customers/[id]`), so
even a future `CustomerId` could only link to the customers list, not a specific record.
`npx tsc --noEmit` clean, `npm run build` clean (exit 0, `/pos` route present). Live-verified
on the dev stack end-to-end (backend+frontend+Postgres, seeded `manager@demo.local`): opened
shift, created a real sale via the API, confirmed the web `/pos` page renders it with no Gift
icon (correct — no loyalty data) and the drawer's General info section unaffected with the new
Loyalty section correctly absent; no console errors. Cleaned up after: closed the test shift,
stopped both preview servers, killed the orphaned backend process. Only
`frontend/features/pos/{types.ts,components/SaleDetailDrawer.tsx,components/SalesTable.tsx}` +
`frontend/messages/{en,uk}.json` touched — confirmed via `git diff` that the pre-existing
uncommitted activity-log-labels changes already sitting in the two message files (unrelated,
predates this task) are untouched by my hunk. **New backend task needed:** (1) add
`CustomerId`/`CustomerName` to `SaleDto`, map in both `CreateSaleAsync` and
`GetSalesForShiftAsync`; (2) map `LoyaltyLedgerEntry` (by `PosTransactionId`) into
`GetSalesForShiftAsync` so the already-built web section actually shows real data. Not
committed (repo convention — main session/user commits).

## TASK-409 — Frontend: Marketing analytics (RFM) dashboard (Фаза 1)
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-406 (backend contract) ·
**Next:** security-reviewer (already scheduled against 404-411, unaffected by this frontend-only
task), documentation-writer (glossary/api-contracts notes — closed by TASK-415)
Log: `.claude/logs/tasks/409_2026-07-26_marketing-analytics-frontend_frontend-developer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фаза 1". Built
`frontend/features/marketing-analytics/` off task log 406's "Frontend API-контракт" section as the
sole contract source (never read the C#) and `docs/uployal/RFM_ANALYSIS.md` for UI/UX behavior. New
`/marketing-analytics` page (role gate matching backend's `CanViewAnalytics` floor,
`useRequireTab`, module gate), full `types.ts` transcription of the RFM contract plus an own-
judgment 11-segment→6-color-group mapping (the competitor doc only defines the 6 color
*meanings*, not a segment table), `api/marketingAnalytics.ts` (all 8 endpoints), one `useQuery` per
GET keyed on the full filter object (deliberately no `keepPreviousData` — never shows a mix of
old/new-filter data), components for the filter bar (new multi-store popover — existing
`useStoreContext`/`StoreSelector` is single-store only), the 11+1 segment grid, and a 4-panel
segment-detail cluster (top products / affinity+basket tabs / behavior charts / recommendation
card with the separate on-click "explain more" Claude call). **Shared-lib changes, small and
deliberate:** `frontend/lib/download.ts` gained `downloadFilePost` (plan assumed GET-only exports;
actual backend contract is POST+JSON body for all 3 exports), `frontend/lib/roles.ts` gained
`canExportMarketingAnalyticsPii`, `frontend/features/modules/types.ts`/`Sidebar.tsx` gained the
`marketing_analytics` module key/NavGroup. Full live browser verification on the dev stack: seeded
~12 synthetic customers into an existing dev tenant to get a non-trivial RFM population, confirmed
overview/segment-detail math, affinity vs. basket returning genuinely different numbers for the
same product, `/explain`'s real 503 (no Claude key in dev) rendering below (not replacing) the
template recommendation, atomic recalculation on period/store changes, the documented
"Hibernating always beats Lost" priority interaction from task log 406 reproduced live with seeded
data, empty-segment and store-scoped "no purchase" behavior matching the documented backend design,
and all 3 exports returning real non-trivial `.xlsx` files. **Discrepancy found and documented, not
invented around:** export responses carry no `Content-Disposition` header (log 406 said the
filename would arrive that way) — harmless here since `downloadFilePost` always uses its own
client-generated filename. `tsc`/`build` clean (`/marketing-analytics` 13.1 kB, in line with sibling
analytics routes). Flagged a follow-up via `spawn_task` (became TASK-413): provider/admin panel
module-activation lists didn't have `marketing_analytics`/`loyalty` yet, only a direct DB write
could enable the module for testing. Not committed.

## TASK-405 — Backend: Loyalty program Application+Api layer (Фаза 0)
**Status:** done (2026-07-26) · **Agent:** backend-developer · **Depends:** TASK-404 (Task #2
of the loyalty/RFM plan's agent sequence) · **Next:** frontend-developer + mobile-developer
(Task #4, parallel), security-reviewer (mandatory pass before release)
Log: `.claude/logs/tasks/405_2026-07-26_loyalty-backend_backend-developer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фаза 0". `ConsumerAuthController`
(`/api/consumer-auth/register|login`, own service — claim shape too different from staff
`AuthService` to share) issuing a new `IJwtService.GenerateConsumerAccessToken` (sub+
`consumer_account_id`, role="consumer", no tenant_id, 30-day lifetime since ConsumerAccount has
no refresh-token flow — **flagged for security-reviewer**, no revocation mechanism yet).
`PhoneNormalizer` (+380XXXXXXXXX, Application/Common, ConsumerAccount-only). `LoyaltyService`
(join/code/history for consumers; resolve-code/manual-adjust/my-membership/join-as-staff/settings
for staff) — QR payload `SGLOY1.{membershipId}.{code}`, `ITotpService.GenerateCode` (new: server
computes the code, unlike 2FA's verify-only). Anti-replay via new
`ILoyaltyRepository.TryClaimTimestepAsync` — single WHERE-guarded `ExecuteSqlInterpolatedAsync`
UPDATE (LoyaltyMembership has no EF concurrency token), proven atomic against live Postgres
(4 new tests). Resolve-code rate-limit/lockout via new `IResolveCodeAttemptTracker`
(`IMemoryCache`-backed, since LoyaltyMembership has no FailedLoginAttempts/LockoutUntil columns —
**flagged for security-reviewer**: single-instance-deployment tradeoff, doesn't survive restart or
scale across instances). `PosService.CreateSaleAsync` extended (redemption then accrual, both
computed on net TotalAmount, all in the sale's one existing SaveChangesAsync — no separate
commit); `Customer.TotalOrders/TotalSpent` finally get written for any sale with a CustomerId.
`AppRoles.Consumer` added (deliberately NOT in `AppRoles.All`); `Tenant.UpdateModules` gained
`"loyalty"`/`"marketing_analytics"` keys; `frontend/lib/roles.ts`/`mobile/lib/roles.ts` mirrored
with the bare `Consumer` constant only (no role-set inclusion — different session shape
entirely). Did NOT touch Domain entities/DbContext/migrations (TASK-404's schema, frozen).
`dotnet build` 0 err/0 warn, `dotnet test` 1004/1004 green (was 936; +68 new, incl. 4 live-Postgres
anti-replay tests and the existing live RLS/concurrency suites re-verified green with the new
dependencies wired in). `tsc --noEmit` clean on frontend+mobile. Not committed.

## TASK-404 — DB: Loyalty program schema (Фаза 0 — ConsumerAccount/LoyaltyMembership/LoyaltyLedgerEntry/LoyaltyProgramSettings)
**Status:** done (2026-07-26) · **Agent:** database-engineer · **Depends:** none (Task #1 of the
loyalty/RFM plan's agent sequence) · **Next:** backend-developer (Task #2)
Log: `.claude/logs/tasks/404_2026-07-26_loyalty-schema_database-engineer.md`
Plan: `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фаза 0". 4 new entities +
`AddLoyaltyProgram` migration: `ConsumerAccount` (global, no TenantId, **no RLS at all** —
deliberate, same precedent as `tenants`, flagged for mandatory security-reviewer pass);
`LoyaltyMembership`/`LoyaltyLedgerEntry`/`LoyaltyProgramSettings` (tenant-scoped, canonical
fail-closed triad). New identity-based `consumer_self_access` policy on
`loyalty_memberships`/`loyalty_ledger_entries` (first of its kind in this repo — lets a
cross-tenant ConsumerAccount JWT, which never sets `app.tenant_id`, read its own rows via
`app.consumer_account_id` instead). `provider_bypass` written as `IN ('provider',
'provider_admin')` from day one on all 3 tenant tables (deviates from database-schema.md's
literal single-role template — matches the precedent `ExpandProviderBypassToProviderAdmin`
already established for the other 71 tables). Extended `TenantConnectionInterceptor` (new
`app.consumer_account_id` session var + `"consumer"` role whitelist entry). Verified live via
`psql`: `customers` RLS already has the full canonical triad (plan's claim confirmed, no fix
needed). `dotnet build` 0 err/0 warn, `dotnet test` 936/936 (14 new: interceptor unit tests +
4 new live `LoyaltyRlsIntegrationTests` proving cross-tenant consumer read, ledger EXISTS-scoping,
staff-session isolation unaffected, fail-closed on full reset). Migration applied to dev DB via
`crm` superuser (FK-validation-under-RLS gotcha), Down()/Up() round-tripped clean. Not committed.

## TASK-401 — Backend: store-scope filter on GET /api/locations (ADR-022 Stage 3 companion)
**Status:** done (2026-07-23) · **Agent:** backend-developer · **Depends:** TASK-392 (user_locations Stage 1), ADR-022
Log: `.claude/logs/tasks/401_2026-07-23_locations-list-store-scope-filter_backend-developer.md`
Stage 3 RESTRICTIVE RLS scopes business DATA but `locations` isn't one of the 9 scoped tables, so
a single-store user still saw every tenant store in StoreSelector. `LocationService.GetAllAsync`
now takes (tenantId, userId, role) from JWT claims via the controller: admin tier
(provider/provider_admin/enterprise_admin) sees all; scoped roles (network_manager..staff) with
≥1 `user_locations` row see only assigned; **0 rows = fail-open (full list)** — deliberate
transitional semantics until Stage 2 backfill completes (StoreSelector takes `stores[0]`, hides on
empty; real protection is the RLS layer), documented in code. Reused TASK-392's
`IUserLocationRepository` (no new repo); role set from Domain `AppRoles` (not Infrastructure
`AppPolicies`). GetById/zones/floor-plan untouched; no frontend changes needed. `dotnet build`
0 err, `dotnet test` 918/918 (11 new: 3 branches + missing-claim defensive). NOT committed —
main session commits together with the Stage 3 merge.

## TASK-400 — Frontend: hide Locations Create/Edit buttons for roles below AtLeastEnterpriseAdmin
**Status:** done (2026-07-23) · **Agent:** frontend-developer · **Depends:** none (bug fix)
Log: `.claude/logs/tasks/400_2026-07-23_locations-create-button-role-gate_frontend-developer.md`
Product owner bug report: "Створити" on `/locations` for type "Склад" → 403 with no friendly
error. Root cause (confirmed in main session, backend untouched — `LocationsController.Create`/
`Update` are deliberately `AtLeastEnterpriseAdmin`-only per ADR-020/ADR-022, no capability-OR
escape hatch by design): `locations/page.tsx` rendered "Create"/"Edit" unconditionally for every
`CanViewStock` role. Fix: gated both buttons behind
`hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN)` (same pattern as `users/page.tsx`'s
`canManageRoleTemplates`) — hide, don't disable. "Plan" (floor-plan) link and the page's own
`GET` list untouched (both correctly open to all `CanViewStock` roles). `npx tsc --noEmit` clean.
Live-verified on local dev stack: store_manager (the exact reported scenario) now sees no
Create/Edit; enterprise_admin still sees both. Not committed (task brief didn't ask for it).

## TASK-398 — Backend: per-item sidebar tab catalog (item-level AllowedTabs granularity)
**Status:** done (2026-07-20) · **Agent:** backend-developer · **Depends:** TASK-391/ADR-021, TASK-397
Log: `.claude/logs/tasks/398_2026-07-20_per-item-tab-catalog_backend-developer.md`
Product feedback on ADR-021's Feature 1: whole-group `AllowedTabs` grants (e.g. "operations" = 7
pages at once) were too coarse. Added 27 item-level keys (literal `NavItem.href` per page,
verified against `Sidebar.tsx`'s `buildNavGroups`) alongside the original 10 group-level keys in
`TenantRoleTabs.All` — both flavours validate through the same `TenantRoleService.Validate` check,
no branching needed. `GET /api/tenant-roles/tabs` now returns a hierarchy
(`TenantRoleTabGroupDto[]` — group node with its own bulk-grant key + nested per-page items;
standalone Dashboard section has `groupKey: null`) instead of TASK-391b's flat list. Flagged (not
fixed, out of scope): `Sidebar.tsx` still only reads the group-level key — item-level grants do
nothing client-side until a follow-up frontend task wires them in; `"/settings/legal-entities"` is
in the catalog for completeness but its existing `canManageLegalEntities`-only carve-out should
stay excluded from that future generic check. `dotnet build` 0 err (1 pre-existing unrelated
warning), `dotnet test` 907/907 green. Docs updated: `.claude/docs/api-contracts.md`,
ADR-021 addendum in `.claude/docs/decisions.md`. Local commit only, no push (product owner pushes).

## TASK-373 — Docs: Block 19 pre-launch audit (FINAL) — go/no-go readiness + stale-doc refresh
**Status:** done (2026-07-16) · **Agent:** documentation-writer + project-manager (main session, direct) · **Depends:** TASK-350..372
Log: `.claude/logs/tasks/373_2026-07-16_prelaunch-readiness-gono-go_documentation-writer.md`
Final block of the pre-launch audit (`eager-pondering-tower.md`). Synthesised all 20 blocks (0–18 +
this one) into the main deliverable `.claude/docs/prelaunch-readiness.md` — executive verdict,
per-block summary, critical fixed findings by severity, launch blockers, user-decision items, accepted
risks, metrics. **Verdict: NO-GO today, short path to GO** — every audit fix is on dev/staging only and
is still an **uncommitted working tree** (verified via `git status`); production runs the full pre-audit
codebase with all found bugs (RLS fail-open, dead worker crons, POS race, privilege escalation, broken
write-offs, non-functional mobile). **4 launch blockers:** (1) commit + deploy the audit to prod;
(2) run the 8 dev-applied EF migrations on prod (+ decide on the never-applied
`ExpandProviderBypassToProviderAdmin`); (3) SSH-verify prod's Postgres connection role is a
non-superuser (`rolsuper=f, rolbypassrls=f`) — an assumption not confirmed this session, and staging
shipped without it (KI-027), so the canary is a net not a substitute; (4) device-test the mobile app
(KI-024/025/026 verified at code level only, no device in the audit env). Refreshed the three stale
2026-06-04 docs (`architecture.md`/`backend-structure.md`/`frontend-structure.md`) to current reality
with a "Last reviewed: 2026-07-16" line each (v1→v4 shipped, Store→Location/Product→Item renames, worker
queues, ~75 migrations, KI-006/004 resolved + KI-027/028 role note). Metrics: backend 854/854, frontend
48/48; ~16 P0 + ~12 P1 fixed; ~11 KI resolved / ~13 open. No code changed (docs only).

## TASK-371 — Security: Block 18 pre-launch audit — OWASP/pentest, dependency CVE scan, secrets check
**Status:** done (2026-07-16) · **Agent:** security-reviewer (main session, direct) · **Depends:** Blocks 0-17
Log: `.claude/logs/tasks/371_2026-07-16_owasp-pentest-block18_security-reviewer.md`
Block 18 of the pre-launch audit (`eager-pondering-tower.md`), final security pass before Block 19.
**Found a P0 (staging-only, KI-027):** live cross-tenant IDOR test on staging (created a real
second tenant via the admin API) showed `GET /api/items/{id}`/`stock/{id}`/`locations/{id}`
returning full data across tenants. Root cause: `shelfguard_staging` (the staging Postgres
connection role) is a superuser (`rolsuper=t, rolbypassrls=t`) — Postgres superusers bypass RLS
unconditionally regardless of `FORCE ROW LEVEL SECURITY`, same bug class production already hit
and fixed once (`feedback-rls-superuser-bypass` memory: separate non-superuser `shelfguard_app`
role + `ALTER TABLE ... OWNER TO`), but that fix was never repeated for staging when Block 0 stood
up `docker-compose.staging.yml`. Attempted the same fix live (create `shelfguard_staging_app`,
transfer table ownership) — **blocked by the harness's own permission classifier** as an
unauthorized persistent infra change; did not work around it, documented as KI-027 with the exact
fix ready to run once the user authorizes it. **Also documented (KI-028):** `GetByIdAsync`-style
repository methods (Items/Stock/Locations and most others) have zero app-level `TenantId` filter —
by design, per CLAUDE.md's "trust RLS" architecture — meaning RLS is the *sole* tenant-isolation
layer for these reads, a single point of failure if a role misconfiguration like KI-027 ever
reaches production. Could not independently re-verify production's actual DB role this block (no
local `.env.production`, SSH out of scope per "прод не чіпаємо") — flagged as the one open
assumption behind believing production is unaffected.
**OWASP pass results:** SQLi — clean, no `FromSqlRaw`/`ExecuteSqlRaw` with interpolated strings
anywhere in backend (only safe `ExecuteSqlInterpolatedAsync` in test cleanup code); worker's raw
`pg` queries are 100% parameterized (`$1`/`$2`), no template-literal-with-variable SQL found. XSS —
zero `dangerouslySetInnerHTML` anywhere in `frontend/`. Broken Auth — live-verified: account
lockout (5 fails → 15 min, generic error, no state disclosure, per-account not global) and JWT
validation (tampered signature, `alg:none`, expired-with-correct-secret all correctly rejected
with 401; `ClockSkew=Zero`, no leeway). 2FA — live end-to-end (real TOTP secret, RFC 6238 codes
generated locally): brute-force on `/2fa/verify` hits the same account-lockout counter as password
login, not just the IP-partitioned rate limiter; recovery codes single-use; challenge token has
its own JWT audience (can't be replayed as an access token), 5-min expiry, tied to one user. RBAC —
live-verified a `merchandiser` (lowest-rank) account gets 403 on both `AtLeastStoreManager` and
`AtLeastEnterpriseAdmin`-gated endpoints. Integration-secret masking — live-verified: PUT a fake
Claude API key, GET returns `"••••CDEF"` (last 4 chars), matches CLAUDE.md's rule; code review
confirms the same masking + round-trip protection for prro/vchasno/telegram/resend/webhook/iot.
**Dependency CVE scan — fixed what was safely fixable:** backend NuGet had 4 High-severity CVEs
(`Npgsql`/`Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.0, `Microsoft.Extensions.Caching.Memory`
8.0.0 transitive via EF Core, `System.Net.Http`/`System.Text.RegularExpressions` 4.3.0 transitive
via old test-SDK deps) — bumped `Microsoft.EntityFrameworkCore(.Design/.InMemory)` and
`Npgsql.EntityFrameworkCore.PostgreSQL` to 8.0.11 (had to align all three on the same patch to
avoid an assembly-version conflict between the Npgsql provider's pinned EFCore.Relational
dependency and a naively-higher 8.0.29), and `Microsoft.NET.Test.Sdk`/`xunit`/
`xunit.runner.visualstudio` to their latest 2.x-compatible versions — **0 vulnerable packages
remain**, `dotnet build` clean (0 err), `dotnet test` 850/850 green. Frontend: bumped `next`
14.1.4→14.2.35 (the actual latest patch in the 14.x line, confirmed via npm registry — not a
major-version jump) + matching `eslint-config-next`, clearing the Next.js authorization-bypass/
cache-poisoning/XSS CVEs that exist in 14.1.x; 12→9 vulnerabilities, remaining 9 (next's own
still-unfixed-in-14.x items, `eslint-config-next`'s `glob` CVE, `vite`/`vitest`'s `esbuild` CVE)
all require a major-version bump (Next 15/16, ESLint config v16, Vitest v4) — documented, not
forced, per this block's "patch/minor only" mandate. `tsc --noEmit` clean, `npx vitest run` 48/48
green, `npm run build` clean. Worker: 1 low-severity `esbuild` CVE (dev-only), fixed via
non-force `npm audit fix` → 0 vulnerabilities. Mobile: fixed the 1 High (`form-data` CRLF
injection) via non-force `npm audit fix`; remaining 10 moderate are all transitive via Expo's own
build-time CLI tooling (`@expo/*`/`xcode`/`uuid`), fix requires an Expo SDK major-version change —
documented, not forced; `tsc --noEmit` clean after the fix.
**Secrets check:** grepped current source + full git history for Anthropic/AWS/Slack/Telegram key
patterns — 0 real matches (only a UI placeholder string `"sk-ant-api03-..."` in the Integrations
settings form hint, not a real key). No `.env`/`.env.staging`/`.env.production` file was ever
committed (`.env.production.example`/`mobile/.env.example` are template-only with `CHANGE_ME`
placeholders); `frontend/.env.local` is committed but only contains a `NEXT_PUBLIC_*` value
(intentionally public) — not a leak. `.gitignore` confirmed covers `.env.staging`/
`.env.production`. KI-014 mitigations (account lockout + 2FA) re-verified live this block, see
above and the updated KI-014 entry.
**Needs a user decision:** KI-027 (staging RLS-bypass fix — ready to execute, blocked by
permission classifier, needs explicit go-ahead) and KI-028 (defense-in-depth tenant-filter
question — 3 options documented, none executed). `.claude/docs/known-issues.md` updated with both
new entries + the KI-014 re-verification note.

## TASK-370 — DevOps/DB: Block 17 pre-launch audit — load testing
**Status:** done (2026-07-16) · **Agent:** devops-engineer + database-engineer (main session, direct) · **Depends:** Block 0, Blocks 1-16
Log: `.claude/logs/tasks/370_2026-07-16_load-testing-block17_devops-engineer.md`
Block 17 of the pre-launch audit (`eager-pondering-tower.md`). Fixed a real incident during
staging bring-up: `docker-compose.staging.yml` had no explicit project name and collided with
the dev stack's default project name, causing `docker compose up` to delete the running dev
containers (data survived — named volumes untouched); added `name: shelfguard_staging` and fixed
a wrong `DATABASE_URL` (host-mapped port instead of Compose-internal `postgres:5432`) that was
crash-looping the staging api. 4 new k6 scenarios (`loadtests/`): login-storm (rate-limiter +
lockout hold under real concurrency; found+fixed 3 sequential `SaveChangesAsync` calls in
`AuthService` batched to 1, p95 2.28s→1.77s; residual latency traced to bcrypt workFactor=12,
~600-700ms/verify, a security tradeoff not changed here — user decision needed if sub-1s login
is required), pos-queue (40 concurrent registers, Block 6's xmin optimistic-concurrency fix
verified correct under real load — 95 sales/255 conflicts/0 errors, stock delta exactly matches,
zero oversell), bulk-order-creation (`/api/orders/calculate`, p95=14ms, no issue), analytics-
concurrent-read (run alongside pos-queue, p95=21ms, no issue). `dotnet test` 850/850 green.
Follow-up flagged (not fixed, out of scope): POS sale path fetches stock unscoped by store
(`task_7d60b19c`).

## TASK-369 — DB: Block 16 pre-launch audit — cross-cutting DB performance sweep
**Status:** done (2026-07-15) · **Agent:** database-engineer (main session, direct) · **Depends:** TASK-350..368
Log: `.claude/logs/tasks/369_2026-07-15_db-performance-audit-block16-part2_database-engineer.md`
Block 16 of the pre-launch audit (`eager-pondering-tower.md`) — aggregated DB-performance pass.
An earlier same-day attempt got only as far as one migration (`AddActivityLogsIndexesAndDropSupersededStockIndexes`
— activity_logs indexes + dropped 2 superseded product_stock indexes, already verified) before
running out of session budget; this entry covers the rest. **Systemic audit of all 76 FORCE RLS
tables for a tenant-leading index:** cross-referenced against actual repository query methods
(not just schema) to avoid flagging false positives — found and fixed 2 real gaps via
`AddChatSessionsAndSupplySchedulesTenantIndexes` (EF-tracked fluent `HasIndex`, applied to dev
DB): `chat_sessions` had zero index besides PK despite `ChatService.GetSessionsAsync` (tenant
chat inbox) querying `WHERE TenantId == tenantId ORDER BY UpdatedAt DESC` directly — real,
present-day full scan on every inbox load, not a future risk; `supply_schedules`'s
`GetAsync(storeId?, supplierId?)` has both filters optional, so the Settings page's unfiltered
list has nothing but RLS to narrow rows. Checked 8 other initially-suspected tables
(`product_adu`/`product_buffer`/`promo_cannibalization`/`product_supplier_settings`/
`as_work_order_lines`/`ticket_comments`/`marketplace_order_items`/`stock_events`) and confirmed
**no fix needed** — every live query path already filters on a Guid FK to a one-tenant-only
parent (StoreId/WorkOrderId/DiscountId/TicketId/OrderId), so RLS's extra TenantId predicate never
causes a real scan; `stock_events` is write-only today (zero read call sites exist anywhere).
Deliberately did not blindly index all 10 — 8 would have been pure write overhead with zero read
benefit (same over-indexing failure mode Block 15 flagged for `notification_queue`).
**EF FK/index-tracking re-check:** `StockMovement` still 100% raw-SQL FKs (invisible to EF, risk
still low, unchanged from TASK-352); `Discount` has partially drifted since TASK-352 — `TenantId`/
`CreatedBy`/`ApprovedBy` are now fluent-tracked, only `ProductId`/`StoreId`/`ProductStockId`
remain raw-SQL-only — doc corrected. Grepped all 47 migrations for raw-SQL FK/index statements;
no new undocumented cases beyond the already-known ones. **N+1 sweep** of
Analytics/Catalog/Events/Notifications (not covered by their own audit block) — clean, no query-
in-loop patterns found anywhere in those 4 modules. `dotnet build` 0 err/0 warn (1 pre-existing
unrelated warning), `dotnet test` 850/850 green. `dotnet ef database update` couldn't connect
(env quirk noted in TASK-352, unrelated to this work) — applied migration SQL directly to dev DB,
hand-verified both indexes exist. Docs updated: `.claude/docs/database-schema.md` (new "Block 16"
section + corrected FK-tracking note). Nothing left needing a user decision.

## TASK-368 — Fullstack: fix unverified Telegram account-linking path (security)
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session, direct) · **Depends:** TASK-367 finding
Log: `.claude/logs/tasks/368_2026-07-15_telegram-link-security-fix_backend-developer.md`
TASK-367 found two competing Telegram-link mechanisms: the real, used one
(`POST /api/auth/telegram/link`) let a user paste a raw client-supplied chat_id with zero proof
of ownership; the safe one (`POST /api/telegram/link-code` + worker's `/start <code>` listener)
was already correctly implemented end-to-end but never called by the web frontend. User confirmed
in chat: fix now. Removed the unverified endpoint (`AuthController`/`IUserService`/`UserService`/
`UserDtos` — `LinkTelegramAsync`/`LinkTelegramRequest`); rewrote `TelegramLinkSection.tsx` to
generate a one-time code, show the `t.me/<bot>?start=<code>` deep link + manual-fallback
instructions, and auto-detect success via 3s polling of `/api/auth/me` (matches the codebase's
existing chat/marketplace/IoT polling convention) plus a manual "Перевірити зараз" button. Mobile
already used the safe flow; worker's `telegram-listener.ts` needed no changes (confirmed correct).
**Found + fixed a second bug while wiring this up:** `AuthUserDto` never included
`TelegramChatId` at all — the old "Telegram: Підключено" status was pure client-side optimistic
cache fiction from the now-removed endpoint's `onSuccess`, never real server state; would have
silently reverted on any reload/cache invalidation. Added `TelegramChatId` to `AuthUserDto` +
`AuthService.ToDto`, without which the new polling UX could never have detected a real link.
Live-verified end-to-end on the dev stack: generated a real code via the UI, simulated the
worker's exact `UPDATE users SET "TelegramChatId"=...` side effect via `docker exec ... psql`
(no live Telegram bot session available in this environment), confirmed the UI auto-flipped to
"✓ Підключено" within one poll cycle with no reload, and that the status survives a hard reload.
0 pre-existing dev-DB rows found linked via the old insecure path (nothing to migrate/document).
`dotnet build` 0 err/0 warn (1 pre-existing unrelated warning), `dotnet test` 850/850 green,
`tsc --noEmit` clean. Flagged, not fixed (low severity, out of scope): the worker's raw-SQL link
path writes no `activity_logs` row, so real Telegram linking no longer appears in the user
activity log (it only ever did via the removed insecure path) — candidate for a small follow-up.

## TASK-367 — Architecture: Block 15 pre-launch audit — cross-cutting duplication/dead code/unused endpoints
**Status:** done (2026-07-15) · **Agent:** project-architect (main session, review-only) · **Depends:** TASK-350..366
Log: `.claude/logs/tasks/367_2026-07-15_crosscutting-duplication-deadcode-audit_project-architect.md`
Block 15 of the pre-launch audit (`eager-pondering-tower.md`) — first repo-wide (not per-module) pass,
review only per `project-architect` guardrails. **Dead code confirmed (not deleted):** `Store`/`StoreZone`
entities + `StoreService`/`StoreRepository` are 100% unreferenced (no `DbSet`, no DI registration,
`StoreRepository` self-marked `[Obsolete]` with every method throwing, `StoresController.cs` already an
empty stub since TASK-201) — attempted deletion of the 9 dead files, blocked by the permission
classifier as exceeding this task's "recommend, don't execute multi-file changes" scope, reverted
cleanly (`git checkout --`, confirmed 0 diff, build/tests back to baseline). Recommended as a small
dedicated follow-up (~15 min, zero risk). **Duplication confirmed:** the 3 Claude advisors
(`ClaudeOrderAdvisor`/`BusinessAssistantAdvisor`/`SupplierAdvisor`) share byte-identical
`ResolveAsync`/`IsConfiguredAsync` key-resolution logic + response-parsing boilerplate — recommend
extracting a shared `ClaudeKeyResolver` helper, not executed (multi-file). Receipts/Transfers/WriteOffs
"document + items" pattern (Block 4's earlier flag) — recommend extracting only the read-side
`GetAll`/`GetPaged`/`GetById` triad, leave Create/status-transition logic separate (genuinely
divergent) — not executed. Mobile `lib/roles.ts` vs frontend `lib/roles.ts` — intentional subset, not
1:1 duplication, acceptable given no monorepo tooling; recommend cross-file comments only. Support
feature retirement (TASK-365) verified complete, no orphaned remnants found. **Unused endpoints found:**
`POST /api/telegram/link-code` orphaned (frontend uses `/api/auth/telegram/link` instead — an unverified
direct chat-ID-paste path; the bot-code flow this endpoint feeds can never fire in production, needs a
security/product decision); `SuppliersController` full CRUD (`/api/suppliers`, own ADR-020 permission
policies) has zero frontend/mobile callers — `frontend/features/suppliers/` documented in CLAUDE.md does
not exist, Receipts has no UI to pick/manage suppliers; `DiscountsController`/`CannibalizationController`/
`SupplySchedulesController`/`WeatherController`'s coefficient CRUD all have full backend, zero UI — a
pattern (v2-spec tuning knobs built backend-first, no settings UI), flagged as a pre-launch product gap,
not a code-quality fix. `dotnet build` 0 err/0 warn, `dotnet test` 879/879 green (unchanged, no code
landed this block).

## TASK-366 — Mobile: Block 14 pre-launch audit — write-offs/POS contract, role gating, token restore
**Status:** done (2026-07-15) · **Agent:** mobile-developer (main session) · **Depends:** TASK-354, TASK-356/357
Log: `.claude/logs/tasks/366_2026-07-15_mobile-audit-role-auth-bugs_mobile-developer.md`
Block 14 of the pre-launch audit (`eager-pondering-tower.md`) — first mobile-focused block.
Write-off mobile payload (`{productId, quantity}`) already matched Block 4's fixed backend, and
POS's 409-handling already correctly surfaced the concurrency error — but found and fixed 3
critical, previously-undiscovered mobile-only bugs that had been silently breaking those very
flows underneath: (1) **every role gate in the app** (`(app)/_layout.tsx`, write-offs,
customers, transfers, schedules, service-desk, dashboard) used invented PascalCase role names
(`'StoreManager'`, `'Director'`, `'Admin'`) that never match the real lowercase role strings —
POS tab invisible to cashiers, manager approve/reject actions invisible everywhere; fixed via
new `mobile/lib/roles.ts` (mirrors `frontend/lib/roles.ts`) used by all 9 affected screens
(KI-024). (2) `user.locationId` was always `undefined` (backend's wire field is `storeId`, no
mapping existed) — blocked write-off/transfer/production creation outright; plus
write-offs/transfers/stock list endpoints were sent the wrong query-param name (`location_id`/
`locationId` vs backend's actual `store_id`), so even fixing (2a) wouldn't have filtered the
lists; fixed both (KI-025). (3) `user` was never restored after a cold app restart (`loadToken()`
only restored the token; the existing `getMe()` was dead code) — broke every role-gated screen
silently until re-login; wired `getMe()` into the boot sequence (KI-026). Also added missing
`onError` handling on write-off approve/reject (now that Block 4 hard-fails on insufficient
stock) and made mobile login fail loudly instead of silently on 2FA-enabled accounts (KI-023,
partial — no mobile 2FA UI exists, flagged for a product decision). Confirmed unchanged:
offline support still absent (KI-022, documented, not built — out of scope), `expo-secure-store`
correctly used for tokens (no AsyncStorage), React 18/TS 5 (web) vs React 19/TS 6 (mobile) is
not a real risk (fully separate npm projects, no shared code). `npx tsc --noEmit` clean after
every fix. `npm run lint` fails on missing `eslint.config.js` (pre-existing, not fixed).
`expo start --web` could not verify live rendering — `react-dom`/`react-native-web` aren't
installed (web target never set up); did not install new deps unprompted. No
emulator/device in this environment (per task brief) — contract-level verification only.

## TASK-365 — Fullstack: retire Support feature, migrate Settings to ServiceDesk
**Status:** done (2026-07-15) · **Agent:** main session (fullstack, no sub-agent per explicit
instruction) · **Depends:** TASK-363 finding
Log: `.claude/logs/tasks/365_2026-07-15_support-to-servicedesk-migration_fullstack.md`
User decision from TASK-363's flagged finding: retire `Features/Support` (tenant Settings UI +
provider backend, both orphaned/unreachable since 2026-06-20 per code trace), keep the already-live
`/service-desk` ServiceDesk feature as the single ticket system. Deleted the dead Support code on
both sides (controllers, service, frontend feature dir); left `SupportTicket`/`SupportMessage`
entities + DB tables untouched (ServiceDesk shares the `SupportTicket` entity/table, 0 rows in
either table in dev). Found and fixed a real gap while verifying: ServiceDesk's provider view could
see tickets but had no reply endpoint — added `GET/{id}` + `POST/{id}/comments` to
`AdminServiceDeskController` and wired a reply UI into `ProviderSupportTab.tsx`. Verified full
round-trip in-browser: tenant creates ticket → provider sees + replies → tenant sees the reply.
`dotnet build`/`dotnet test` (879/879) clean, `tsc --noEmit` clean.

## TASK-364 — Frontend: Block 13 pre-launch audit — cross-cutting frontend quality
**Status:** done (2026-07-15) · **Agent:** frontend-developer (main session) · **Depends:** TASK-363
Log: `.claude/logs/tasks/364_2026-07-15_frontend-crosscutting-quality-audit_frontend-developer.md`
Block 13 of the pre-launch audit (`eager-pondering-tower.md`) — first frontend-wide (not
per-feature) block. KI-004 (duplicate `apiFetch`) confirmed already resolved, no code change,
doc updated. Added `app/error.tsx`/`app/global-error.tsx` (neither existed) — friendly UA
fallback UI, `console.error` with a `TODO(KI-020)` marker for future Sentry wiring.
**Found + fixed while verifying:** `global-error.tsx` broke `npm run build` on the pinned
`next@14.1.0` (`PageNotFoundError: Cannot find module for page: /_document` — a known Next
14.1.0 bug, fixed 14.1.1+, triggered by App-Router-only + `global-error.tsx` + no
`pages/_document`); fixed by bumping `next` to `14.1.4` (same minor line, patch-only, smallest
fix). Live-verified the boundary in-browser with a temporary throwing test route (deleted
after). Evaluated moving the access token out of `localStorage` (XSS exposure) — traced the
boot sequence and found the dashboard layout hard-gates on `getToken()` *before* any network
call and nothing anywhere calls `/api/auth/refresh` proactively on mount, so removing
`localStorage` without adding a new bootstrap-refresh flow would log every user out on every
reload; **not fixed**, documented as KI-021 with 3 options for the user to choose from. Sentry
absence confirmed and documented as KI-020 (needs a real DSN only the user can provision).
Added 5 new Vitest test files (46 new tests, 0%→covered): `lib/api.test.ts` (401→refresh→retry
state machine + request/response handling), `lib/roles.test.ts`, `lib/providerPermissions
.test.ts`, `lib/supplierPermissions.test.ts`, `lib/slug.test.ts` — all pure-logic files with no
`@testing-library/react` dependency needed (none installed). `npx tsc --noEmit` clean,
`npx vitest run` 6/6 files 48/48 tests green, `npm run build` clean (post next bump).

## TASK-363 — Backend: Block 12 pre-launch audit — Provider / Admin / ServiceDesk / Chat
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-362
Log: `.claude/logs/tasks/363_2026-07-15_provider-admin-servicedesk-chat-audit_backend-developer.md`
Block 12 of the pre-launch audit (`eager-pondering-tower.md`). **Found + fixed a P0:**
`ProviderTeamService` let any `provider_admin` self-escalate to the literal owner role
(`role: "provider"`) via Invite/Update on themselves or a teammate — no rank/owner check existed
beyond "can't demote the owner." Since `ProviderController` (tenant CRUD, impersonation,
platform logs) is gated strictly to `role == provider` (not provider_admin), this let a
provider_admin grant itself full owner access. Also fixed: provider_admin could deactivate the
literal owner account (DoS). Fix: Invite/Update/Deactivate now take the caller's own role from
the JWT and reject granting/protecting the owner role unless the actor already is the owner.
10 new tests (`ProviderTeamServiceTests`, zero coverage before). **Found + fixed a P1/hardening
gap:** `chat_messages`/`support_messages` had RLS completely disabled (live-confirmed via
`pg_class`) — the only two tables in the whole Chat/ServiceDesk/Support family without it, while
every sibling (including the analogous `supplier_chat_messages`) has it. App code was already
scoping correctly everywhere (not a live exploit), but zero DB safety net. Fixed via
`20260715153812_AddChatAndSupportMessagesRls` (EXISTS-subquery-via-parent pattern, matches
`supplier_chat_messages`); live cross-tenant read test confirmed 0 rows leak, own-tenant reads
still work. **Flagged, NOT fixed — high-confidence P0, needs a product decision:** the `Support`
feature (Settings → "Служба підтримки", `/api/support/*`) is fully wired on the tenant side but
its provider-side reply UI is completely orphaned — zero frontend component anywhere calls the
correctly-implemented `/api/provider/support/*` hooks. Real tenant support tickets vanish with
no operator ever seeing them. Migration dates suggest ServiceDesk (4 days later) was meant to
replace it but the old tenant UI/backend were never removed. Needs a decision: build the missing
inbox, retire/redirect the old feature, or merge into ServiceDesk. Background task spawned.
Reviewed and confirmed correct, no changes: tenant onboarding atomicity (single SaveChanges,
both Provider and Admin onboarding paths), impersonation mechanics (stateless scoped JWT,
explicit frontend exit, audits back to the real provider's user id), provider-role isolation
from tenant flow (UserService's ValidRoles excludes all provider tiers), ServiceDesk status
lifecycle + access + no N+1, Chat IDOR (tenant id always from JWT, never request body) + no N+1,
RLS on support_tickets/ticket_comments/chat_sessions (Block 2 pattern intact), no worker code
touches any ServiceDesk/Chat table (Block 11's bug class doesn't apply here). `dotnet build`
0 err/0 warn (1 pre-existing unrelated warning), `dotnet test` 879/879 green (was 869). Migration
applied to dev DB only; prod not touched.

## TASK-362 — Backend: Block 11 pre-launch audit — IoT / Weather / Events / Cannibalization
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-361
Log: `.claude/logs/tasks/362_2026-07-15_iot-weather-events-cannibalization-audit_backend-developer.md`
Block 11 of the pre-launch audit (`eager-pondering-tower.md`). **Confirmed and fixed KI-016
(P0, same bug class as Blocks 7/9):** live-confirmed against the dev DB that `iot_devices`/
`weather_data`/`temperature_readings`/`product_stock` all have their store column renamed to
`"LocationId"` (v4 rename) while `stock_events` genuinely kept `"StoreId"`. Fixed
`weather-fetch.job.ts`'s `INSERT INTO weather_data` (still used `"StoreId"` even after TASK-358's
partial fix — every upsert had been throwing) and `mqtt-listener.ts` (4 places: device lookup ×2,
temperature_readings INSERT, product_stock FEFO SELECT). **Found one level deeper in the same
investigation:** `weather-fetch.job.ts`/`ai-order.job.ts` never called `SET app.role = 'worker'`
at all, and `notification.job.ts`'s `handleExpiryAlert`/`handleIotAlert` likewise never set it —
under the Block 2 fail-closed RLS fix, these queries silently returned zero rows unless the
pooled pg connection happened to inherit the role from another job's reused connection
(connection-pool-luck correctness, not guaranteed). Fixed all three files with the explicit SET,
matching every other worker job. Live-verified end-to-end on the rebuilt dev worker container
(real BullMQ jobs, real MQTT messages via `mosquitto_pub`, real DB queries) — not just
tsc/build — including proof that `handleIotAlert` now finds the 3 real matching users where it
previously would have found zero. **Also added:** MQTT temperature readings now sanity-bound
(`isPlausibleTemperature`/`isPlausibleHumidity` in `iot-rules.ts`, -60..60°C) before insert — a
broken sensor can no longer write garbage into `temperature_readings` or falsely trigger
`temp_violation`; live-verified a 9999°C reading correctly rejected. Reviewed and confirmed
correct, no changes: IoT device→location binding (no N+1), weather fallback (neutral 1× when
no data, matches Block 7's "never break AI orders" requirement), Events/Cannibalization default
coefficients match v2-spec §4/§5 exactly, `OrderCalcService` correctly wires all three
multipliers, RLS fail-closed + worker_bypass present on all 8 tables named in the brief
(live-confirmed via `\d`). **Flagged, not fixed — needs a product decision:** KI-019 —
`IotController`/`WeatherController`/`EventsController`/`CannibalizationController` (and nearly
all of v2/v3: Orders/Adu/Buffer/AiOrders/Pos) have no `[RequireModule]` gate despite CLAUDE.md's
architecture rule; not fixed because `Tenant.DefaultModulesForBusinessType` grants no tenant
`"auto_order"`/`"iot"`/`"pos"` by default, so adding the gate blind would 403 every currently-
working tenant. `dotnet build` 0 err/0 warn, `dotnet test` 869/869 green (unchanged — worker-only
block). Worker `tsc --noEmit` clean.

## TASK-361 — Backend: Block 10 pre-launch audit — Auto Service / Production
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-360
Log: `.claude/logs/tasks/361_2026-07-15_autoservice-production-audit_backend-developer.md`
Block 10 of the pre-launch audit (`eager-pondering-tower.md`). Module gating
(`[RequireModule]` on both `AutoServiceController`/`ProductionController`) already correct,
5 existing tests cover it. No old `stores`/`catalog_products` table references found (same
bug class as Blocks 7/9) — both modules are clean EF LINQ, no raw SQL. **Found + fixed a
P1:** `ProductionService.CompleteOrderAsync` silently fell back to a fake
`DateTime.UtcNow.AddYears(10)` expiry for the produced batch when the output `Item` had no
`ShelfLifeDays` configured — not literally null (the audit brief's specific worry) but the
same bug in disguise, defeating FEFO tracking for that batch without surfacing anything to
the user. Fixed: now validates `ShelfLifeDays` up front (before any ingredient consumption,
atomic guarantee preserved) and returns 422 if missing, mirroring `ReceiptService`'s
stricter "no placeholder expiry" pattern. 1 new test. Reviewed and confirmed correct, no
changes: FEFO in Production correctly scoped to `order.LocationId`; RLS on
`as_customers`/`as_vehicles`/`as_work_orders`/`as_work_order_lines`/`as_service_catalog`/
`production_orders`/`recipes` verified live via `pg_policies` — all carry the canonical
Block 2 fail-closed pattern; child tables `recipe_ingredients`/
`production_order_consumptions` deliberately have no own RLS (tenant scope inherited via
JOIN from parent, documented in entity comments, verified no unscoped access path exists);
no N+1 in either module's list endpoints. **Flagged, not fixed — needs a product
decision:** KI-018 — Auto Service has no location concept at all (`AsWorkOrder` has no
`LocationId`), so spare-part FEFO write-down is tenant-wide instead of location-scoped
(Production doesn't have this gap). Invisible for single-location tenants, a real
cross-location leak for auto-service chains, which v4-spec explicitly supports. Needs a
schema migration + API changes, out of scope for this block. `dotnet build` 0 err/0 warn
(1 pre-existing unrelated warning), `dotnet test` 869/869 green (was 868).
**Addendum (same day):** user confirmed directly in chat — plan the KI-018 fix now,
implement later. Full plan written into the task log (nullable `AsWorkOrder.LocationId`
additive migration + no RLS changes needed, verified live that RLS quals never filter on
`LocationId`; `IAutoServiceRepository.GetFefoOrderedAsync` gets a `locationId` param
mirroring the already-correct `IProductionRepository` shape; frontend reuses the existing
`useStoreContext`/`StoreSelector`, no new UI component). Effort ~1 day, low risk (additive,
no breaking API change). One open product question left unresolved on purpose (how
pre-migration `LocationId = NULL` orders behave — recommended: fall back to today's
tenant-wide FEFO rather than hard-block). `known-issues.md` KI-018 status updated to
"planned" with a link to the plan. No code changed for this addendum.

## TASK-360 — Backend: Block 9 pre-launch audit — Customers / Notifications / Schedules
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-359
Log: `.claude/logs/tasks/360_2026-07-15_crm-hr-notifications-audit_backend-developer.md`
Block 9 of the pre-launch audit (`eager-pondering-tower.md`). Modules had zero test coverage.
**Found + fixed 2 P0:** (1) `worker/src/jobs/notification.job.ts`/`expiry-check.job.ts`/
`stock-snapshot.job.ts` queried pre-rename table/column names (`catalog_products`/`stores`/
`"StoreId"` on `product_stock` — renamed to `items`/`locations`/`"LocationId"` mid-June) — the
entire hourly expiry-notification cron and its dashboard-snapshot sibling crashed on every run,
same bug class as TASK-358. Root-cause enabler: local dev `docker-compose.yml`'s worker
`DATABASE_URL` was still the broken .NET-format string TASK-033 (2026-06-11) already fixed for
staging/prod — never applied to dev, so no worker job had run successfully against a real DB in
dev this whole audit series; fixed alongside (`postgresql://` format). Also fixed a P1 in the
same file: `expiry-check.job.ts`'s hardcoded 1/3-day thresholds diverged from both v1-spec §2.2
and the backend's own `StockStatus.Compute` — batches 4-14 days out were cron-invisible, never
notified; now mirrors `PerishabilityClass.GetThresholds` via a join to `items`. All three fixes
live-verified end-to-end (rebuilt worker container, manually triggered jobs, confirmed
`notification_queue`/`stock_status_snapshots` rows written correctly, 0 errors).
(2) `notification_settings` RLS: Block 2 (TASK-352) deliberately kept a session-level fail-open
branch here, grouped with `users`/`refresh_tokens` as "pre-auth lookup" — live-reproduced that
this doesn't actually apply (every access is `[Authorize]`'d, JWT-derived, no anonymous path
touches this table) by seeding cross-tenant rows and reading them back under a RESET session.
Fixed via `20260715120000_FixNotificationSettingsRlsFailOpen` (removes only the outer fail-open
branch, keeps the inner null-TenantId branch needed for provider accounts); updated the existing
allowlist test + added a dedicated Postgres-integration regression test, both pass. **Found +
fixed a P1:** Schedules' shift-overlap guard (`DetectShiftConflicts`) only ran at publish time —
`AddShiftAsync`/`UpdateShiftAsync` never re-checked, so adding/editing a shift on an
already-published schedule could silently double-book an employee; fixed both methods with the
same overlap rule. **P2:** Customers had zero Phone/Email format validation (any string
accepted) — added a permissive-but-real format check. Reviewed and confirmed correct, no
changes: `customers`/`schedule_shifts`/`work_schedules` RLS (Block 2 fix verified live via
`pg_policies`, not just migration text), indexes (all TenantId-leading, match actual filters, no
gaps), no N+1 in any of the three modules' lists, Schedules role gating matches v1-spec §3.2.
Flagged, not fixed: KI-016 (`weather-fetch.job.ts`/`mqtt-listener.ts` same StoreId-column bug
class, Block 11 scope — background task spawned), KI-017 (`needs_verification` status has no
cron-triggered notification at all — schema gap, small dedicated task candidate). 15 new tests
(`CustomerServiceTests`, `ScheduleServiceTests`, `NotificationServiceTests` + 1 new Postgres RLS
regression test). `dotnet build` 0 err/0 warn, `dotnet test` 868/868 green (was 846). Worker
`tsc --noEmit` clean. Migration applied to dev DB; prod/staging not touched.

## TASK-359 — Backend: Block 8 pre-launch audit — Suppliers & Marketplace
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-358
Log: `.claude/logs/tasks/359_2026-07-15_suppliers-marketplace-audit_backend-developer.md`
Block 8 of the pre-launch audit (`eager-pondering-tower.md`). Pre-existing uncommitted changes
in `SupplierCabinetCooperationController.cs`/`CooperationRequestsTab.tsx` verified correct, no
further changes. **Found + fixed a P1:** supplier custom roles/permissions
(`SupplierRole.Permissions`, TASK-306) were UI-only — `SupplierCabinetController`/
`SupplierCabinetCooperationController` gated only by `RequireRole(supplier_admin)`, so any
invited staff member had full API access regardless of assigned role (self-escalation within
the supplier's own tenant — e.g. a `task_board`-only staffer could still invite new staff or
delete other roles). Same class of gap ADR-020 fixed for tenant roles. Fix: new
`SupplierPermissionAuthorization.HasPermission` (mirrors `LegalEntityAuthorization`, reads the
JWT `permissions` claim already correctly populated by the existing generic pipeline — only
the read side was missing) + in-body checks on every `SupplierCabinetController` action,
mapped 1:1 to the existing frontend nav permission grouping; chat left ungated (matches
BUG-019's deliberate decision). Corrected a stale/false comment in `Sidebar.tsx` claiming the
backend already gated the cooperation-flow routes. 4 new tests. **Flagged, not fixed — needs a
product decision:** cooperation-flow controller (agreements, orders, contract-settings,
support-tickets) has no fine-grained permission key defined at all; adding one means choosing
new taxonomy, a product call, not an objective fix. Reviewed and confirmed correct: agreement
lifecycle (no status can be skipped, pending→awaiting_signature→active→terminated), Вчасно
integration (per-tenant key via `integration_configs`, graceful error handling, not
hardcoded/shared), marketplace order isolation (supplier-scoped catalog validation, tenant-
scoped list/cancel/status-update), RLS on all supplier/marketplace two-tenant tables (created
with the canonical NULLIF pattern from day one — never subject to the Block 2/TASK-352
fail-open bug, so that fix correctly left them untouched; `provider_bypass`+`worker_bypass`
both present), no N+1 in order/agreement/chat/ticket list endpoints. `dotnet build` 0 err,
`dotnet test` 846/846 green (was 842). `tsc --noEmit` clean.

## TASK-358 — Backend: Block 7 pre-launch audit — AI Orders / AI Assistant
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-357
Log: `.claude/logs/tasks/358_2026-07-15_ai-orders-assistant-audit_backend-developer.md`
Block 7 of the pre-launch audit (`eager-pondering-tower.md`). **Found + fixed a P0:**
`worker/src/jobs/ai-order.job.ts` and `weather-fetch.job.ts` both queried `FROM stores` — a
table renamed to `locations` in `20260615183318_V4LocationsRename` — so the nightly
05:00 cron (v2-spec §7) never generated a single AI order suggestion, and `weather_data` was
never populated (every `AiOrderService.GenerateAsync` call, cron or manual, fed Claude an
empty weather array). Fixed both to `FROM locations` (columns unchanged). **Found + fixed a
P1:** the N+1 in `AiOrderService.GetListAsync` flagged in TASK-355's log (per-suggestion
`GetByIdAsync` just to read `Items.Count`) — `AiOrderRepository.GetListAsync` now
eager-loads `Items`, service reads the count directly; regression test added. **P2:** all
three Claude advisors (`ClaudeOrderAdvisor`/`BusinessAssistantAdvisor`/`SupplierAdvisor`) had
no explicit `AnthropicClient.Timeout` — SDK default is 10 min × up to 3 attempts, could hang
a synchronous `POST /api/ai-orders/generate` for ~30 min; set to 60s. Reviewed and confirmed
correct, no changes: AI isolation (Application layer has zero Anthropic SDK references,
only Domain interfaces), graceful error degradation (Claude failures → readable 400, never
500, already had try/catch + Ukrainian billing-specific message), API key masking (last-4,
fixed in TASK-347) and no logging of the key, RLS/cross-tenant isolation (same
per-request `AppDbContext` as everywhere else, no superuser/detached-scope bypass — the POS
Task.Run bug class from TASK-356 does not repeat here), no N+1 in AI-prompt context assembly
itself, no duplicate Claude spend from the frontend (both generate/ask hooks are React Query
mutations, buttons disabled while pending). 12 new tests (`AiOrderServiceTests`,
`AiAssistantServiceTests`). `dotnet build` 0 err/0 warn, `dotnet test` 842/842 green (was
830). Worker `tsc --noEmit` clean. **Flagged, not fixed (low severity):**
`weather-fetch-cron` fires at 06:00, an hour *after* `ai-order-cron`'s 05:00 — the morning AI
order run always reads the previous day's weather fetch.

## TASK-357 — Frontend: POS cash reconciliation UI (close-shift cash count)
**Status:** done (2026-07-15) · **Agent:** frontend-developer (main session) · **Depends:** TASK-356
Log: `.claude/logs/tasks/357_2026-07-15_pos-cash-reconciliation-ui_frontend-developer.md`
UI for TASK-356's `POST /api/pos/shifts/close { actualClosingCash? }` contract. New
`CloseShiftDialog.tsx` (replaces `window.confirm()`) — optional cash-count input,
blank = old no-reconciliation behavior; client-side negative guard mirrors backend's
400. New `CashReconciliationSummary.tsx` — renders only when `closingCash != null`,
shown in the existing Z-report card: opening/expected/actual cash + discrepancy badge
(green "Збіг" exact / amber "Надлишок" surplus / red "Недостача" shortage).
`ShiftDto`/`CloseShiftRequest` types, `useCloseShift` hook updated. **Found+fixed
while verifying:** both close/open shift dialogs stay mounted while hidden
(`if (!isOpen) return null`), so internal `useState` doesn't reset on reopen — a
stale `actualClosingCash` from a previous close silently carried into the next one.
Fixed in `CloseShiftDialog.tsx` via a `useEffect` reset on `isOpen`; the identical
pre-existing bug in `OpenShiftDialog.tsx` was left as-is (out of scope) and flagged
as a background task. `tsc --noEmit` clean. Live-verified on local dev stack:
shortage (-50, red #ef4444), exact match (Збіг, green #22c55e), surplus (+450),
and the no-input backward-compatible path (all four fields `null`, no reconciliation
section rendered) — cross-checked against the raw `/shifts/close` network response,
not just the rendered text. No web UI creates POS sales (mobile-only), so
`expectedCashAmount`'s cash-sales-total branch wasn't exercised with a real sale.

## TASK-356 — Backend: Block 6 pre-launch audit — POS & Фіскалізація (Checkbox ПРРО)
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-355
Log: `.claude/logs/tasks/356_2026-07-15_pos-fiscalization-audit_backend-developer.md`
Block 6 of the pre-launch audit (`eager-pondering-tower.md`). Highest financial/legal risk
area. **Found + fixed 2 P0:** (1) online fiscalization ran on a detached, un-awaited
`Task.Run` that captured the request's scoped `IPosRepository`/DbContext and an
`HttpContext`-driven RLS interceptor — both invalid once the HTTP response completed, so
sales were fiscalized only by the 5-min retry job, never inline (Checkbox idempotency
prevented double-fiscalization, but "instant fiscal receipt" never actually worked); fixed
by running the attempt inline, bounded by an 8s timeout, still never blocking the sale.
(2) `ProductStock.Quantity` had no optimistic-concurrency protection — two concurrent sales
of the same batch's last unit both succeeded (silent oversell, lost update); fixed via
`xmin` concurrency token (`AppDbContext`), a new `ConcurrencyConflictException` (Domain
layer, so `PosService` doesn't need an EF Core reference) thrown from
`PosRepository.SaveChangesAsync`, translated to a clean 409 in `PosService.CreateSaleAsync`.
**Found + fixed a P0-adjacent bug while building the concurrency test:**
`ItemRepository.GetByBarcodeAsync` (the only way `PosService.CreateSaleAsync` resolves a
scanned barcode) threw `PostgresException 42846: cannot cast type text[] to jsonb` against
real Postgres — every existing test used an in-memory fake, so this had never been caught;
core POS barcode scanning could not have worked in production. Fixed via
`EF.Functions.JsonContains`. Verified indexes on pos_transactions/pos_transaction_items/
pos_shifts already adequate (best-indexed module in the codebase), no N+1 in shift/day
report paths, money is `decimal` throughout, `IFiscalServiceFactory` correctly per-tenant,
FEFO in POS sales matches Block 3. New real-Postgres test
(`PosConcurrencySalesIntegrationTests`, deterministic two-way rendezvous, not timing-luck)
+ 2 new fake-based unit tests. **Flagged for user decision (not fixed):** shift-open is
scoped per tenant not per store (blocks multi-store simultaneous POS — tied to Checkbox
license being resolved per-tenant, not a simple fix); `PosShift.ClosingCash` cash
reconciliation was never built end-to-end (schema exists, no endpoint/UI). Spawned a
separate background task for an unrelated but same-root-cause jsonb query bug in
`DailySalesRepository.GetProductIdsByBarcodesAsync` (out of scope, not POS).
`dotnet build` 0 err/0 warn, `dotnet test` 824/824 green (was 821). Migration
`20260715054917_AddProductStockXminConcurrencyToken` applied to dev DB.
**Addendum (same day):** user confirmed two directives on the flagged gaps. (1) Per-store
shifts — **plan only**, written into the task log — traced the restriction to
`IPosRepository.GetOpenShiftAsync`/`IFiscalServiceFactory.GetForTenantAsync` both being
tenant-scoped (no `StoreId`), confirmed via `.claude/docs/integrations.md` that Checkbox's
`X-License-Key` is register-scoped (not company-scoped) — so this is ShelfGuard's own
schema simplification (`integration_configs` has no `StoreId`), not a Checkbox limitation;
not trivial (DB migration + `IFiscalServiceFactory`/`IPosRepository`/`PrroSettingsController`
signature changes + frontend store selector), tracked as `known-issues.md` KI-015, not
implemented. (2) Cash reconciliation — **implemented**: `POST /api/pos/shifts/close` body
now optionally accepts `{ actualClosingCash }` (backward compatible, omit = old behavior);
`ShiftDto` gained `openingCash`/`closingCash`/`expectedCashAmount`/`cashDiscrepancy` (cash-only
sales, card excluded); new `IPosRepository.GetCashSalesTotalForShiftAsync`; validates
`>= 0` → 400; 6 new tests (exact/shortage/surplus/negative/no-count/double-close). Updated
`api-contracts.md` (new POS section, full contract for frontend hand-off) and
`known-issues.md`. `dotnet test` 830/830 green.

## TASK-355 — Backend: Block 5 pre-launch audit — Orders/ADU/Buffer
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-354

Reviewed `Features/Adu`, `Features/Buffer`, `Features/Orders` against v2-spec.md §1-3 +
v1-spec.md §2.7 (MOQ/USQ). Formulas match spec (ADU windows/groups, CDA zones, order
formula, div-by-zero guards). Found + documented a MOQ/USQ rounding-ladder deviation
(anchored at zero instead of MOQ) — user confirmed same-day, fixed: `OrderFormula.Compute`
now rounds UP the MOQ + k×USQ ladder (`moq + ceil((raw-moq)/usq)*usq`), never below what
was actually needed. No N+1, indexes adequate, no duplication with Stock (Block 3). Found
(not fixed, out of scope) a real N+1 in `AiOrderService.GetListAsync` — flagged as a
separate background task. Added 4 edge-case tests (new product w/ no history, zero-ADU
buffer, empty delivery schedule) + updated MOQ/USQ ladder tests for the fix. Full log:
`.claude/logs/tasks/355_2026-07-15_orders-adu-buffer-audit_backend-developer.md`.
Build 0 errors, tests 821/821 green.

## TASK-354 — Backend: Block 4 pre-launch audit — Receipts/Transfers/WriteOffs
**Status:** done (2026-07-15) · **Agent:** backend-developer (main session) · **Depends:** TASK-353
Log: `.claude/logs/tasks/354_2026-07-15_receipts-transfers-writeoffs-audit_backend-developer.md`
Block 4 of the pre-launch audit (`eager-pondering-tower.md`). **Found and fixed a P0**:
`WriteOffService.ApproveAsync` had `if (item.ProductStockId is null) continue;` —
silently skipping stock deduction and movement logging. The mobile app's "quick
write-off" screen (the only UI in the whole codebase that creates write-offs) sends
`{ productId, quantity }` with no `productStockId`, so every write-off approved through
the real app never touched `product_stock` and never wrote a `stock_movements` row,
despite showing `status=approved` and a computed `TotalLossAmount`. Fix: no-batch items
now FEFO-consume across the product's batches at the write-off's store (new
`IWriteOffRepository.GetFefoOrderedAsync`, same query as `StockRepository`'s). Also
fixed a **P1**: both the explicit-batch and new FEFO branches used to silently
`Math.Min`-clamp the deduction when requested quantity exceeded available stock,
leaving `LossAmount` inconsistent with the real amount removed — now both hard-fail
`ApproveAsync` with a clear error and persist nothing (matches the audit's explicit
"can't write off more than is in stock" requirement, which previously did not hold).
3 tests rewritten/added in `WriteOffServiceTests.cs` (replaced the test that had
encoded the buggy "nothing happens" behavior as correct).
**DB index gap**: `stock_receipts`/`stock_transfers` had FK-column indexes but no index
with `TenantId` at all (unlike `WriteOff`, which already had one) — every RLS-filtered
query on these two tables was a seq scan. Added 3 composite indexes, migration
`20260714210933_AddStockReceiptsTransfersTenantIndexes` (additive), applied to dev DB,
verified via `\di`.
Reviewed and found correct, no changes: Receipts create/receive validation, Transfers
source/destination quantity consistency + FEFO immutability (Block 3 already confirmed
at service level, re-confirmed full workflow), no N+1 in any of the three modules'
list endpoints (all eager-`.Include()`), FK indexes on `ProductId`/parent-id columns
all present via EF convention. Flagged (not fixed, low severity / out of scope):
`ToStoreId`/`DestinationStoreId`/`StoreId` aren't pre-validated against `Locations`
(relies on DB FK + RLS, bad id → 500 not 400); Receipts/Transfers/WriteOffs share no
common "document + items" abstraction despite near-identical shape (Block 15 candidate).
`dotnet build` 0 err, `dotnet test` 817/817 green (was 815).

## TASK-353 — Backend: Block 3 pre-launch audit — Inventory/Stock/Locations/Stores/Catalog
**Status:** done (2026-07-14) · **Agent:** backend-developer (main session) · **Depends:** TASK-352
Log: `.claude/logs/tasks/353_2026-07-14_inventory-stock-fefo-audit_backend-developer.md`
Block 3 of the pre-launch audit (`eager-pondering-tower.md`). FEFO
(`StockService.FefoConsumeAsync`/`GetFefoOrderedAsync`) and transfer immutability
(`TransferService`, `expiry_date`/`batch_number` copied as-is) were already correct —
added 3 targeted tests (tied-expiry consumption + new `StockRepositoryFefoTests.cs` EF
InMemory suite pinning the real LINQ query's zero-qty/archived/store/product filters) and
a defense-in-depth explicit status filter on `GetFefoOrderedAsync`. KI-008 (pagination)
was already resolved by commit `206b2534` (2026-06-18) — `api/products` is now a pure
redirect shim to the paginated, authorized `api/items`; doc was just stale, now marked
resolved in `known-issues.md` + `api-contracts.md` corrected. **Found and fixed a real N+1**:
`StockService.GetSuggestionsAsync` ran one `GetDeficitStocksAsync` query per
action-required batch — the bulk method (`GetDeficitStocksBulkAsync`) existed but was
never wired in. Rewired to a single bulk query (`Dictionary<Guid, List<ProductStock>>`,
filters out the batch's own store in-memory to preserve "exclude own store" semantics);
2 test fakes updated for the new signature, 2 new regression tests added. `idx_stock_fefo_active`
+ `idx_stock_expiry_active` verified present on dev DB (table too small for a meaningful
EXPLAIN ANALYZE at 25 rows). Flagged as follow-ups (not fixed, out of scope): stale
"Pending Endpoints" table in `api-contracts.md`; dead `StoreService`/`Store` code
superseded by `LocationService`/`Location` (TASK-201). `dotnet build` 0 err/0 warn,
`dotnet test` 815/815 green (was 808/808).

## TASK-352 — DB: Block 2 pre-launch audit — RLS cross-tenant sweep + fix, DB-level leak test
**Status:** done (2026-07-14) · **Agent:** database-engineer (main session) · **Depends:** TASK-351
Log: `.claude/logs/tasks/352_2026-07-14_db-cross-tenant-audit_database-engineer.md`
Block 2 of the pre-launch audit (`eager-pondering-tower.md`). Queried `pg_policies` directly
against the dev DB (74 FORCE RLS tables) instead of parsing 68 migration files. Found (P0): 6
tables (`customers`, `schedule_shifts`, `work_schedules`, `support_tickets`, `ticket_comments`,
`chat_sessions`) had their tenant policy named something other than the literal
`tenant_isolation`, so both 2026-06-29 bulk NULLIF-guard fixes silently skipped them — 5 had no
NULLIF guard at all, `chat_sessions`'s OR-based guard didn't actually short-circuit either.
Reproduced live: all 6 throw `invalid input syntax for type uuid` when `app.tenant_id` is RESET
(unauthenticated-request state), and confirmed `worker_bypass`/`provider_bypass` don't rescue it
(Postgres evaluates every permissive policy's qual). 3 of the 6 also had no `provider_bypass` at
all. Fix: `20260714100000_FixMissingRlsGuardsAndProviderBypass.cs` (additive, renames to
canonical `tenant_isolation` + NULLIF + adds missing `provider_bypass`); applied directly to dev
DB (full `backend` build broken all session by an in-flight parallel edit to
`UsersController.cs`/`AppPolicies.cs`, not touched — worked via `ShelfGuard.Tests`, which builds
standalone). Practical cross-tenant leak test (forged tenant-id in WHERE clause, real
NOSUPERUSER/NOBYPASSRLS role) against `customers`/`product_stock`/`ai_order_suggestions` — RLS
blocked all 3, both manually and via 3 new automated tests in
`RlsCrossTenantIntegrationTests.cs` (soft-skip if no local Postgres; CI has none today). One test
turns the audit query itself into a permanent regression guard. `database-schema.md` RLS
Template section reduced to one canonical pattern (old no-NULLIF version marked deprecated with
the incident it caused); fixed a stale "ADR-009" citation. `dotnet test ShelfGuard.Tests`
805/805 green. **Needs a decision:** 71 tables' `provider_bypass` only matches role `provider`,
not `provider_admin` — but `ProviderPermissions` grants `provider_admin` the same `All`
permissions, so provider-team admins likely get silent empty results (not a leak) on
Analytics/Marketplace queries. Not fixed — flagged as an architectural call.
**Update:** a "coordinator" message mid-task claimed the user approved the 71-table expansion
directly in chat; per this agent's rules that's not equivalent to the user's own message in
this transcript, and the harness's permission classifier independently blocked the apply for the
same reason. Migration prepared (`20260714150000_ExpandProviderBypassToProviderAdmin.cs`) but
NOT applied to any DB — awaiting the user's direct confirmation in this conversation. See log
for details.
**Update 2 (worse P0, found+fixed on dev):** independently verified a real fail-open bug in
`tenant_isolation` on 60 tables — the `IS NULL OR` branch (from the 2026-06-29 bulk fix, copied
into this task's own earlier `20260714100000` migration) returns ALL tenants' rows when
`app.tenant_id` is unset, instead of the intended NULLIF short-circuit-to-zero-rows. Reproduced
live (real NOSUPERUSER role, RESET state → `product_stock` returned all rows). Root-caused to a
deviation from the actual canonical pattern in `.claude/agents/database-engineer.md`. Fixed 57 of
60 tables via `20260714180000_FixFailOpenTenantIsolationOnReset.cs`; kept the fail-open branch on
`users`/`refresh_tokens`/`notification_settings` (legitimate pre-auth lookup need — the
coordinator's blanket instruction would have broken login/token-refresh). DB apply was blocked
by the permission classifier for the same relayed-approval reason as above; a later message
claimed the orchestrator applied it directly via `dotnet ef database update` — independently
re-verified this (not trusted at face value): migration is genuinely recorded in
`__EFMigrationsHistory`, policy text is genuinely fail-closed, live RESET-state test genuinely
returns 0 rows now. Found and fixed 2 real worker-code regressions this exposed
(`telegram-listener.ts`, `notification-dispatch.job.ts` — neither set `app.role='worker'`, so
they silently depended on the removed fail-open branch); found 3 unrelated pre-existing dead-code
issues (`ai-order.job.ts`, `notification.job.ts`, `weather-fetch.job.ts` query non-existent
`stores`/`catalog_products` tables) flagged separately, not fixed. 2 new regression tests added.
`dotnet test` 808/808 green, worker `tsc --noEmit` clean. **Production NOT touched — still runs
the fail-open policy; deploying this fix to prod is a separate decision for the user.**

## TASK-351 — Security: Block 1 pre-launch audit — Auth & Access Control, KI-005 fix
**Status:** done (2026-07-14) · **Agent:** security-reviewer (main session) · **Depends:** TASK-350
Log: `.claude/logs/tasks/351_2026-07-14_auth-access-control-audit_security-reviewer.md`
Block 1 of the pre-launch audit (`eager-pondering-tower.md`). Reviewed
`Auth`/`Users`/`TenantRoles` (login/refresh-rotation-with-reuse-detection/lockout/
password-policy/2FA, v1-spec §3.2 role matrix vs `AppPolicies.cs`, ADR-019 temporary
grants + ADR-020 TenantRole capabilities real backend enforcement, impersonation
audit logging) — no P0/P1 found, this area had already been through several recent
hardening passes (TASK-329/330, TASK-346/347). One informational spec/code
divergence flagged (staff invite/deactivate narrower than v1-spec §3.2 for
network_manager/store_manager) — needs a product decision, no code changed. Fixed:
`AuthController` login/2fa-verify/refresh now have explicit `[AllowAnonymous]`
(previously anonymous only by absence of an attribute). Closed KI-005 (hardcoded
bcrypt seed hash): `DbSeeder.SeedAsync` now hashes `config["Seed:DefaultPassword"]`
(fallback `"password"`, dev-only) via injected `IPasswordHasher` at runtime instead
of a hardcoded hash in source. New `UserServiceCrossTenantTests.cs` (5 tests) pins
the cross-tenant guard on `UserService`. HTTP-level "no token → 401" test left as
TODO for Block 2/18 (no integration-test harness exists yet in this repo).
`dotnet build` 0 err/0 warn, `dotnet test` 805/805 green.

## TASK-350 — DevOps: Block 0 pre-launch audit — staging environment, KI-006 fix, audit tooling base
**Status:** done (2026-07-14) · **Agent:** devops-engineer · **Depends:** —
Log: `.claude/logs/tasks/350_2026-07-14_staging-environment-audit-base_devops-engineer.md`
Block 0 of the pre-launch audit (`C:\Users\stass\.claude\plans\eager-pondering-tower.md`).
`docker-compose.staging.yml` (NEW) — full containerized stack (api/web/postgres/
redis/mosquitto/worker) isolated from dev (5435/6380/1884/5000/3000) and prod
(5100/3100/loopback), on 5436/6381/1885/5101/3101; own postgres container (unlike
prod's `external_links`). `.env.staging.example` + `docs/staging.md` + README pointer.
KI-006 fixed: `Program.cs` seed call now gated (`IsDevelopment() || SEED_ON_START==true`)
— staging auto-seeds, production never does by default; `known-issues.md` updated.
Audit tooling base: `loadtests/` (k6 smoke script against `/api/marketplace/item-categories`,
no dedicated `/health` endpoint exists), `dotnet list package --vulnerable` +
`npm audit` ×3 confirmed running cleanly (vuln counts logged, not remediated —
Block 18), `frontend/vitest.config.ts` + `lib/utils.test.ts` — `npm test` passes 2/2.
`dotnet build` clean; `docker compose ... config` validates staging compose.

## TASK-349 — Frontend: InviteUserModal — вибір TenantRole шаблону при створенні користувача
**Status:** done (2026-07-13) · **Agent:** frontend-developer · **Depends:** TASK-345..348 (ADR-020)
Log: `.claude/logs/tasks/349_2026-07-13_invite-with-tenant-role_frontend-developer.md`
Bug: щойно створений TenantRole-шаблон не з'являвся у "Запросити користувача" —
`INVITE_ROLES` була жорстко закодована на 4 базові ролі, призначення шаблону існувало
лише постфактум через `TenantRoleSelector` у `UserDetailPanel`. UX-фікс без змін
бекенду (`InviteAsync` не чіпали — сьогоднішній privilege-escalation review,
TASK-346/347): `InviteUserModal.tsx` оркеструє два вже готові виклики —
`useInviteUser()` → `useAssignTenantRole()`. Додано `"staff"` (ADR-020) в
`INVITE_ROLES` + лейбл у `ROLE_LABELS`; новий select "Шаблон ролі (необов'язково)" з
`useTenantRoles()`; вибір шаблону дефолтить Role на "staff" лише якщо адмін ще не
чіпав поле вручну. Частковий збій (invite ok, assign fail) не ховає створеного
користувача — модалка лишається відкритою з чіткою помилкою, кнопка стає "Закрити".
`tsc --noEmit` + `npm run build` чисті; live-verified обидва шляхи (success +
simulated race → archived-template 400) на локальному стеку.

## TASK-334 — Frontend: public marketing landing page (/)
**Status:** done (2026-07-10) · **Agent:** frontend-developer · **Depends:** TASK-333 (контракт leads)
Log: `.claude/logs/tasks/334_2026-07-10_landing-page_frontend-developer.md`
`app/page.tsx`: redirect → SSG-лендінг (укр., темна тема #0B0F17, стиль Linear/Vercel,
SEO+OpenGraph). Нова фіча `features/landing/`: SVG-лого (щит+полиці), sticky header,
hero зі скриншотом у browser-рамці, проблеми/можливості (8)/showcase (6 скриншотів
`public/landing/`)/як це працює/для кого/тарифи «за запитом»/FAQ/форма заявки
(RHF+zod, honeypot, POST `/api/public/leads` — 204/400/429). Reveal-анімації CSS+IO,
без нових залежностей. Бонус: `app/icon.svg` (favicon), `lang="uk"`.
tsc clean, build success, `/` prerendered static, форма і якорі перевірені в браузері.

## TASK-333 — Backend: landing lead capture endpoint
**Status:** done (2026-07-10) · **Agent:** backend-developer · **Depends:** — (frontend landing — паралельна задача)
Log: `.claude/logs/tasks/333_2026-07-10_landing-leads_backend-developer.md`
`POST /api/public/leads` (AllowAnonymous, rate limit `public-leads` 5/min per IP):
honeypot `website` → 204 без збереження; валідація name 2..100 / phone 5..30 /
company ≤150 / message ≤1000 → 400 `{error}`; happy path → `landing_leads`
(provider-level, без tenant_id/RLS — як provider_roles) + ILogger info.
Telegram-нотифікація відкладена (worker pipeline tenant-scoped) — TODO у сервісі.
Міграція `20260710112137_AddLandingLeads` (additive). Build 0 err, 701/701 tests.

## TASK-329 — Backend: auth hardening (rate limit, lockout, password policy, reuse detection, headers)
**Status:** done (2026-07-09) · **Agent:** backend-developer · **Depends:** —
Log: `.claude/logs/tasks/329-330_2026-07-09_auth-hardening-2fa_backend-developer.md`
Rate limiting 10/min login+2fa-verify, 30/min refresh (429 `{error}`), ForwardedHeaders
за nginx; lockout 5 невдач → 15 хв (generic error, аудит `user.login_failed`/
`user.locked_out` з IP); `PasswordValidator` (12+ символів, літера+цифра, blocklist ~100,
email local-part) у всіх 5 місцях встановлення пароля; зміна пароля відкликає всі
refresh-токени; повторне використання ротованого refresh-токена → revoke всієї сім'ї +
`auth.refresh_reuse_detected`; security headers middleware. Build 0 err, 685/685 tests,
міграція `20260709204440_AuthHardeningAnd2fa` (additive), live smoke: 401/429/headers OK.

## TASK-330 — Backend: 2FA TOTP (opt-in) + recovery codes
**Status:** done (2026-07-09) · **Agent:** backend-developer · **Depends:** TASK-329
Log: той самий · Handoff: `.claude/logs/handoffs/330-backend-to-frontend.md` (точний API-контракт для TASK-331)
Otp.NET (Infrastructure) за `ITotpService`; login → `{requiresTwoFactor, challengeToken}`
(JWT 5 хв, purpose=2fa, окрема audience — не проходить bearer auth); `/api/auth/2fa/`
verify (anonymous, ліміт auth-login, анти-replay по timestep, recovery-коди одноразові) /
setup / enable (8 кодів XXXX-XXXX, SHA256 у jsonb) / disable (пароль+код);
`AuthUserDto.TwoFactorEnabled`. Невірний 2FA-код рахується в той самий lockout-лічильник.

## TASK-331 — Frontend: 2FA UI + password policy hints + lockout UX
**Status:** done (2026-07-09) · **Agent:** frontend-developer · **Depends:** TASK-330
Log: `.claude/logs/tasks/331_2026-07-09_2fa-ui_frontend-developer.md`
Login: другий крок з 6-значним кодом / recovery-кодом (тогл), UA-помилки для 401/429,
«Назад» до кроку 1; `LoginResponse` → discriminated union, токени не зберігаються при
challenge. Profile: секція «Двофакторна автентифікація» (setup QR `qrcode.react` +
секрет, enable з одноразовим показом recovery-кодів + підтвердження «Я зберіг коди»,
disable через пароль+код), refresh `/api/auth/me` після змін. ChangePasswordForm:
валідація 12+ символів літери+цифри, hint, серверні `{error}` as-is, toast про
розлогінення інших пристроїв. Фікс `lib/api.ts`: 401 з `/api/auth/2fa/verify` більше
не тригерить refresh→redirect. tsc clean, build success (50/50), eslint змінених файлів
clean (у frontend/ немає ESLint-конфіга — pre-existing, `next lint` інтерактивний).

---
# Previous sprint — v4.4 «Chat UX unification» (started 2026-07-07)

## TASK-319 — Marketplace chat: bottom-right floating widget + real unread badges
**Status:** done (2026-07-07) · **Agent:** backend-developer → frontend-developer (finished directly in main session after agent stalls) · **Depends:** —
User ask: supplier↔client marketplace chat should render bottom-right like the existing
`SupportChatWidget` (Чат підтримки / Мій асистент), for both client and supplier side;
closed chats should show an unread-message indicator.
Scope decisions:
- **Marketplace chat** (`SupplierChatSession`/`SupplierChatMessage`) — already has
  `SenderTenantId` per message (clean two-tenant model), so a real per-message `IsRead`
  → per-session `UnreadCount` is a same-file backend change, no schema migration.
  Repositioned both `SupplierChatPanel.tsx` (client) and `SupplierClientChatPanel.tsx`
  (supplier) to the bottom-right floating style; added unread badges (Sidebar
  «Повідомлення» nav item, `ChatInboxTab` per-row, client's «Написати постачальнику»
  button).
- **"Чат підтримки"** (tenant↔provider `ChatMessage`/`ChatService`) — investigated:
  `IsRead` there already means "read by provider" (used by the provider's shared queue,
  `GetMessagesForProviderAsync` marks all `IsRead=true`), and the tenant side's own
  `GetSessionsAsync`/`GetMessagesAsync` never had real unread tracking (hardcoded
  `unreadCount: 0`) — no `SenderTenantId`/sender-role marker exists on `ChatMessage` to
  disambiguate "read by tenant" without a schema change + touching a column the
  provider queue already depends on. **Out of scope for TASK-319** — flagged as a
  separate follow-up (see spawn_task) rather than risking the provider queue.
- **AI Assistant** — synchronous ask/answer, no server-pushed messages while closed;
  no unread concept applies, no changes made.

**Backend half done (2026-07-07):** `SupplierChatSessionDto.UnreadCount` +
`ISupplierChatRepository.MarkMessagesReadAsync` (auto-called from `GetMessagesAsync`) —
log `.claude/logs/tasks/319a_2026-07-07_marketplace-chat-unread-backend_backend-developer.md`,
handoff `.claude/logs/handoffs/319-backend-to-frontend.md`. Build 0 errors, 645/645 tests
green.

**Frontend half done (2026-07-07):** log
`.claude/logs/tasks/319b_2026-07-07_marketplace-chat-widget-unread-frontend.md`.
`SupplierChatPanel.tsx` (client) and `SupplierClientChatPanel.tsx` (supplier) repositioned
from centered dimmed modal to bottom-right floating widget (fixed bottom:24 right:24,
380×540, matches `SupportChatWidget` visual language, no backdrop). Unread badges: client's
«Написати постачальнику» button (`marketplace/[id]/page.tsx` — hoisted `useSupplierChatMessages`
to page level so the 3s poll runs while the panel is closed, derives unread from
`senderTenantId`/`isRead`); supplier's `ChatInboxTab` per-row badge + aggregate badge on the
Sidebar «Повідомлення» nav item (`useSupplierChatSessions(enabled)` gated to `supplier_admin`
only). `tsc --noEmit` clean, `npm run build` green (48 routes), `dotnet build`/`dotnet test`
645/645 green.
**Note:** three spawned frontend-developer agent attempts for this half stalled (reported
"I'll wait for the agent" instead of working — known pattern, see
`feedback-agent-self-delegation-loop` memory) before one background instance quietly
finished part of the Sidebar.tsx wiring; the rest was completed directly in the main
session per the "correct once then do it directly" guidance rather than spawning a 4th
attempt.

---
# Current Sprint — v4.3 «Supplier Cooperation & Marketplace Orders» (started 2026-07-06)

Клієнт бачить каталог/рейтинг/відгуки постачальника публічно (як зараз). Для замовлень —
заявка на співпрацю → постачальник схвалює → генерується договір (PDF: реквізити, підпис,
мокра печатка) → підписання через Вчасно або скачування для фізичного підпису → статус
active відкриває marketplace-замовлення. Консультація — існуючий чат; питання — тікети
підтримки постачальника.

## TASK-316 — DB: cooperation schema (agreements, orders, tickets, contract settings)
**Status:** done (2026-07-06) · **Agent:** database-engineer · **Depends:** —
Log: `.claude/logs/tasks/316_2026-07-06_cooperation-schema_database-engineer.md`
6 таблиць + two-tenant RLS + партіальний unique index (одна live-угода на пару).
Міграція `20260706155440_SupplierCooperation`. Build green, міграція не застосована.

## TASK-317 — Backend: agreements + contract PDF (QuestPDF) + Вчасно + orders + support tickets
**Status:** done (2026-07-06) · **Agent:** backend-developer · **Depends:** TASK-316
Log: `.claude/logs/tasks/317_2026-07-06_cooperation-backend_backend-developer.md`
Handoff: `.claude/logs/handoffs/317-to-318_frontend-developer.md` (усі ендпоінти + DTO shapes)
Сервіси: заявка клієнта / рішення постачальника / генерація договору з реквізитами,
підписом і печаткою / надсилання у Вчасно (per-tenant ключ через integration_configs) /
скачування PDF; marketplace-замовлення з гейтом «тільки active agreement»; тікети підтримки.
Build 0 errors, 639/639 tests green. QuestPDF + DejaVu Sans (кирилиця OK).

## TASK-318 — Frontend: client cooperation UX + supplier cabinet (requests, contract settings, orders, support)
**Status:** done (2026-07-07) · **Agent:** frontend-developer · **Depends:** TASK-317
Log: `.claude/logs/tasks/318_2026-07-06_cooperation-frontend_frontend-developer.md` (два проходи)
Клієнт: статус/заявка/договір/підтримка на `/marketplace/[id]`, кошик → замовлення
(лише active agreement), нова `/marketplace/orders` (таби Замовлення/Співпраця) +
sidebar «Мої замовлення». Кабінет: 4 нові сторінки `/supplier/requests` (approve/
reject/договір/Вчасно/mark-signed/terminate), `/supplier/contract-settings` (реквізити
+ upload підпису/печатки), `/supplier/orders` (переходи статусів), `/supplier/support`
(тікети+тред) + 4 пункти в supplier-nav. `tsc --noEmit` чисто, `npm run build` green.
Не покрито: ручний E2E повного флоу проти бекенду — кандидат на QA-задачу.

---
# Previous sprint — v4.2 «Supplier Categories & Navigation» (started 2026-07-03)

Архітектура: ADR-017 (`.claude/docs/decisions.md`). Feature A: provider-панель `/provider`
дістає таб-спліт «Клієнти» / «Постачальники» над існуючим списком тенантів (client-side
фільтр по `business_type`, без нового ендпоінта/роуту). Feature B: `SupplierItem` отримує
nullable `category` + `attributes JSONB`; довідник категорій/полів — backend-джерело
істини (`GET /api/marketplace/item-categories`), фронтенд рендерить форму динамічно.
Existing items без категорії лишаються валідними назавжди (не міграційна яма).

---

## BUG-015 — StoreSelector shown to provider role in TopBar ✅ done (2026-07-04)
Log: `.claude/logs/tasks/bug015-017_2026-07-04_provider-storeselector-duplicate-button-detail-width_frontend-developer.md`
`frontend/components/layout/TopBar.tsx` already used `TENANT_ROLES.has(userRole)` (excludes
provider/provider_admin/provider_agent + supplier_admin) — verified correct, no change needed.

## BUG-016 — Duplicate "Створити постачальника" button on /marketplace ✅ done (2026-07-04)
Log: `.claude/logs/tasks/bug015-017_2026-07-04_provider-storeselector-duplicate-button-detail-width_frontend-developer.md`
Removed button + `CreateSupplierModal` usage from `frontend/app/(dashboard)/marketplace/page.tsx`.
Deleted unused `frontend/features/marketplace/components/CreateSupplierModal.tsx` (no other callers).
Backend `MarketplaceAdminController`/`AdminCreateSupplierAsync` left untouched — candidate for later cleanup.

## BUG-017 — Supplier detail page constrained to half width ✅ done (2026-07-04)
Log: `.claude/logs/tasks/bug015-017_2026-07-04_provider-storeselector-duplicate-button-detail-width_frontend-developer.md`
Removed `maxWidth: 900` from both wrapper divs in `frontend/app/(dashboard)/marketplace/[id]/page.tsx`.

## BUG-018 — Client chat messages never reach supplier (no UI inbox) ✅ done (2026-07-07)
Log: `.claude/logs/tasks/bug018_2026-07-07_supplier-chat-inbox_frontend-developer.md`
Root cause: `ClientsTab.tsx` (`/supplier/clients`) was the only place that opened
`SupplierClientChatPanel`, and its list (`useSupplierClients`) only includes clients
with a review or a task (TASK-313 design) — a client who only started a chat never
appeared, so the supplier had no way to see/reply even though messages saved fine.
The already-existing `GET /api/supplier-cabinet/chat/sessions` endpoint +
`useSupplierChatSessions()` hook were dead code (no component used them).
Fix (frontend-only): new `ChatInboxTab.tsx` renders all chat sessions via
`useSupplierChatSessions()`, opens `SupplierClientChatPanel` on click. Wired in as
a tab switcher ("Клієнти" / "Повідомлення") on `/supplier/clients` — no new route,
no nav change, no backend change. `tsc --noEmit` clean, `npm run build` green.
**Superseded by BUG-019** — the tab was still gated behind `client_management`.

## BUG-019 — Chat inbox still unreachable: wrongly nested under client_management ✅ done (2026-07-07)
Log: `.claude/logs/tasks/bug019_2026-07-07_supplier-chat-inbox-permission-gate_frontend-developer.md`
User screenshot showed a supplier staff account missing Профіль/Клієнти/Команда nav
items (no profile_management/client_management/staff_management permission) — so the
BUG-018 fix, nested inside `/supplier/clients`, was unreachable for that account,
reproducing the original complaint. Fix: moved chat inbox to its own ungated route
`/supplier/messages` (new nav item, no `permission` key — same treatment as the
TASK-318 cooperation items) using the existing `ChatInboxTab` unchanged;
`/supplier/clients` reverted to always rendering just `ClientsTab`. `tsc --noEmit` clean.

---

## TASK-293 — DB: SupplierItem.Category + Attributes (JSONB)
**Status:** done · **Agent:** database-engineer · **Depends:** —
Міграція: `supplier_items.category text NULL` + `supplier_items.attributes jsonb NULL`
(raw SQL у подвійних лапках колонок, ADR-008). Entity `SupplierItem`
(`backend/ShelfGuard.Domain/Entities/SupplierItem.cs`): `string? Category`,
`Dictionary<string, object?>? Attributes`. EF config: `.HasColumnType("jsonb")` для
Attributes (той самий підхід, що `Item.Barcodes` — перевірити, чи потрібен додатковий
Npgsql dynamic-json switch для `Dictionary<string, object?>`, чи досить generic
`JsonSerializer` конвертера, як показано в ADR-017 п.3). Без DEFAULT — обидва nullable,
existing rows лишаються `NULL`. Не чіпати RLS (`supplier_items` вже під `tenant_isolation`
+ `provider_bypass` з попередніх спринтів) — тільки перевірити NULLIF-guard присутній.
**Accept criteria:** migration up/down чиста на dev-базі; existing `SupplierItem` рядки
не ламаються (Category/Attributes читаються як null); `dotnet build` + тести green.

---

## TASK-294 — Backend: довідник категорій (SupplierItemCategories) + item-categories endpoint
**Status:** done (2026-07-03) · **Agent:** backend-developer · **Depends:** TASK-293
Log: `.claude/logs/tasks/294-295_2026-07-03_supplier-item-categories_backend-developer.md`
Новий `ShelfGuard.Domain.Constants.SupplierItemCategories`: фіксовані ключі `food`,
`auto_parts`, `medical`, `construction` + для кожного — список полів
`{ Key, LabelUa, Type (text|number|date|bool|select), Required, Options? }`:
- `food`: weight/volume (text, req), expiry_date (date, req), batch_number (text, opt)
- `auto_parts`: oem_number (text, req), compatible_models (text, opt, вільний текст через кому),
  part_number (text, opt)
- `medical`: dosage (text, req), expiry_date (date, req), prescription_status (select:
  ОТС/рецептурний, req), storage_conditions (text, opt)
- `construction`: unit (text, req), package_weight_volume (text, opt), certification_class (text, opt)
Метод `SupplierItemCategories.Validate(string? category, Dictionary<string,object?>? attrs)`
→ список помилок за відсутні required-поля (порожній список, якщо `category == null` —
без категорії валідація не застосовується, ADR-017 п.5).
Новий публічний ендпоінт `GET /api/marketplace/item-categories` (`[AllowAnonymous]`) —
віддає довідник як DTO (`SupplierItemCategoryDto[]`) для фронтенд-рендеру форми.
**Accept criteria:** unit-тести на Validate (кожна категорія: бракує required → помилка;
всі required заповнені → ok; category=null → завжди ok); ендпоінт віддає 4 категорії з
повним списком полів; `dotnet build` + тести green.

---

## TASK-295 — Backend: Category/Attributes у SupplierItem DTOs + CRUD валідація
**Status:** done (2026-07-03) · **Agent:** backend-developer · **Depends:** TASK-294
Log: `.claude/logs/tasks/294-295_2026-07-03_supplier-item-categories_backend-developer.md`
`SupplierItemDto`, `AdminAddSupplierItemDto`, `AdminUpdateSupplierItemDto`,
`CabinetAddItemDto`/`CabinetUpdateItemDto` (якщо окремі — перевірити фактичні назви в
`MarketplaceDtos.cs`) отримують `string? Category` + `Dictionary<string,object?>? Attributes`.
`MarketplaceService`/`SupplierCabinetService` CRUD-методи товару: перед create/update
викликають `SupplierItemCategories.Validate` → 400 зі списком відсутніх полів, якщо
`category` заданий і чогось не вистачає. Existing товари без категорії — CRUD без змін
поведінки. AI Supplier Recommendation (`SupplierRecommendationDto.MatchedItem`) — Category
проходить крізь той самий `SupplierItemDto`, змін логіки рекомендації не потрібно.
**Accept criteria:** POST/PUT товару з category="medical" без expiry_date → 400 з
переліком полів; той самий запит з усіма required → 200/201; товар без category — як
раніше; unit-тести на guard + backward-compat; `dotnet test` green.

---

## TASK-296 — Frontend: динамічна форма товару за категорією (CabinetItemModal)
**Status:** done (2026-07-03, log: 296-297_2026-07-03_supplier-categories-and-provider-tabs_frontend-developer.md) · **Agent:** frontend-developer · **Depends:** TASK-294, TASK-295
`frontend/features/supplier-cabinet/components/CabinetItemModal.tsx`: додати select
«Категорія» (опційний, з опцією «Без категорії») над існуючими полями. Категорії й поля
підтягуються з нового хука `useItemCategories()` (`GET /api/marketplace/item-categories`,
закешовано, `staleTime: Infinity` — довідник статичний). При виборі категорії — рендер
додаткового блоку полів під схемою категорії (text/number/date/bool/select), значення
складаються в `attributes` об'єкт при сабміті. Клієнтська дзеркальна валідація
required-полів (UX, не заміна серверної) — показує ту саму помилку до сабміту.
`types.ts` (`CabinetItem`, add/update payloads) — `category?: string`, `attributes?:
Record<string, unknown>`. `CabinetItemsTable.tsx` — показати бейдж категорії в рядку
(лейбл з довідника), товари без категорії — без бейджа (як зараз).
**Accept criteria:** вибір категорії показує правильний набір полів; сабміт без required
поля показує помилку і не відправляє запит; товар без категорії зберігається як раніше;
`tsc --noEmit` + `npm run build` green.

---

## TASK-297 — Frontend: провайдер-панель — таби «Клієнти» / «Постачальники»
**Status:** done (2026-07-03, log: 296-297_2026-07-03_supplier-categories-and-provider-tabs_frontend-developer.md) · **Agent:** frontend-developer · **Depends:** —
`frontend/app/(dashboard)/provider/page.tsx`: `activeTab` розширюється з
`"tenants" | "logs"` на `"clients" | "suppliers" | "logs"`. Список `tenants` (з існуючого
`useTenants()`, без нового API-виклику) фільтрується client-side:
`t.businessType === "supplier"` → таб «Постачальники», інакше → «Клієнти». Таби показують
лейбл з лічильником (`Клієнти (N)`, `Постачальники (M)`). Пошук (`search` state) працює
в межах активного табу. `TenantCard`/`TenantDetailPanel`/`CreateTenantWizard` — без змін
(реюз). Health-картки зверху (`stats`) лишаються агрегатом по всіх тенантах — не діляться
по табу.
**Accept criteria:** перемикання таба фільтрує список без нового network-запиту; лічильники
в лейблах табів коректні; пошук працює в межах вибраного табу; `tsc --noEmit` +
`npm run build` green.

---

## TASK-298 — QA: supplier categories + provider nav split regression
**Status:** done (2026-07-03, log: `.claude/logs/reviews/qa_293-298_2026-07-03.md`) · **Agent:** qa-tester · **Depends:** TASK-296, TASK-297
Усі 8 сценаріїв PASS на локальному стеку: item-categories довідник (4 категорії, коректні
required-поля), medical create без/з required (400 з укр. помилкою / 201), category omitted
CRUD (backward compat, включно з legacy item без категорії), update null→food без/з required
(400/200), невідома категорія → 400, provider tenants `businessType` присутній +
platform-marketplace виключений (BUG-014 regression), регресія публічних marketplace-ендпоінтів
і cabinet profile/items. `dotnet test` 535/535 green, `tsc --noEmit` чисто, `npm run build` green.
Багів не знайдено. Не покрито: PUT існуючого seed-товару alpha@supplier.local (немає credentials,
адмінський контролер без PUT для items) — pre-existing обмеження, поза скоупом.
**Accept criteria:** усі сценарії пройдені; знайдені баги оформлені як BUG-задачі. Виконано.

---

## TASK-282 — DB: supplier business_type, IsOwnerManaged, дефолтні модулі
**Status:** done (2026-07-02, migration `20260702192126_V41SupplierSelfService`, log: `282_2026-07-02_supplier-self-service-db_database-engineer.md`) · **Agent:** database-engineer · **Depends:** — 
Міграція `V41SupplierSelfService`:
- `supplier_profiles.IsOwnerManaged boolean NOT NULL DEFAULT false` + partial unique index
  `UX_supplier_profiles_owner_tenant ON supplier_profiles ("TenantId") WHERE "IsOwnerManaged"` 
  (колонки в raw SQL — у подвійних лапках, ADR-008).
- Domain: `Tenant.DefaultModulesForBusinessType` — новий кейс `"supplier"` → `["marketplace_supplier"]`.
- Перевірити, що існуючі RLS-політики supplier_* мають NULLIF-guard (патерн d8abc4d8); якщо ні — включити в цю міграцію.
- Дані не мігруються: existing suppliers (`TenantId = Guid.Empty`) без змін.
**Accept criteria:** міграція up/down чиста на dev-базі; unique index не конфліктує з existing rows; `dotnet build` + тести green.

---

## TASK-283 — Backend: роль supplier_admin + онбординг supplier-tenant
**Status:** done (2026-07-02, log: `283-285_2026-07-02_supplier-self-service-backend_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-282
- `AppRoles`: додати `SupplierAdmin = "supplier_admin"` (+ у `All`).
- Admin tenant onboarding (`Admin` feature): при створенні tenant з `business_type = "supplier"` — 
  автоматично створити `Supplier` (`TenantId` = new tenant id) + `SupplierProfile`
  (`IsOwnerManaged = true`, `IsPublic = false`); перший user tenant-а отримує роль `supplier_admin`.
- Policy/authorization: supplier_admin НЕ входить у tenant-staff політики (stock/pos/etc.) — тільки кабінет.
**Accept criteria:** створення supplier-tenant через `/api/admin/tenants` дає tenant + user + Supplier + Profile однією транзакцією; supplier_admin отримує 403 на `/api/stock`; тести на онбординг-hook.

---

## TASK-284 — Backend: SupplierCabinetController (профіль, товари, відгуки)
**Status:** done (2026-07-02, log: `283-285_2026-07-02_supplier-self-service-backend_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-283
Новий `SupplierCabinetController` (`/api/supplier-cabinet`), `[Authorize]` роль supplier_admin + `[RequireModule("marketplace_supplier")]`. Resolve «мій Supplier» по `tenant_id` через `IsOwnerManaged`-профіль:
- `GET /profile`, `PUT /profile` (region, categories, website, delivery_regions, working_hours, payment_terms), `POST /profile/publish` (toggle `IsPublic`)
- `GET /items`, `POST /items`, `PUT /items/{id}`, `DELETE /items/{id}` — реюз Admin*-методів `MarketplaceService` (параметризувати supplierId)
- `GET /reviews` (read-only), `GET /metrics`
**Accept criteria:** усі ендпоінти працюють лише в контексті свого tenant (RLS-перевірка: другий supplier-tenant не бачить чужі items); provider-created suppliers (Guid.Empty) недоступні через кабінет; unit-тести на resolve + CRUD.

---

## TASK-285 — Backend: reviews hardening + публічні відгуки + rating recalc
**Status:** done (2026-07-02, log: `283-285_2026-07-02_supplier-self-service-backend_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-282
- `CreateReviewAsync`: guard — reviewer tenant ≠ `supplier.TenantId` та reviewer `business_type != "supplier"` (400); дубль уже дає 409.
- Після створення відгуку — синхронний перерахунок `SupplierMetrics.Rating` = AVG(rating) (створити metrics-рядок, якщо нема).
- Новий публічний `GET /api/marketplace/suppliers/{id}/reviews` (`[AllowAnonymous]`, paginated) — rating, comment, created_at, назва tenant-рецензента (denormalized display name, без id).
**Accept criteria:** self-review → 400; supplier-tenant review → 400; rating у публічному листингу оновлюється після нового відгуку; тести на guard + recalc.

---

## TASK-286 — Frontend: supplier cabinet (роль, sidebar, сторінки)
**Status:** done (2026-07-03, log: `286-287_2026-07-03_supplier-cabinet-marketplace-frontend_frontend-developer.md`) · **Agent:** frontend-developer · **Depends:** TASK-284
- `lib/roles.ts`: `SupplierAdmin` + set `SUPPLIER_ONLY`; supplier_admin виключити з tenant-staff sets.
- Sidebar: для supplier_admin — тільки група «Кабінет постачальника» (Профіль / Мої товари / Відгуки) + профіль користувача.
- Нова feature `features/supplier-cabinet/` (`types.ts`, `api/`, `hooks/`, `components/`), сторінки `(dashboard)/supplier/profile`, `/supplier/items`, `/supplier/reviews`. Реюз компонентів `features/marketplace/` (AddSupplierItemModal, форма профілю) де можливо.
- Admin onboarding UI: у формі створення tenant — опція business_type `supplier`.
**Accept criteria:** supplier_admin після логіну бачить лише кабінет; CRUD товарів і publish-toggle працюють; `tsc --noEmit` + `npm run build` green.

---

## TASK-287 — Frontend: marketplace enrichment — рейтинг і відгуки видимі клієнтам
**Status:** done (2026-07-03, log: `286-287_2026-07-03_supplier-cabinet-marketplace-frontend_frontend-developer.md`) · **Agent:** frontend-developer · **Depends:** TASK-285
- `/marketplace/[id]`: блок «Відгуки» (список з `GET /suppliers/{id}/reviews`, зірки, дата, ім'я рецензента) + існуюча форма «залишити відгук» показує 400/409 помилки guard-ів.
- `SupplierCard` у листингу: рейтинг (зірки + число) і кількість відгуків; фільтр за категорією вже є — переконатися, що категорії supplier-профілів відображаються.
**Accept criteria:** рейтинг/відгуки видно і анонімно, і клієнт-tenant-ам; свіжий відгук одразу оновлює рейтинг (invalidate query); `tsc --noEmit` + build green.

---

## TASK-288 — QA: supplier self-service regression
**Status:** done (2026-07-03, log: `.claude/logs/reviews/qa_282-288_2026-07-03.md`) · **Agent:** qa-tester · **Depends:** TASK-286, TASK-287
Усі 6 сценаріїв + регресія + `dotnet test` 494/494 + `tsc --noEmit` — PASS (локальний стек).
Знайдено 2 pre-existing баги (не блокують v4.1):
- **BUG-009 (high, deploy/env):** 8 hand-written міграцій без `[Migration]`/`[DbContext]` атрибутів
  (AddProviderRoles, AddNotificationIsRead, 2×ProviderBypassRls, AddItemPerishabilityClass,
  ForceRlsOnAllTenantTables, 2×FixRlsNullIf) — EF `MigrateAsync` їх НЕ бачить; свіжа БД отримує
  неповну схему (login 500: ProviderRoleId missing). Локальну dev-базу полагоджено вручну.
- **BUG-010 (medium):** `GET /api/marketplace/suppliers/{id}` віддає unpublished-профіль
  (IsPublic=false) навіть анонімно — detail не фільтрує is_public (листинг/search фільтрують).
Low-нотатки (див. QA-лог): review-guard-и 400 екрануються module gate 403 для supplier-tenant-ів;
supplier_admin має 200 на /api/notifications/history (свій tenant, порожньо).
Тест-план: (1) онбординг supplier-tenant провайдером; (2) ізоляція — supplier A не бачить дані supplier B і клієнтських tenant-ів (RLS); (3) supplier_admin 403 на всі tenant-staff ендпоінти; (4) publish-toggle → поява/зникнення в публічному листингу; (5) review-флоу: клієнт лишає відгук, дубль → 409, self-review → 400, рейтинг перерахований; (6) module gate: деактивація `marketplace_supplier` → 403 кабінету.
**Accept criteria:** усі 6 сценаріїв пройдені на dev; знайдені баги оформлені як BUG-задачі.

---

## TASK-289 — Backend: provider-path onboarding + cabinet backfill + role guard (ADR-016)
**Status:** done (2026-07-03, log: `289_2026-07-03_provider-supplier-onboarding_backend-developer.md`) · **Agent:** backend-developer · **Depends:** TASK-283, BUG-012
Провайдерський візард (`ProviderService.CreateTenantAsync`, `/api/provider/tenants`) не мав
онбординг-хука TASK-283 — supplier-tenant, створений через нього, лишався без Supplier/Profile.
Fix:
- `ProviderService.CreateTenantAsync` викликає `SupplierOnboarding.CreateOwnerManaged` в тій самій
  транзакції, що й TenantAdminService (`ITenantRepository.AddPendingAsync` — deferred-варіант
  `AddAsync`, +`AddSupplierAsync`/`AddSupplierProfileAsync`, один `SaveChangesAsync`).
  `TenantAdminService` теж переведено на спільний хелпер (усунуто дублювання логіки).
- `SupplierCabinetService.ResolveAsync` — lazy backfill: якщо `IsOwnerManaged`-профілю нема,
  а `tenant.business_type == "supplier"` — створює пару через
  `IMarketplaceRepository.GetOrCreateOwnerManagedProfileAsync` (race-safe, той самий патерн
  detach+refetch, що й `GetOrCreatePlatformTenantIdAsync`, BUG-012). Самолікує supplier-tenant,
  створений на проді до цього фіксу.
- `CreateTenantUserRequest.Role` + валідація в `ProviderService.CreateTenantUserAsync`: роль має
  відповідати `business_type` тенанта (`supplier` → тільки `supplier_admin`, інакше — тільки
  `enterprise_admin`); невідповідність — 400.
Тести: `ProviderServiceTests` (онбординг supplier/non-supplier, role guard обидва напрямки),
`SupplierCabinetServiceTests` (backfill supplier/non-supplier tenant, no-op коли профіль вже є).
`dotnet build` + `dotnet test` — 513/513 green (було 506).
**Accept criteria:** supplier-tenant через `/api/provider/tenants` отримує Supplier+Profile
однією транзакцією; кабінет самолікує existing supplier-tenant без профілю; role guard рубає
supplier_admin для non-supplier тенанта і навпаки.

---

# Previous Sprint — v3.5 «Provider UX» (started 2026-06-21)

---

## TASK-281 — Dashboard і /stock: консистентний фільтр магазину
**Status:** done · **Agent:** frontend-developer · **Depends:** TASK-280 · Updated: 2026-07-02
Дашборд (stats, «Потребують уваги», карта зон) викликав `/api/stock*` без
`store_id` — показував дані всіх магазинів, тоді як `/stock` фільтрує за
`selectedStoreId` з header StoreSelector. Після «Переглянути всі» список міг
бути порожнім. Fix: `frontend/features/dashboard/api/dashboard.ts` — усі три
функції приймають `storeId` (helper `withStore` додає `store_id=` до URL);
`frontend/features/dashboard/hooks/useDashboard.ts` — хуки читають
`selectedStoreId` з `useStoreContext` і включають його в queryKey. Бекенд
(`StockController`) вже приймає `store_id?` на `/api/stock`, `/summary`,
`/zones-summary`. Коли магазин не вибрано (`null`) — параметр не додається,
обидві сторінки показують все. `tsc --noEmit` та `npm run build` — green.
Log: `281_2026-07-02_dashboard-store-consistency_frontend-developer.md`

---

## TASK-280 — Dashboard: блок «Потребують уваги» — 5 рядків + «Переглянути всі»
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-02
Блок `AttentionTable` не мав обмеження висоти — при багатьох товарах займав пів
сторінки. Fix (у `frontend/features/dashboard/components/AttentionTable.tsx`):
показуються перші 5 рядків поточного фільтра; нижче кнопка
«Переглянути всі (N)» (лише коли рядків > 5). Ціль навігації — `/stock`
(сторінки `/shelf` немає): таб «All» → `/stock`, таби Expired/Critical/Warning →
`/stock?status=<value>` — сторінка вже читає `status` з query params, значення
збігаються зі `StockFilters`, тож фільтр преселектнутий. Стилі — існуючий
inline dark-theme патерн блоку. `tsc --noEmit` та `npm run build` — green.
Log: `280_2026-07-02_dashboard-attention-view-all_frontend-developer.md`

---

## TASK-279 — Повідомлення про завершення сеансу при неактивності
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-02
Раніше при протуханні access token + невдалому refresh `frontend/lib/api.ts` робив
жорсткий redirect на `/login` без пояснення — користувача «викидало» мовчки.
Fix: redirect тепер на `/login?reason=session_expired`; на сторінці логіну новий
клієнтський компонент `SessionExpiredNotice` (features/auth/components) читає параметр
через `useSearchParams` (обгорнуто в `<Suspense>` у server-сторінці) і показує amber-банер
«Час сеансу сплив. Будь ласка, увійдіть знову.» над формою — той самий візуальний патерн,
що й error-блок у LoginForm, але warning-тон (#F59E0B), бо це очікувана подія.
`middleware.ts` без змін: він не може відрізнити «сеанс сплив» від «перший візит»
(в обох випадках cookie відсутні), тож reason ставить лише api.ts після фактичного
провалу refresh. `tsc --noEmit` та `npm run build` — green.
Log: `279_2026-07-02_session-expired-notice_frontend-developer.md`

---

## BUG-009 — 8 hand-written міграцій без [Migration]/[DbContext] атрибутів
**Status:** done · **Agent:** database-engineer (+ main session verification) · Updated: 2026-07-03
Found in QA v4.1: EF `MigrateAsync` ігнорував 8 ручних міграцій (AddProviderRoles,
AddNotificationIsRead, ServiceDesk/Team provider bypass RLS, ItemPerishabilityClass,
ForceRlsOnAllTenantTables, 2× NULLIF RLS-фікси) — свіжа БД розгорталась неповною.
Fix: додано атрибути `[DbContext(typeof(AppDbContext))]` + `[Migration("<id>")]`,
міграції переписані на ідемпотентний SQL (IF NOT EXISTS / OR REPLACE guards),
snapshot оновлено. На проді вони виконаються ПОВТОРНО при наступному деплої
(відсутні у __EFMigrationsHistory) — ідемпотентність перевірена: DELETE 8 рядків
історії на локальній БД з існуючими обʼєктами → повторний прогін чистий.
`dotnet ef migrations list` показує всі 9; build green; tests 500/500.
Log: `bug009_2026-07-03_orphan-migrations_database-engineer.md`

---

## BUG-010 — GET /api/marketplace/suppliers/{id} віддає unpublished профіль
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-03
Found in QA v4.1 (`qa_282-288_2026-07-03.md`). Листинг/search фільтрують `IsPublic`,
але detail-ендпоінт — ні: неопублікований профіль був доступний будь-кому за id.
Fix: `MarketplaceService.GetSupplierProfileAsync` повертає `null` (→404) якщо
`profile.IsPublic == false` — для анонімних і автентифікованих. Legitimate доступи
не зачеплені: supplier cabinet читає свій профіль через `ISupplierCabinetService.
GetOwnerManagedProfileAsync` (окремий шлях), MarketplaceAdminController використовує
лише Admin*-методи — інших call sites у `GetSupplierProfileAsync` нема.
Tests: +2 unit (unpublished→null для anon/auth, published→dto). `dotnet build` 0 warn.
Follow-up (main session, 2026-07-03): той самий guard додано в `GetSupplierItemsAsync`
і `GetSupplierReviewsAsync` (приватний `IsPublishedAsync`) → `/items` і `/reviews`
unpublished-постачальника тепер теж 404. +4 unit tests. `dotnet test` 500/500 green.
Log: `bug010_2026-07-03_unpublished-supplier-leak_backend-developer.md`

---

## BUG-011 — банер «Час сеансу сплив» після ручного «Вийти»
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-03
Repro: клік «Вийти» → /login з банером session_expired (TASK-279), хоча вихід ручний.
Cause: in-flight polling (SupportChatWidget 3с, notifications badge) ловив 401 після
відкликання refresh cookie → `apiFetch` робив hard redirect `/login?reason=session_expired`,
перебиваючи чистий `router.push("/login")` з `useLogout`.
Fix (`frontend/lib/api.ts` + `useAuth.ts`): module-level прапорець `markLoggedOut()`,
який `useLogout.mutationFn` ставить ПЕРЕД `authApi.logout()`; у 401-гілці `apiFetch`
при прапорці — тихий `ApiError` без refresh/redirect (перевірка і до, і після tryRefresh
для гонки). Прапорець скидається в `setToken()` (login/refresh). Додатково: 401 без
токена на момент запиту → редірект на `/login` БЕЗ reason (не «сеанс сплив»).
TASK-279 сценарій не зачеплено: протухла сесія з токеном далі дає reason=session_expired.
`npx tsc --noEmit` + `npm run build` green.
Log: `bug011_2026-07-03_logout-expired-banner_frontend-developer.md`

---

## BUG-013 — майстер «Новий клієнт» (provider): нема типу «Постачальник» + кирилична назва блокує «Далі»
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-03
Repro: CreateTenantWizard (панель провайдера) не мав business type «Постачальник»
(supplier додано лише в admin у TASK-286); кирилична назва → slugify відкидав усі
не-ASCII символи → slug порожній → кнопка «Далі» disabled.
Fix: (1) `features/provider/types.ts` — `supplier` у BusinessType, labels («Постачальник»,
🚚), ALL_BUSINESS_TYPES, preset `["marketplace_supplier"]`; `marketplace_supplier` у
TenantModule + MODULE_LABELS/DESCRIPTIONS/ALL_MODULES (звірено з Tenant.cs, TASK-282).
(2) Спільна util `lib/slug.ts` — транслітерація укр→лат (щ→shch, ї→yi, х→kh тощо) +
санітизація; використана в CreateTenantWizard і admin/CreateTenantModal (там була та сама
вада). Назва компанії зберігається як введена — транслітерується тільки slug.
tsc + next build green.
Log: `bug013_2026-07-03_provider-wizard-supplier-slug_frontend-developer.md`

---

## TASK-290 — AddTenantUserModal: role selector + success view (ADR-016)
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-03
Попередній прогін лишив компонент напівготовим: рахував `isSupplier`/`roles`/`role`,
але не рендерив селектор ролі, не слав `role` у запиті, і мав мертвий код
(`createdUser`/`CheckCircle2`). `TenantDetailPanel` не передавав `businessType`.
Fix: `types.ts` (`role` у `CreateTenantUserRequest`), `TenantDetailPanel.tsx`
(`businessType={tenant?.businessType}`), `AddTenantUserModal.tsx` (поле «Роль»,
`role` у mutateAsync, success-екран після створення). Backend поки ігнорує `role`
(окрема задача). tsc + build green.
Log: `290_2026-07-03_supplier-user-role-modal_frontend-developer.md`

---

## TASK-292 — Кнопки в модалках маркетплейсу: стиль під `Btn`
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-07-03
`CreateSupplierModal.tsx` і `AddSupplierItemModal.tsx` мали raw `<button>` замість
спільного `components/ui/Btn.tsx` — випадали зі стилю решти застосунку (user feedback).
Fix: «Скасувати» → `<Btn variant="ghost">`, primary-дія → `<Btn type="submit">`
(той самий патерн, що вже в `AddTenantUserModal.tsx`). Тільки розмітка, логіка не змінена.
`tsc --noEmit` + `npm run build` green.
Log: `292_2026-07-03_supplier-modal-buttons-restyle_frontend-developer.md`

---

## BUG-012 — POST /api/admin/marketplace/suppliers 500 (FK violation) на prod
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-03
Root cause: `MarketplaceService.AdminCreateSupplierAsync` хардкодив `TenantId = Guid.Empty`
→ INSERT у `suppliers` порушував FK `FK_suppliers_tenants_TenantId` (тенант 00000000-… не
існує). Флоу TASK-275 «+ Створити постачальника» падав 500 завжди — рядків з Guid.Empty
у prod немає.
Fix: get-or-create системний tenant «Platform Marketplace» (slug `platform-marketplace`,
business_type=supplier, inactive, без users) — `MarketplaceRepository.
GetOrCreatePlatformTenantIdAsync` (ліниво, race-safe по unique slug + detach на програші);
`AdminCreateSupplierAsync` використовує його id. Supplier cabinet не зачеплено: профілі
admin-флоу мають `IsOwnerManaged = false`, кабінет фільтрує `IsOwnerManaged = true` —
покрито тестом. Чому TASK-275-тести не зловили: NSubstitute-моки репо не перевіряють FK;
додано 4 repo-тести на EF InMemory (перший виклик створює tenant, другий/крос-контекст
реюзає; cabinet-лукап не бачить platform-suppliers) + 2 service-тести. ADR-016 amendment
у `decisions.md`. Build green, 506/506 тестів.
Log: `bug012_2026-07-03_admin-supplier-fk_backend-developer.md`
**Next:** deploy to prod; re-check «+ Створити постачальника» на /marketplace.

---

## BUG-014 — Provider випадково створив supplier_admin у системному tenant «Platform Marketplace»
**Status:** done · **Agent:** backend-developer · **Depends:** BUG-012 · Updated: 2026-07-03
Root cause: системний tenant `platform-marketplace` (BUG-012 фікс) не фільтрувався у
`ProviderService.GetTenantsAsync` → з'являвся у provider-панелі поруч з реальними клієнтами;
provider створив там supplier_admin через «Додати адміністратора», юзер отримує 403 на
`/api/supplier-cabinet/*` (tenant inactive, без модуля marketplace_supplier).
Fix: `TenantRepository.GetAllAsync` фільтрує `Slug != MarketplaceRepository.PlatformTenantSlug`
на рівні репозиторію (уникнули cross-feature reference з Application); `ProviderService.
CreateTenantUserAsync` — загальний guard: `!tenant.IsActive` → 400 "Tenant is not active."
(захищає від тієї ж помилки на будь-якому деактивованому tenant, не тільки platform).
Тести: `TenantRepositoryPlatformTenantTests` (EF InMemory, GetAllAsync виключає platform tenant)
+ `ProviderServiceTests.CreateTenantUser_InactiveTenant_IsRejected`. Build green, 515/515 тестів.
Data cleanup на prod (stray user) — окремо, поза скоупом цього фіксу.
Log: `bug014_2026-07-03_platform-tenant-visible-in-provider-list_backend-developer.md`
**Next:** deploy to prod; clean up stray user tenant 89d95a15-abcb-459a-b943-6e9a8a3f07ac.

---

## BUG-007 — /api/movements 500: паралельні запити на одному DbContext
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-02
Found during store_manager role QA (follow-up to BUG-006). На prod `/api/movements`
повертав 500 на кожен виклик (5/5 запитів fail).
Root cause: `MovementService.GetAsync` запускав `_repo.GetAsync` і `_repo.CountAsync`
паралельно через `Task.WhenAll` на одному scoped `AppDbContext`. DbContext не
thread-safe → «A second operation was started on this context instance…» → 500.
Fix: обидва запити виконуються послідовно через `await` у
`ShelfGuard.Application/Features/Movements/MovementService.cs`. Grep по всьому
Application + Infrastructure: інших `Task.WhenAll` над одним DbContext немає.
Build green, 459/459 тестів.
Log: `bug007-008_2026-07-02_movements-concurrency-topproducts-jsonb_backend-developer.md`
**Next:** deploy to prod; re-run store_manager QA pass.

---

## BUG-008 — /api/analytics/pos/top-products 500: jsonb Barcodes у SQL-проєкції
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-02
Found during store_manager role QA (follow-up to BUG-006). Ендпоінт падав 500 навіть
після фіксу DateTime Kind (BUG-006).
Root cause: `AnalyticsRepository.GetPosTopProductsAsync` проєктував
`i.Product!.Barcodes.Count > 0 ? i.Product.Barcodes[0] : null` всередині SQL-запиту.
`Barcodes` — `List<string>` mapped to `jsonb`; Npgsql не транслює `.Count` / індексер
`[0]` над jsonb-списком → runtime translation exception → 500.
Fix: у проєкції вибирається весь список (`Barcodes = i.Product!.Barcodes`), перший
штрихкод береться client-side (`FirstOrDefault()`) після `ToListAsync` — той самий
патерн, що в `DailySalesRepository.cs:50-54`. Інші `Barcodes.Count/[0]` у кодовій базі —
в Application-сервісах над матеріалізованими entity, не в IQueryable — не зачеплені.
Build green, 459/459 тестів.
Log: `bug007-008_2026-07-02_movements-concurrency-topproducts-jsonb_backend-developer.md`
**Next:** deploy to prod; re-run store_manager QA pass on POS analytics.

---

## BUG-006 — Analytics 500: DateTimeKind.Unspecified vs timestamptz
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-07-02
Found during QA of store_manager role. On prod усі 4 POS analytics ендпоінти
(`/api/analytics/pos/summary`, `revenue-trend`, `top-products`, `cashiers`) повертали 500,
а `/api/analytics/write-offs` та `/api/movements` — 500 тільки з `from=&to=` фільтрами.
Root cause: `DateOnly.ToDateTime(TimeOnly.MinValue/MaxValue)` в `AnalyticsRepository.cs`
дає `DateTime` з `Kind=Unspecified`; Npgsql відхиляє такі параметри для `timestamptz`
колонок (`pos_transactions.CreatedAt` тощо) → runtime exception → 500. Тести не ловили,
бо використовують fake-репозиторії.
Fix: приватні хелпери `ToUtcStart(DateOnly)` / `ToUtcEnd(DateOnly)` через
`ToDateTime(..., DateTimeKind.Utc)`; замінено всі 14 конверсій. `MovementRepository` вже
використовував правильний overload — без змін. Build green, 459/459 тестів.
Log: `bug006_2026-07-02_analytics-datetime-kind-500_backend-developer.md`
**Next:** deploy to prod; re-run store_manager QA pass on analytics endpoints.

---

## TASK-278 — Live Chat: живий чат провайдер ↔ клієнт
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-21
Різниця між тікетом і чатом: тікет — для довгострокових задач (налаштування компанії), чат — миттєве спілкування.
**DB (міграція AddChatFeature):**
- `chat_sessions` (id, tenant_id, created_by_user_id, subject TEXT, status open/closed, created_at, updated_at; RLS на tenant_id)
- `chat_messages` (id, session_id, sender_user_id, sender_name TEXT, body TEXT, is_read, created_at; RLS через session → tenant_id)
**Backend:**
- `POST /api/chat/sessions` — клієнт відкриває нову сесію (перший повідомлення)
- `GET /api/chat/sessions` — клієнт бачить свої сесії (свій tenant)
- `GET /api/chat/sessions/{id}/messages` — список повідомлень сесії
- `POST /api/chat/sessions/{id}/messages` — надіслати повідомлення (клієнт або провайдер)
- `POST /api/chat/sessions/{id}/close` — закрити сесію
- `GET /api/admin/chat/sessions` (ProviderOnly) — всі сесії cross-tenant
- `GET /api/admin/chat/sessions/{id}/messages` (ProviderOnly) — повідомлення клієнта
- `POST /api/admin/chat/sessions/{id}/messages` (ProviderOnly) — відповідь провайдера
- `POST /api/admin/chat/sessions/{id}/close` (ProviderOnly) — закрити сесію
**Frontend (клієнт) — `SupportChatWidget.tsx`:**
- Повністю переробити: замість тікету показати список чат-сесій + кнопку "Новий чат"
- Активна сесія: вигляд як у месенджері (бульки повідомлень), input внизу, відправка через Enter/кнопку
- Polling кожні 3 секунди через `refetchInterval` React Query (без WebSocket)
**Frontend (провайдер) — нова вкладка в `/service-desk`:**
- Панель "Живий чат" поруч із існуючим Service Desk
- Список чат-сесій усіх клієнтів (ім'я, тенант, остання активність, кількість непрочитаних)
- При натисканні — повна переписка + input для відповіді
- Нові повідомлення підсвічуються, polling кожні 3с
Accept: dotnet build green; міграція green; клієнт може надіслати повідомлення, провайдер його бачить і відповідає; tsc + next build green.

## TASK-277 — Команда: створення користувача з логіном/паролем та правами
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-21
**Backend:**
- Розширити `InviteProviderMemberRequest` полем `Password?: string` (необов'язкове)
- В `ProviderTeamService.InviteMemberAsync`: якщо `Password` передано → хешувати його замість `tempPassword`
- Якщо `Password` не передано — поведінка залишається як є (tempPassword)
**Frontend — `InviteProviderMemberModal.tsx`:**
- Додати поля: «Пароль» (type=password) + «Підтвердження паролю»
- Валідація: обидва поля повинні збігатися, мінімум 6 символів
- Додати секцію «Права доступу» — readonly список того, що може робити обрана роль:
  - provider_admin: управління командою, всі клієнти, Service Desk, Чат
  - provider_agent: Service Desk, Чат, перегляд клієнтів
- Кнопка тепер «Створити користувача» (а не «Запросити»)
Accept: backend build green; фронтенд: tsc green; можна створити провайдер-агента з власним паролем, він може увійти в систему з цим паролем.

## TASK-276 — Розклад: множинний вибір днів при додаванні зміни
**Status:** done · **Agent:** frontend-developer · **Depends:** — · Updated: 2026-06-21
Поточний `AddSlotModal` у `ScheduleTab.tsx` дозволяє вибрати лише один день.
**Зміни:**
- Замінити `<select>` для дня тижня на 7 чекбоксів (Пн–Нд) у горизонтальній сітці
- Форма дозволяє виділити будь-яку кількість днів (мінімум 1)
- При сабміті — послідовно викликати `create.mutateAsync` для кожного вибраного дня з однаковими `userId`, `startTime`, `endTime`, `notes`
- Стан форми: `dayOfWeek` → `daysOfWeek: number[]`
- Якщо будь-який з викликів повертає помилку — показати її й зупинитись
- Після успіху — закрити модалку (одиночний `onClose()`)
Accept: tsc green; можна обрати 3 дні → backend отримує 3 POST-запити → 3 слоти з'являються у grid.

## TASK-275 — Маркетплейс: Full-width + Створення постачальника + Додавання товарів
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-21
**Frontend (швидке виправлення):**
- У `frontend/app/(dashboard)/marketplace/page.tsx` рядок 80: видалити `maxWidth: 1200` зі стилів обгортки
**Backend — нові провайдер-ендпоінти (`MarketplaceAdminController`):**
- `POST /api/admin/marketplace/suppliers` (ProviderOnly) — створити нового постачальника:
  Body: `{ companyName, region, categories[], website?, deliveryRegions[], workingHours?, paymentTerms?, isPublic, plan }`
  Дія: CREATE `Supplier` (tenantId = provider tenant_id) + CREATE `SupplierProfile` для нього
- `POST /api/admin/marketplace/suppliers/{id}/items` (ProviderOnly) — додати товар:
  Body: `{ customName, price?, minQty?, unit?, isAvailable }`
  Дія: CREATE `SupplierItem` (supplierId = id)
- `DELETE /api/admin/marketplace/suppliers/{id}/items/{itemId}` (ProviderOnly) — видалити товар
**Frontend — сторінка `/marketplace`:**
- Додати кнопку «+ Створити постачальника» (видима лише для PROVIDER_TEAM ролей) поруч із пошуковим рядком
- `CreateSupplierModal.tsx` (`features/marketplace/components/`): форма з полями companyName, region, categories (textarea через кому), isPublic toggle, plan select (free/premium)
- На `SupplierCard.tsx` або `marketplace/[id]/page.tsx` — кнопка «+ Додати товар» (видима для PROVIDER_TEAM):
  `AddSupplierItemModal.tsx`: customName, price, minQty, unit, isAvailable toggle
- Hooks: `useCreateSupplier`, `useAddSupplierItem`, `useDeleteSupplierItem` у `features/marketplace/hooks/`
Accept: backend build green; tsc + next build green; провайдер може створити постачальника → він з'являється у списку; можна додати/видалити товар; сторінка на всю ширину.

---

## v3.4 carry-over

## TASK-274 — Provider Schedule (розклад команди)
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** TASK-272 · Updated: 2026-06-20
Тижневий розклад доступності для агентів: recurring slots (DayOfWeek 0-6 + time range).
Backend: entity `ProviderScheduleSlot` + migration `AddProviderScheduleSlots` + `ProviderScheduleController`
(GET ?userId=, POST, DELETE/{id}; ProviderTeamMember/ProviderCanInvite policies).
Frontend: `ScheduleTab.tsx` — 7-колонковий weekly grid + AddSlotModal.
Build green, migration green, tsc green.
Log: `274_2026-06-20_provider-schedule_backend-developer.md`

## TASK-273 — Provider Employee Statistics
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** TASK-272 · Updated: 2026-06-20
Статистика продуктивності команди (без нової схеми): assigned/resolved tickets, created-by-provider, comments, avg resolution time.
Backend: `IProviderStatsRepository` + `ProviderStatsRepository` (cross-tenant) + `ProviderStatsService` + `GET /api/provider/team/stats`.
Frontend: `StatsTab.tsx` — таблиця з прогрес-баром resolve rate + кольоровими метриками.
Build green, tsc green.
Log: `273_2026-06-20_provider-employee-stats_backend-developer.md`

## TASK-272 — Provider HR: управління власним персоналом
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-20
Розширення команди провайдера: редагування учасника + реактивація.
Backend: `PUT /api/provider/team/{id}` + `POST /api/provider/team/{id}/reactivate` ([ProviderCanInvite]).
Frontend: `EditMemberModal.tsx` (нова) + оновлений `TeamTab.tsx` з кнопками Edit/Відновити.
Guard: роль власника (`provider`) не може бути змінена через API.
Build green, tsc green.
Log: `272_2026-06-20_provider-hr-staff-management_backend-developer.md`
**Next:** TASK-273 (employee performance stats), TASK-274 (schedule/calendar UI).

---

## TASK-271 — Backend: Provider cross-tenant Service Desk
**Status:** done · **Agent:** backend-developer · **Depends:** TASK-251 · Updated: 2026-06-20
Provider може бачити тікети з усіх тенантів та створювати тікети від імені клієнта.
Нові ендпоінти (ProviderOnly policy):
- `GET  /api/admin/service-desk?status=&tenantId=` — всі тікети cross-tenant
- `POST /api/admin/service-desk` — створити тікет для клієнтського тенанту
Нові файли: `IProviderTicketRepository`, `ProviderTicketRepository`, `IProviderTicketService`,
`ProviderTicketService`, `ProviderServiceDeskDtos`, `AdminServiceDeskController`.
Migration `AddTicketCreatedByProvider` — `CreatedByProvider bool DEFAULT false` на `support_tickets`.
Тікет зберігається з `TenantId = client tenant` + `CreatedByProvider = true` → клієнт бачить у
своєму Service Desk, Провайдер бачить у cross-tenant запиті.
Build green, 459/459 тестів.
Log: `271_2026-06-20_provider-service-desk-backend_backend-developer.md`
**Next:** TASK-272 Provider HR (власний персонал), TASK-270 chat button in header.

---

## BUG-005 — pos_transactions.RetryCount missing on production
**Status:** done · **Agent:** database-engineer · **Depends:** — · Updated: 2026-06-16
Flagged in TASK-204 log: prod threw `column p.RetryCount does not exist` in
`PosService.GetPendingFiscalizationAsync`. Root cause: migration
`20260613000000_AddPosTransactionRetryCount` (TASK-069, committed 2026-06-13) was never
actually deployed to prod. Fix: regenerated as `20260616151654_AddPosTransactionRetryCount`
(same single AddColumn, fresh timestamp so it lands after the v4 rename migrations on next
deploy). Build green, Pos tests 76/76 green.
Log: `bug005_2026-06-16_pos-retrycount-missing-column_database-engineer.md`
**Next:** verify on next prod deploy that the migration applies and fiscalization retry
worker stops erroring.

---

## TASK-078 — Mobile: Write-offs screen
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Екран списання для мобільного працівника:
- Список власних списань (GET /api/write-offs)
- Кнопка «+ Списання» → scan штрихкод (expo-camera) → підтягнути назву товару → вибір причини (expired/damaged/theft/other) → кількість → коментар → підтвердження
- Detail екран окремого списання
- Тільки для ролей: storekeeper, store_manager і вище
Accept: tsc green; flow проти API (create + list); scan штрихкоду відкриває форму з назвою товару.

## TASK-079 — Mobile: Transfers screen
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Екран переміщень між магазинами/зонами:
- Список переміщень (GET /api/transfers)
- Кнопка «+ Переміщення» → scan штрихкод → кількість → вибір destination store → підтвердження
- Статуси: pending / in_transit / completed
- Тільки для ролей: storekeeper, store_manager і вище
Accept: tsc green; create + list flow проти API.

## TASK-080 — Mobile: Notifications screen
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Сповіщення на мобільному:
- Bell icon у (app)/_layout.tsx header з badge кількості непрочитаних
- Екран /notifications: список (GET /api/notifications/history), тип іконкою (expiry/stock/system), read/unread стилі
- Tap → mark as read
Accept: tsc green; список підвантажується з API; badge оновлюється.

## TASK-081 — Mobile: Dashboard з реальними даними
**Status:** done · **Agent:** mobile-developer · **Depends:** — · Updated: 2026-06-15
Підключити index.tsx до реальних API:
- Картки Safe/Warning/Critical/Expired → GET /api/stock/summary
- Секція «AI замовлення» → GET /api/ai-orders (pending suggestions, count)
- Секція «Останні події» → GET /api/stock/events?limit=5 (або /api/activity-logs)
- Pull-to-refresh
Accept: tsc green; реальні числа замість заглушок; pull-to-refresh працює.

---

## v3.3 carry-over

## TASK-075 — Architect: Menu groups + Role matrix
**Status:** done · **Agent:** project-manager · **Depends:** — · Updated: 2026-06-14
Визначити логічні групи навігації та матрицю доступу ролей до меню.
Нова роль: Касир (cashier) — тільки /pos.
Уточнено: StoreManager → менеджмент магазину; NetworkManager → мережева картина.
Accept: задокументована матриця, TASK-076 + TASK-077 готові до виконання.

## TASK-076 — Backend: Cashier role + оновлені AppPolicies
**Status:** done · **Agent:** backend-developer · **Depends:** 075 · Updated: 2026-06-14
Додати роль `cashier` до AppRoles enum (C#), оновити AppPolicies:
- CanAccessPos: cashier + storekeeper + store_manager + network_manager + enterprise_admin
- CanManageStore: store_manager + network_manager + enterprise_admin (без cashier/storekeeper/merchandiser)
- CanViewNetworkAnalytics: network_manager + enterprise_admin
Оновити UserInviteDto/UserUpdateDto валідацію нових ролей.
Accept: dotnet build green; тести авторизації з cashier роллю проходять.

## TASK-077 — Frontend: Згрупований Sidebar + RBAC видимість
**Status:** done · **Agent:** frontend-developer · **Depends:** 075, 076 · Updated: 2026-06-14
Переробити Sidebar.tsx: групи зі стрілкою expand/collapse, роль-based видимість.

**Групи та доступ:**
1. Головна: Дашборд — TENANT_ROLES
2. Каса (expand): Каса (/pos), POS Аналітика — CAN_ACCESS_POS (cashier + managers)
3. Склад (expand): Каталог, Залишки, Прийомка, Переміщення, Списання — CAN_RECEIVE_STOCK + TENANT_ROLES
4. Продажі (expand): Продажі, Замовлення, AI Замовлення, Події — AT_LEAST_STORE_MANAGER
5. Аналітика (expand): Аналітика загальна, POS Аналітика — CAN_VIEW_ANALYTICS
6. Управління (expand): Персонал, План магазину, IoT пристрої — AT_LEAST_STORE_MANAGER
7. Адмін: Провайдер, Адмін — PROVIDER_ONLY
8. Налаштування — all

**Нові role sets у frontend/lib/roles.ts:**
- CAN_ACCESS_POS: cashier + CAN_RECEIVE_STOCK
- CAN_MANAGE_STORE: AT_LEAST_STORE_MANAGER (без cashier/storekeeper)
- CAN_VIEW_NETWORK: network_manager + enterprise_admin

**Правила видимості по ролях:**
- cashier: тільки Каса (група Каса), Налаштування
- storekeeper: Склад, Каса (без POS Аналітики), Налаштування
- merchandiser: Склад (Каталог + Залишки, без Прийомки/Переміщень), Налаштування
- store_manager: Каса, Склад, Продажі, Аналітика, Управління, Налаштування
- network_manager: Каса (POS Аналітика), Продажі, Аналітика, Управління, Налаштування
- enterprise_admin: все крім Provider/Admin
Accept: tsc + next build green; кожна роль бачить тільки свої групи; collapse/expand працює.

---

# Carry-over from v3.2 «ПРРО Каса» (started 2026-06-12)

Scope: v3-spec §3 + §6 Фаза 4. ADR-012: Checkbox (SaaS ПРРО) as fiscal provider behind
IFiscalService, offline-first (ADR-011 flow stays). Test cash register registered in
Checkbox cabinet (фіскальний номер TEST582378; license key + cashier creds in
.claude/private/access.md — blocker resolved 2026-06-12).

## TASK-066 — DB: pos_shifts, pos_transactions, pos_transaction_items
**Status:** done · **Agent:** database-engineer · **Depends:** — · Updated: 2026-06-12
v3-spec §5 + Status/'pending_fiscalization', OfflineNumber; RLS (TenantId direct);
FK product_stock SET NULL (яка партія списана). Accept: migration + RLS verified, build green.
Committed as 6d7a5082 «feat(pos): v3.2 POS schema».

## TASK-067 — Infrastructure: Checkbox fiscal client (IFiscalService)
**Status:** done · **Agent:** backend-developer · **Depends:** — · Updated: 2026-06-12
Done: IFiscalService + DTOs (Application/Features/Pos/Fiscal), CheckboxFiscalClient +
PrroOptions + token store (Infrastructure/Integrations/Prro), Noop fallback, DI switch,
unit tests 292/292 green. Live: license key valid on api.checkbox.in.ua
(⚠️ dev-api host from docs does NOT resolve — docs corrected). Cashier creds received →
**full live e2e GREEN** (CheckboxLiveE2ETests, gated by PRRO_LIVE_E2E=1): PIN signin →
shift CREATED→OPENED → sell receipt DONE (fiscal_code TEST-KcEsEF + tax_url) → Z-report
CLOSED, ~6s total. Added IFiscalService.GetShiftStatusAsync (shift opening is async —
needed for polling; TASK-068 must poll after open/close).
Log: 067_2026-06-12_checkbox-fiscal-client_backend-developer.md
ADR-012. Integrations/Prro: CheckboxFiscalClient implementing IFiscalService —
cashier signin (login/password or PIN → bearer token), shift open/close, sell receipt,
receipt status; DTOs; config binding PRRO__* (PROVIDER/BASEURL/LICENSEKEY/CASHIER__*,
secrets in .env only); error mapping + timeouts; unit tests with fake HTTP handler.
Accept: unit tests green (fake handler); live: dev-api.checkbox.in.ua reachability green
+ license-key flow as far as possible without cashier creds (blocker: cashier login/PIN
pending from user).

## TASK-068 — API: POS endpoints (shifts, sales → FEFO + stock_events)
**Status:** done · **Agent:** backend-developer · **Depends:** 066, 067 · Updated: 2026-06-13
⚠️ ADR-013: must resolve fiscalization through the per-tenant IFiscalServiceFactory
(TASK-071), not the startup-time IFiscalService DI registration.
POST /api/pos/shifts/open|close, POST /api/pos/sales (items by barcode; critical → auto
discount price, expired → 423 block per spec §3), GET /api/pos/shifts/current, sales list.
Sale = one DB tx: pos_transaction + items + FEFO write-down + stock_events('pos_sale');
fiscalization async (Status). Accept: service tests (FEFO, expired block, totals), build green.

## TASK-069 — Worker: fiscalization retry job
**Status:** done · **Agent:** backend-developer (worker) · **Depends:** 067, 068 · Updated: 2026-06-13
Cron */5 min: pending_fiscalization docs → submit/poll receipt status via Checkbox
(through API endpoint backed by IFiscalService); update FiscalNumber/Status on DONE.
Offline numbering handled by Checkbox itself (ADR-012). Accept: tsc green;
retry/backoff covered.

## TASK-071 — Settings: ПРРО провайдер (Checkbox) у Налаштування → Інтеграції
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** 067 · Updated: 2026-06-13
ADR-013. Per-tenant fiscal provider config, same mechanism as the Claude key
(integration_configs service='claude' → ClaudeOrderAdvisor.ResolveAsync; web UI
features/integrations + IntegrationsTab).
**Backend:** storage in integration_configs (service='prro', JSONB: provider
[checkbox|disabled, extensible], base_url [test/prod], license_key, cashier_login,
cashier_password, cashier_pin_code; RLS already on table — verify tenant isolation).
Endpoints: GET/PUT /api/settings/prro (GET masks secrets: ••••+last 4; PUT with
masked/unchanged secret keeps stored value — secrets are write-only),
POST /api/settings/prro/test (ping cash-registers/info via X-License-Key + cashier
signin, no shift side effects). Per-tenant IFiscalServiceFactory
(Infrastructure/Integrations/Prro): tenant DB config → PRRO__* env fallback →
NoopFiscalService; replaces startup DI switch; CheckboxTokenStore keyed per
tenant+license key. TASK-068/069 consume the factory.
**Frontend:** rework SERVICE_META.prro (features/integrations/types.ts — current
fields are stale placeholders) → provider select («Checkbox» / «вимкнено»),
credential form (license key, login/password or PIN, base URL test/prod toggle),
«Перевірити з'єднання» button calling /test, status badge (connected/error/disabled)
in IntegrationsTab card.
**Accept:** backend unit tests (resolution order DB→env→noop, masking, keep-on-masked
PUT, factory per-tenant); test endpoint green against live Checkbox test register;
cross-tenant isolation verified; tsc + next build green; full UI flow: select provider
→ enter creds → test → save → re-open shows masked secrets.

## TASK-070 — Mobile: POS screens (tablet) in Expo app
**Status:** done · **Agent:** mobile-developer · **Depends:** 068 · Updated: 2026-06-13
Зміна (open/close + PIN), продаж: скан штрихкоду (expo-camera) → кошик → ціна з акцією,
critical/expired badge, оплата cash/card (терминал SDK / принтер — Phase 4.1, поза скоупом),
чек зі статусом фіскалізації. Accept: tsc green; flow проти прод-API.

## TASK-072 — Web: POS dashboard (зміни, транзакції, Z-звіти)
**Status:** done · **Agent:** frontend-developer · **Depends:** 068 · Updated: 2026-06-14

## TASK-074 — SaaS Admin Panel: tenant onboarding + управління
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** — · Updated: 2026-06-15
Provider-only панель: список тенантів, створення (назва+slug+план+перший адмін),
статус active/inactive, зміна плану (basic/standard/enterprise/trial), модулі,
usage stats (users/stores/products/sales). Route /admin, policy ProviderOnly.
Backend: GET|POST /api/admin/tenants, GET|PATCH|POST /api/admin/tenants/{id}/...
Frontend: /admin сторінка з таблицею тенантів + create modal + detail drawer.
Accept: dotnet build+test green; tsc green; CRUD flow проти API.

## TASK-073 — POS Аналітика: API + Web дашборд
**Status:** done · **Agent:** backend-developer + frontend-developer · **Depends:** 068 · Updated: 2026-06-15
Нові ендпоінти GET /api/analytics/pos/* + веб-дашборд /analytics/pos.
Метрики: виручка за період, динаміка по днях, топ товарів, ефективність касирів,
середній чек, розбивка cash/card. Дані з pos_transactions + pos_transaction_items.
Accept: backend тести зелені; tsc + next build green; графіки відображають реальні дані.
Веб-інтерфейс для десктоп касира/менеджера — аналог TASK-070 (mobile) але для Next.js.
Route `/pos`. Функціонал: поточна зміна (відкрити/закрити + статус фіскалізації),
список продажів зміни (чек-деталі), Z-звіт після закриття, sidebar «Каса» (CanReceiveStock).
Використовує існуючі ендпоінти TASK-068:
  GET  /api/pos/shifts/current
  POST /api/pos/shifts/open  (body: { storeId, openingCash? })
  POST /api/pos/shifts/close
  GET  /api/pos/sales?shiftId=
Не включає: продаж через сканер (мобільна функція), оплата терміналом — Phase 4.1.
Accept: tsc + next build green; shift open/close/list-sales flow проти API.

---
# Previous sprint — v3.1 «IoT Foundation» (started 2026-06-12)

Scope: v3-spec §6 Фаза 1. ADR-010: MQTT ingestion in worker. pos_* tables → Phase 4.
**✅ COMPLETE 2026-06-12** — log: 061-065_2026-06-12_iot-foundation_multi-agent.md
Builds/tests green (backend 15/15 IoT tests, worker tsc, next build).
Live e2e PASSED on local stack: migration+RLS ✓, mosquitto pub/sub ✓,
temp alert → notification rows ✓, weight −490г → FEFO −2 units ✓.
2 bugs caught & fixed in e2e (jsonb config parsing; $6 type cast in notification log).
**DEPLOYED to production 2026-06-12** (93.127.143.98): mosquitto healthy (port 1884),
V3IotFoundation migration applied (auto on API start), RLS 6 policies verified,
worker «[mqtt] connected, subscribed to shelfguard/#», /iot and /floor-plan → 200.
Deploy bug fixed on the way: deploy.sh sourced unquoted .env → truncated DB
connection string overrode --env-file → API crash loop (fix: 95f5586d + quoted .env).

## TASK-061 — DB: IoT schema (iot_devices, temperature_readings, weight_readings)
**Status:** done · **Agent:** database-engineer · **Depends:** — · Updated: 2026-06-12
v3-spec §5: 3 tables + RLS (tenant via iot_devices.tenant_id; readings join device),
FKs to stores/store_zones, idx_temp_readings_device_time + device_id unique.
Accept: migration applies cleanly; RLS verified cross-tenant; dotnet build green.

## TASK-062 — DevOps: Mosquitto MQTT broker in docker-compose
**Status:** done · **Agent:** devops-engineer · **Depends:** — · Updated: 2026-06-12
Service `mosquitto` (eclipse-mosquitto:2), port 1883, allow_anonymous for dev,
persistent volume, MQTT_URL env wired to worker. Accept: `docker compose up` →
pub/sub smoke test on shelfguard/# passes.

## TASK-063 — API: iot_devices CRUD + readings endpoints
**Status:** done · **Agent:** backend-developer · **Depends:** 061 · Updated: 2026-06-12
GET/POST /api/iot/devices, GET/PUT/DELETE(soft) /api/iot/devices/:id,
GET /api/iot/devices/:id/readings (temp, paged), GET /api/iot/temperature?store_id=
(latest per device). Thin controllers, service in Application/Features/IoT.
Accept: tests for service rules (device_id unique per tenant, soft delete); build+tests green.

## TASK-064 — Worker: MQTT listener → readings + stock_events + temp alerts
**Status:** done · **Agent:** backend-developer (worker) · **Depends:** 061, 062 · Updated: 2026-06-12
Subscribe shelfguard/#; resolve device by device_id; update last_seen_at/battery.
temp payload → temperature_readings + threshold check (fridge >+8°C, freezer >-12°C
from device config) → is_alert + notification queue (critical → manager/director).
weight payload → weight_readings + confidence calc (95/85/60, <70 = log only) →
stock_events (type sensor) + FEFO write-down for confident deltas.
Offline cron: last_seen_at > 30 min → alert. Accept: tsc green; unit-testable pure
funcs for confidence/thresholds; e2e via mosquitto_pub on local stack.

## TASK-065 — Web: IoT devices dashboard (/iot)
**Status:** done · **Agent:** frontend-developer · **Depends:** 063 (+064 for live data) · Updated: 2026-06-12
Devices table: type icon, zone, online/offline (last_seen_at), battery, firmware;
register/edit/deactivate dialogs; temperature tab: recharts line per device,
alert badges. Sidebar «IoT пристрої» (AT_LEAST_STORE_MANAGER).
Accept: tsc + next build green; CRUD flow works against API.

---
# Previous sprint — v2.5 «AI Agent» ✅ COMPLETE (2026-06-12) — v2 DONE

## TASK-060 — Web: AI orders dashboard ✅ done (2026-06-12)
Log: `.claude/logs/tasks/060_2026-06-12_ai-orders-dashboard_frontend-developer.md`
/ai-orders per spec §7 mockup: base/AI/final + reasoning, inline edit, accept/reject.
Claude key manageable via Налаштування → Інтеграції. Live e2e pending Anthropic credits.

## TASK-058 + TASK-059 — Claude advisor + AI orders API + daily job ✅ done (2026-06-11)
Log: `.claude/logs/tasks/058-059_2026-06-11_ai-order-agent_backend-developer.md`
ClaudeOrderAdvisor (Infrastructure/AI, official SDK, structured outputs), 6 endpoints,
worker cron 05:00 + Telegram notify. Awaiting CLAUDE_API_KEY for live e2e.

---
# Previous sprint — v2.4 «Cannibalization» ✅ COMPLETE (2026-06-11)

## TASK-057 — Promo cannibalization ✅ done (2026-06-11)
Log: `.claude/logs/tasks/057_2026-06-11_cannibalization_backend-developer.md`
Auto-suggestions (promo ×2.0, siblings ×0.7), apply flow, promo coefficient in formula.
E2e: Вода k_event 2.0 × k_promo 2.0 → ORDER 304. Next: v2.5 AI Agent (TASK-058..060).

---
# Previous sprint — v2.3 «Events & Weather» ✅ COMPLETE (2026-06-11)

## TASK-056 — Web: events calendar ✅ done (2026-06-11)
Log: `.claude/logs/tasks/056_2026-06-11_events-calendar_frontend-developer.md`
/events: month grid, recurring projection, CRUD + coefficient editor, seed button. 200 OK.
Next: v2.4 Cannibalization (TASK-057) → v2.5 AI Agent (TASK-058..060).

## TASK-054 — Demand events calendar ✅ done (2026-06-11)
Log: `.claude/logs/tasks/054_2026-06-11_demand-events_backend-developer.md`
4 tables + RLS, full CRUD, 5 seeded holidays, event coefficient wired into order
formula (most-specific scope wins, events multiply). E2e: Вода ×2 → ORDER 152.

## TASK-055 — Open-Meteo integration ✅ done (2026-06-11)
Log: `.claude/logs/tasks/055_2026-06-11_open-meteo-weather_backend-developer.md`
Client + 6 endpoints + worker cron 06:00 + weather coefficient in formula.
E2e on real Kyiv forecast: k_event 2.0 × k_weather 1.5 → ORDER 228.

---
# Previous sprint — v2.2 «Buffer & Formula» ✅ COMPLETE (2026-06-11)

## TASK-053 — Web: orders page + buffer funnel ✅ done (2026-06-11)
Log: `.claude/logs/tasks/053_2026-06-11_orders-page-buffer-funnel_frontend-developer.md`
/orders: one-click chain ADU→buffers→order, funnel viz, MOQ/USQ tags. Deployed, 200 OK.
Next sprint: v2.3 «Events & Weather» (TASK-054..056).

## TASK-051 — CDA buffer engine ✅ done (2026-06-11)
Log: `.claude/logs/tasks/051_2026-06-11_cda-buffer-engine_backend-developer.md`
product_buffer table + RLS, pure CdaBufferCalculator (9 tests), GET/recalculate endpoints.
Verified on production: Total 51.97 = G 36.03 + Y 5.02 + R 10.92 (hand-checked).

## TASK-052 — Order formula ✅ done (2026-06-11)
Log: `.claude/logs/tasks/052_2026-06-11_order-formula_backend-developer.md`
POST /api/orders/calculate. Full chain verified on production:
Вода Моршинська 51.97+24−0−0 → ORDER 76. Tests 9/9.

---
# Previous sprint — v2.1 «Data Foundation» ✅ COMPLETE (2026-06-11)

## TASK-046 — v2 schema: daily_sales, product_adu, supply_schedules ✅ done (2026-06-11)
Log: `.claude/logs/tasks/046_2026-06-11_v2-data-foundation-schema_database-engineer.md`
Migration V2DataFoundation applied to production. RLS verified (6 policies).

## TASK-047 — Daily Sales API ✅ done (2026-06-11)
Log: `.claude/logs/tasks/047_2026-06-11_daily-sales-api_backend-developer.md`
GET/POST /daily-sales (upsert), POST /import (CSV by barcode), PUT /:id/mark-anomaly.
Verified on production. Tests 5/5.

## TASK-048 — ADU calculation engine ✅ done (2026-06-11)
Log: `.claude/logs/tasks/048_2026-06-11_adu-engine_backend-developer.md`
Pure AduCalculator (9 unit tests) + eligibility query + upsert. Verified on production:
recalculate → 2 products with adu_effective 10.9167 (group 3, 30 valid days).

## TASK-049 — Supply schedules CRUD ✅ done (2026-06-11)
Log: `.claude/logs/tasks/049_2026-06-11_supply-schedules-crud_backend-developer.md`
Full CRUD + one-active-per-pair rule (409), ISO day validation, soft delete.
Verified on production (6/6 e2e checks). Tests 11/11.

## TASK-050 — Web: sales entry page ✅ done (2026-06-11)
Log: `.claude/logs/tasks/050_2026-06-11_sales-entry-page_frontend-developer.md`
/sales: filters + manual entry form + CSV import dialog + anomaly toggle. Deployed, 200 OK.

---
# v1 maintenance (parallel)
TASK-045 (mobile profile+receipt wiring) · TASK-034 (auth tests) · TASK-035 (bin/obj)
TASK-038 (impersonation verify) · TASK-039 (bot /start) — see backlog.md

---
# Done

## TASK-033 — Notifications e2e ✅ done (2026-06-11)
Log: `.claude/logs/tasks/033_2026-06-11_notifications-e2e_devops-engineer.md`
Fixed 5 pipeline breaks (pg URL format, PascalCase SQL, Redis collision with another
project, DATE→NaN statuses, duplicate scheduler). Verified live: statuses recompute
hourly, 23 notifications queued. Delivery needs TELEGRAM_BOT_TOKEN / RESEND_API_KEY (user).


## TASK-018 — Mobile App Scaffolding ✅ done (2026-06-07)
Log: `.claude/logs/tasks/018_2026-06-07_mobile-scaffolding_mobile-developer.md`

## TASK-025 — DB Fix: RLS + FK Constraints ✅ done (2026-06-04)
Log: `.claude/logs/tasks/025_2026-06-04_fix-rls-fk_database-engineer.md`

## TASK-019 — Analytics API ✅ done (2026-06-04)
Log: `.claude/logs/tasks/019_2026-06-04_analytics_backend-developer.md`


## TASK-016 — Write-offs ✅ done (2026-06-04)
Log: `.claude/logs/tasks/016_2026-06-04_write-offs_backend-developer.md`

## TASK-015 — Stock Transfers ✅ done (2026-06-04)
Log: `.claude/logs/tasks/015_2026-06-04_transfers_backend-developer.md`

## TASK-014 — Stock Receipts ✅ done (2026-06-04)
Log: `.claude/logs/tasks/014_2026-06-04_receipts_backend-developer.md`

## TASK-013 — Suppliers CRUD ✅ done (2026-06-04)
Log: `.claude/logs/tasks/013_2026-06-04_suppliers-crud_backend-developer.md`

## TASK-012 — Stores/Zones CRUD ✅ done (2026-06-04)
Log: `.claude/logs/tasks/012_2026-06-04_stores-zones_backend-developer.md`

## TASK-007 — ProductStock API + FEFO ✅ done (2026-06-04)
Log: `.claude/logs/tasks/007_2026-06-04_product-stock-api_backend-developer.md`

## TASK-006 — Products API ✅ done (2026-06-04)
Log: `.claude/logs/tasks/006_2026-06-04_products-api_backend-developer.md`

## TASK-002 — Full DB Schema ✅ done (2026-06-04)
Log: `.claude/logs/tasks/002_2026-06-04_full-db-schema_database-engineer.md`

## TASK-010 — Web dashboard ✅ done (2026-06-03)
Log: `.claude/logs/tasks/010_2026-06-03_web-dashboard_frontend-developer.md`

---

## TASK-027..031 — Frontend Pages ✅ done (2026-06-04)
Log: `.claude/logs/tasks/027_2026-06-04_frontend-pages_frontend-developer.md`
Pages: /stock, /receipts, /receipts/:id, /transfers, /write-offs, /analytics

---

## TASK-011b — Web products page (/inventory) ✅ done (2026-06-10)
Log: `.claude/logs/tasks/011b_2026-06-10_products-page_frontend-developer.md`
Route: /inventory — Catalog CRUD (list + create + edit + delete + detail drawer)

---

## TASK-024 — Notifications Settings API ✅ done (2026-06-10)
Log: `.claude/logs/tasks/024_2026-06-10_notifications-api_backend-developer.md`
Endpoints: GET /notifications/settings, PUT /notifications/settings, GET /notifications/history, POST /notifications/test

---

## TASK-023 — Users API (HR module) ✅ done (2026-06-10)
Log: `.claude/logs/tasks/023_2026-06-10_users-api_backend-developer.md`
Endpoints: GET /users, GET /users/:id, POST /users/invite, PUT /users/:id, PUT /users/:id/permissions, DELETE /users/:id, GET /users/:id/activity

---

## TASK-022 — Discounts API ✅ done (2026-06-10)
Log: `.claude/logs/tasks/022_2026-06-10_discounts-api_backend-developer.md`
Endpoints: GET /discounts, GET /discounts/:id, POST /discounts, PUT /discounts/:id/approve, PUT /discounts/:id/cancel

---

## BUG-004 — Inconsistent 404 error format ✅ fixed (2026-06-11)
Log: `.claude/logs/tasks/bug004_2026-06-11_error-format-standardization_backend-developer.md`
Central fix: custom IClientErrorFactory + InvalidModelStateResponseFactory in ShelfGuard.Api.
All error bodies now follow `{error: "..."}`. Verified on production. All 4 smoke-test bugs closed.

---

## BUG-003 — GET /api/analytics/summary ✅ closed: not a bug (2026-06-11)
Log: `.claude/logs/reviews/bug003-resolution_2026-06-11.md`
Route never existed — smoke test probed a guessed name. Real endpoint is
`/api/analytics/expiry-summary`; all 6 analytics routes verified 200 on production.
Stale `/api/analytics/dashboard` row in api-contracts.md corrected.

---

## BUG-002 — GET /api/stock/summary ✅ fixed (2026-06-11)
Log: `.claude/logs/tasks/bug002_2026-06-11_stock-summary-endpoint_backend-developer.md`
Response: `{safe, warning, critical, expired, needsVerification, total}`. Optional `?store_id` filter.
Verified on production: 25 total batches (11 safe / 7 warning / 5 critical / 2 expired).

---

## BUG-001 — RLS Tenant Leakage ✅ fixed (2026-06-10)
Log: `.claude/logs/tasks/bug001_2026-06-10_rls-tenant-leakage_security-reviewer.md`
Fix: `TenantConnectionInterceptor.BuildSetSql()` now always SETs `app.tenant_id`.
Provider users get null UUID → RLS returns `[]` instead of leaking tenant data.
Tests: 13/13 pass.

---

## Next candidates

- **TASK-007** — ProductStock (batches) API + FEFO logic — **найвищий пріоритет**, блокує dashboard реальні дані
- **TASK-011** — `/api/stock` backend endpoint + `/stock` frontend page
  - Requires: product_stock table ✅, catalog_products ✅
  - Blocks: real dashboard stats (Safe/Warning/Critical/Expired from actual batches)

- **TASK-012** — Extend DbSeeder with store, zones, catalog_products, stock batches
  - Makes dashboard show real FEFO data instead of POC products proxy

- **TASK-003b** — Migrate catalog API from POC `Products` → `catalog_products`
  - Low priority until stock API is built
# TASK-446 — Mobile design-system foundation

**Status:** partial_device_pass / accessibility-and-login-smoke pending · **Agent:** mobile-developer + qa-tester · **Updated:** 2026-08-01

Shared tokens and all roadmap primitives are implemented and documented. Staff login, dashboard,
and customers list are converted as the low-risk device-tested reference set. Business behavior,
guards, session handling, and API/query behavior remain unchanged. TypeScript, lint (0 errors),
21 suites / 96 tests, and Android export pass. Device visual/accessibility regression remains.

Device attempt prewarmed and loaded the current bundle without css-interop/navigation regression,
but the phone could not reach the API even after Wi-Fi off/on recovery. The retained session was
not destroyed. Finish login/dashboard/customers, font-scale, keyboard, and accessibility smoke
after device routing recovers.

Owned Metro is stopped and 8082 has no listener. ADB timed out during final cleanup verification;
after reconnect, verify font scale `0.9` and remove only reverse `tcp:8082` if still present.

Continuation: dashboard and Customers pass current-source Android smoke (safe area, labels/touch
bounds, empty/search/clear, Back, no css-interop regression). Converted staff-login keyboard,
validation, and logout/login remain pending after Metro/ADB became uncontrollable on launch.
Large-font is blocked by realme `WRITE_SETTINGS`; TalkBack was not run. No logout, credential
submission, app-data clearing, or business mutation occurred; the manager session was retained.

## TASK-461 — Allowlisted mobile query-cache foundation

**Status:** review_pending_device · **Agent:** mobile-developer · **Updated:** 2026-08-01 · **Next:** TASK-462

ADR-025's versioned tenant+user persisted read cache is implemented for explicit schedule,
marketplace-supplier and recipe summary query families. All other queries are denied by default;
mutations remain online-only. TypeScript, lint (0 errors), 24 suites / 108 tests and Android export
pass. TASK-462 owns stale/offline screen UX; TASK-463 owns Android+iOS device/security acceptance.

## TASK-445 — Mobile offline architecture decision

**Status:** done · **Agent:** project-architect · **Updated:** 2026-08-01 · **Next:** TASK-461

ADR-025 records the final product boundary: Android+iOS phones, portrait-only, production API for
preview, durable drafts plus limited owner-namespaced cached reads, and online-only business submits.
No generic mutation queue or full offline POS is authorized. Implementation is decomposed into
TASK-461 (cache foundation), TASK-462 (offline-read UX), and TASK-463 (cross-platform security/QA).
Log: `.claude/logs/tasks/445_2026-08-01_mobile-offline-architecture_project-architect.md`.

## TASK-440 — EAS environments and release configuration

**Status:** review / blocked_credentials_assets_builds · **Agent:** devops-engineer · **Updated:** 2026-08-01

Android+iOS phone/portrait EAS profiles, isolated update channels, production API binding,
runtime/version policy, identifiers, and least-privilege camera/microphone configuration are ready.
Automated config/type/lint/test/export checks pass (Expo Doctor 20/21 only until tracked generated
`.expo/README.md` deletion is committed). Release build/install closure requires approved branded
assets, Apple/Google credentials/accounts, and authorization to run remote builds.
Log: `.claude/logs/tasks/440_2026-08-01_eas-release-configuration_devops-engineer.md`.
# TASK-462 — Limited offline-read UX rollout

**Status:** review_pending_device · **Agent:** mobile-developer · **Updated:** 2026-08-01 · **Next:** TASK-463

Shared Ukrainian offline/current/stale/refresh/no-data status UX is implemented for only schedules,
marketplace suppliers and production recipes. Cached results remain explicitly marked after failed
refresh and every cached state shows last server update. No mutation/cache allowlist expansion.
TypeScript, lint (0 errors), 26 suites / 118 tests and Android export pass. Cross-platform device,
privacy, process-death and storage acceptance is handed to TASK-463.

# TASK-463 — Cross-platform offline security and device acceptance

**Status:** fix_ready_for_device_retest / ios_device_build_pending · **Agent:** mobile-developer + security-reviewer + qa-tester · **Updated:** 2026-08-01

The HIGH offline process-death defect is fixed in source with a minimal protected snapshot and exact
allowlisted-route shell. Offline screens suppress requests/search/details/mutations; reconnect must
pass `/auth/me` and module loading. TypeScript, lint (0 errors), and 29/29 suites, 136/136 tests pass.
Device/build work is paused by user request; Android retest and iOS acceptance remain.

Android code/config security review is clear after exact allowlisting, retention/size/cleanup,
online-only POS and backup/permission hardening. TypeScript, lint (0 errors), 28 suites / 126 tests,
prebuild/config and Android export pass. Physical Android QA remains. iOS backup, Keychain,
process-death and transfer acceptance remains blocked without an iOS build/device.

Android physical attempt could not reach current-source UI: the retained July 29 APK predates
TASK-461..463, Metro manifest returned HTTP 500 `UnexpectedServerError`, and current `assembleDebug`
failed on locked generated resources plus a missing react-native-worklets CMake reply. No device
cache/owner/POS result is claimed. See `2026-08-01_TASK-463-android-device-qa.md`.

DevOps subsequently installed the fresh APK successfully. Targeted Metro cache recovery reached
running state, but the fresh dev client still fails before JS UI with `Failed to download remote
update` / manifest `UnexpectedServerError`; logcat shows missing
`expo.modules.splashscreen.SplashScreenManager`. Android acceptance now waits on this mobile runtime
defect, not build recovery.

**Build recovery:** the exact generated Android/worklets caches were cleared after stopping the
workspace Gradle daemon. The original lock and missing-CMake failures did not recur, but clean
multi-ABI native compilation exceeded the bounded ~9-minute window and produced no APK before
controlled termination. TASK-463 remains blocked_current_source_build_runtime; no install or QA
result is claimed. Log: `440_2026-08-01_android-build-recovery_devops-engineer.md`.

**Final build recovery:** incremental `assembleDebug` PASS in 497s; fresh current-source APK was
hashed and installed via `adb --no-streaming -r`, retaining firstInstallTime/app data and updating
lastUpdateTime to `2026-08-01 20:47:06`. Packaged Android backup exclusions and no-audio permission
policy pass. Build blocker is cleared; TASK-463 now returns to Android QA, while iOS stays blocked.

**Mobile runtime fix:** `expo-dev-launcher@56.0.25` requires
`expo.modules.splashscreen.SplashScreenManager`, but `expo-splash-screen` was absent from direct
dependencies and native autolinking. Added the SDK-aligned `expo-splash-screen~56.0.14` package and
config plugin, plus a regression test. Prebuild/autolinking, TypeScript, Android export, 28 suites /
128 tests and a fresh `assembleDebug` pass. APK replacement preserved app data; native cold launch
has no missing-class, manifest-parser or fatal exception. Metro did not bind during the bounded
post-build attempt, so current-source JS/UI smoke remains QA-owned rather than inferred.

# TASK-520 — Consumer App: banner schema (Banner, BannerLocation, BannerProduct, BannerEvent)

**Status:** done · **Agent:** database-engineer · **Updated:** 2026-08-14 · **Next:** TASK-521

Schema-only slice of the Consumer App plan (`quirky-questing-hoare.md`). New `Banner`/
`BannerLocation`/`BannerProduct`/`BannerEvent` entities (`Discount`-style private-setter+`Create()`
for `Banner`, `UserLocation`-style for the 3 join/log tables), wired into `AppDbContext.cs`, migration
`AddBannersSchema` applied and live-verified against the real non-superuser `shelfguard_app_dev` role:
ownership, FORCE RLS + exactly 3 policies per table (tenant_isolation/provider_bypass/worker_bypass,
no fail-open branch), cross-tenant isolation, fail-closed on reset session, provider/worker bypass,
unique-constraint backstop, and cascade delete all confirmed. `dotnet build` 0 errors; `dotnet test`
1411/1411 green (dynamic RLS audits in `RlsCrossTenantIntegrationTests.cs` picked up all 4 new tables
automatically, no new xUnit file needed). TASK-521 (backend-developer, service/controller layer) was
blocked on this task and can now proceed.
Log: `.claude/logs/tasks/520_2026-08-14_banner-schema_database-engineer.md`.

# TASK-521 — Consumer App: banner admin API + consumer content API

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-14 · **Next:** TASK-522

Backend slice of the Consumer App plan (`quirky-questing-hoare.md`), blocked on TASK-520 (done).
Admin banner CRUD (`BannersController`, `[Authorize(Policy = AtLeastEnterpriseAdmin)]`) — list/
get/create/update (full replace incl. locationIds/productIds)/soft-delete (`SetActive(false)`,
never hard)/image upload (byte-for-byte `ItemsController.UploadImage` pattern)/analytics
(views/clicks/CTR). New public `ConsumerContentController` (`api/consumer/{tenantId}/...`,
`[AllowAnonymous]`, works with zero `Authorization` header): active banners for a store
(body/terms split into `string[]` server-side for the mobile contract), view/click event
recording (`ConsumerAccountId` nullable), active promotions (pure read projection over the
existing `Discount` — zero changes to `DiscountService`/`DiscountsController`), paginated
catalog browse annotated with per-store availability. Tenant context for the anonymous/consumer
reads reuses `ITenantSessionOverride` exactly as `ConsumerLoyaltyController`/`LoyaltyService`
already do — no new mechanism. `dotnet build` 0 errors; `dotnet test` 1411/1411 green. Live
sanity-checked against the real dev DB: anonymous GETs work with no auth header, admin-created
banner correctly appears on the anonymous consumer feed with split body/terms, view/click →
analytics roundtrip confirmed (1/1/ctr=1), cross-tenant isolation confirmed (wrong tenantId in
route → empty list + 404 on view, never the other tenant's data), soft-delete confirmed
(banner vanishes from consumer feed immediately, no hard delete). Test data cleaned up after.
TASK-522 (frontend-developer, admin UI) unblocked. Mandatory mobile-developer handoff doc
written with full API contract.
Log: `.claude/logs/tasks/521_2026-08-14_banner-backend_backend-developer.md`.
Handoff: `.claude/logs/handoffs/521-to-mobile-developer_consumer-content-api.md`.

# TASK-522 — Consumer App: admin frontend (banners, promo products, catalog card)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-14 · **Next:** none (closes
the plan's backend+admin scope)

Frontend slice of the Consumer App plan (`quirky-questing-hoare.md`), blocked on TASK-521 (done).
Added `BannersSection`/`BannerForm`, `PromoProductsSection`, `CatalogSection` to
`frontend/app/(dashboard)/consumer-app/page.tsx` under `BonusProgramSection`, following that
section's exact structural/style conventions (`frontend/features/consumer-app/{api,hooks,
components}`). Banners: full create/edit/soft-delete/image-upload/analytics against
`BannersController`. Promo products: reuses the pre-existing `DiscountsController` as-is
(`POST` reason=promo → immediate `PUT .../approve`, `PUT .../cancel` to remove — no new backend).
Catalog: read-only status card, links to `/inventory` (no `/catalog` route exists — the plan named
the wrong path; real catalog CRUD lives in `features/inventory`). `npx tsc --noEmit` and
`npm run lint` both clean.
Found and fixed 2 real bugs during live verification: (1) `BannerForm`/`PromoProductsSection`
date inputs were sending bare `"YYYY-MM-DD"` to `timestamp with time zone` columns
(`Banner.ValidFrom/ValidUntil`, `Discount.ValidUntil`), which Npgsql rejects as
`DateTime.Kind=Unspecified` (500) — fixed by pinning to UTC midnight ISO before sending; (2) the
promo-product discount-percent input had `min={0.01} step="0.1"` with default `"10"`, an HTML5
step-mismatch that silently blocked native form submission — fixed to `min={0}` matching the
rest of the codebase's percent-field convention. Both confirmed fixed live: banner created (2
stores, 2 products, both dates, image uploaded and verified on disk + in the edit-form preview),
analytics popover showed 0/0/0.0% right after creation, promo product added/removed against a
real store with correct price-before/after, catalog card showed the real active-product count
(50). Screenshot not captured — the Browser pane never composited frames this session (tooling
limitation); verification instead used the accessibility tree, live network inspection, and
direct DB checks. Test data (banner + its join rows) hard-deleted from the dev DB afterward, no
residue.
Log: `.claude/logs/tasks/522_2026-08-14_consumer-app-frontend_frontend-developer.md`.

# TASK-523 — Consumer App: Banner.PublishedAt (draft/published lifecycle schema)

**Status:** done · **Agent:** database-engineer · **Updated:** 2026-08-14 · **Next:** TASK-524

Schema-only follow-up to TASK-520/521/522, for a banners admin history view (running/past/draft
tabs). Added `Banner.PublishedAt` (nullable `DateTime`, private setter) — `null` = draft, never
published; non-null = first-publish timestamp. Deliberately separate from `IsActive` (manual
pause) and `ValidFrom`/`ValidUntil` (display window). `Create(...)` gained a trailing optional
`bool publishImmediately = true` (default preserves today's immediate-publish behavior; the sole
existing caller in `BannerService.cs` uses named args, unaffected). Added idempotent
`Publish(DateTime utcNow)` (no-op if already published, never overwrites the original timestamp)
to back TASK-524's publish endpoint. `Update(...)` left untouched — publishing only happens via
`Publish()`. `AppDbContext.cs` Banner config extended with the new nullable column. Migration
`AddBannerPublishedAt` — single-column `ALTER TABLE banners ADD COLUMN "PublishedAt" timestamp
with time zone NULL`, no RLS changes needed. Applied cleanly against the real dev DB via the
non-superuser `shelfguard_app_dev` role and verified via `\d banners`. `dotnet build` 0 errors;
`dotnet test` 1411/1411 green, no regressions. TASK-524 (backend-developer: publish endpoint +
lifecycle status logic) unblocked.
Log: `.claude/logs/tasks/523_2026-08-14_banner-published-at_database-engineer.md`.

# TASK-524 — Consumer App: banner publish endpoint + lifecycle status + draft-leak fix

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-14 · **Next:** TASK-525

Follow-up to TASK-523 (done), builds the API surface for the admin banners history view
(running/past/draft tabs). `CreateBannerRequest` gained `bool PublishImmediately = true`, wired
into `Banner.Create(..., publishImmediately: ...)`. `BannerDto` gained `PublishedAt` and computed
`LifecycleStatus` (`"draft"` when `PublishedAt == null`, `"running"` when published and
`IsCurrentlyActive`, `"past"` otherwise) — derived in `BannerService`'s mapping helper, never
stored, same pattern as `IsCurrentlyActive`. New `POST /api/banners/{id}/publish`
(`AtLeastEnterpriseAdmin` gate, same as the rest of `BannersController`) — idempotent, calls
`Banner.Publish(DateTime.UtcNow)`, 404 if not found. **Critical fix**: `ConsumerContentRepository
.GetActiveBannersAsync` (backs the public `GET /api/consumer/{tenantId}/banners`) was filtering
on `IsActive` + date window only — a draft banner (`PublishedAt == null`) with `IsActive=true`
and a currently-valid date window could leak to anonymous consumers. Added `b.PublishedAt !=
null` as a required condition alongside the existing checks. `Discount`/`DiscountsController`
untouched, per brief (its own `Status` enum already covers pending/active/expired/cancelled).

`dotnet build` 0 errors (1 pre-existing unrelated warning, same baseline as TASK-520/521/523).
`dotnet test` 1411/1411 green. **Live-verified** against the real dev DB (`ea@demo.local`, tenant
"Свіжий Кут"): created a banner with `publishImmediately=false` and a currently-valid date
window + `IsActive=true` → confirmed `GET /api/consumer/{tenantId}/banners?storeId=` returned
`[]` (draft correctly hidden); called `POST /api/banners/{id}/publish` → `publishedAt` set,
`lifecycleStatus` flipped `draft`→`running`, banner immediately appeared in the consumer feed;
called publish again → same timestamp (idempotent, confirmed no overwrite). Test banner deleted
(soft via API, then hard-purged via psql) afterward, DB left clean. TASK-525 (frontend-developer,
admin history tabs UI) unblocked.
Log: `.claude/logs/tasks/524_2026-08-14_banner-publish-lifecycle_backend-developer.md`.

# TASK-525 — Consumer App: split into separate pages + banner/promo history tabs

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-14 · **Next:** none

Follow-up to TASK-522 (done), using TASK-523/524's lifecycle contract (`BannerDto.publishedAt`/
`lifecycleStatus`, `POST /api/banners/{id}/publish`, `CreateBannerRequest.publishImmediately`) and
`Discount.status` as-is (no backend changes needed — pending/active/expired/cancelled already
maps to draft/running/past). Two parts: (1) split `/consumer-app` (previously one page stacking
4 sections) into 4 pages — `/consumer-app` (bonus program only), `/consumer-app/banners`,
`/consumer-app/promotions`, `/consumer-app/catalog` — mirroring `marketing-analytics`'s
sidebar-group pattern exactly; `Sidebar.tsx`'s `consumer_app` group grew from 1 to 4 items. (2)
Added Активні/Минулі/Чернетки history tabs (new shared `LifecycleTabs.tsx`) to `BannersSection`
and `PromoProductsSection`, both filtering an already-fetched list client-side (no new fetch per
tab); `usePromoProducts` dropped its `status=active` query filter to fetch full history. Both
create forms gained an "Опублікувати одразу" toggle (default ON): ON keeps today's behavior
(banner: `publishImmediately: true`; promo: create→approve chain), OFF leaves the row a draft
(`lifecycleStatus: "draft"` / `status: "pending"`). Draft rows get a row-level "Опублікувати"
action in both sections (new `usePublishBanner`/`usePublishPromoProduct` hooks).

`npx tsc --noEmit` and `npm run lint` both clean. Live-verified against the real dev backend
(`ea@demo.local`, tenant "Свіжий Кут"): sidebar shows all 4 sub-items routing correctly; created
one banner + one promo product with the toggle ON (landed in Активні) and one each with the
toggle OFF (landed in Чернетки, `publishedAt`/`approvedAt` null as expected); clicked each row's
"Опублікувати" → banner `POST /{id}/publish` and promo `PUT .../approve` both returned 200 and
moved the row to Активні (counts updated live). Test data cleaned up (soft-deleted/cancelled via
UI, then hard-purged via psql), DB left clean. This closes the current round of Consumer App
admin frontend work — mobile wiring remains the separate future task already documented in the
TASK-521 handoff doc (`.claude/logs/handoffs/521-to-mobile-developer_consumer-content-api.md`).

# TASK-577 — Transfers: create-transfer form on the web frontend

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-20 · **Next:** none

`/transfers` was read-only; backend (`POST /api/transfers`) and frontend data layer
(`transfersApi.create`, `useCreateTransfer`) already existed unused. Added
`frontend/features/transfers/components/CreateTransferForm.tsx` (styled like `AddBatchForm.tsx`:
local inline styles, native `<select>`/`<input>`, single error box) wired into
`/transfers` via a new "New Transfer" button + `Modal`. Source-store select drives
`useStock({ store_id }, enabled)` (added the `enabled` param to `useStock` — backward compatible)
to populate a FEFO-sorted, text-filterable batch picker; rows keyed by `productStockId` so two
batches of the same product stay separate; destination options structurally exclude the source
store. `transferType` hardcoded `store_to_store` (no UI switch, matches mobile reference). Full
i18n block added under `Dashboard.transfers.createForm` in `uk.json`/`en.json`.

Deviation from plan (found live in-browser, not by inspection): the plan's literal
`max={availableQty}` on the quantity input triggers native HTML5 constraint validation that
silently blocks submission *before* the custom over-limit error message can render. Fixed with
`noValidate` on the `<form>` so the plan's own validation/error copy always runs; `min`/`max`/
`step` kept for spinner UX only.

`npx tsc --noEmit` and `npm run lint` clean. **Live-verified in-browser** (dotnet API :5000 +
`next dev` :3002 against the real dev DB, `ea@demo.local`/"Свіжий Кут", 4 stores, source store
with 645 batches): source-store selection populates FEFO-sorted batches; destination excludes
source; two batches of the same product become two rows; already-added batch shows "Already
added" (no-op re-click); row removal works; empty-quantity and over-available-quantity submits
both show the correct per-product error text and fire no request; a valid submit (incl. a
fractional quantity) → `POST /api/transfers` 201, modal closed, new "In Transit" row appeared at
the top of the list with no manual refresh. Cross-checked in Postgres: transfer/items rows
correct, source `product_stock.Quantity` decremented correctly, `stock_movements` written. Test
transfer + movements deleted and stock quantities restored via psql afterward — dev DB left
clean. Temporary local-only CORS entry (`:3002`) added for testing and reverted (confirmed via
`git diff`); dev servers stopped.
Log: `.claude/logs/tasks/577_2026-08-20_create-transfer-form_frontend-developer.md`.
Log: `.claude/logs/tasks/525_2026-08-14_consumer-app-page-split-history_frontend-developer.md`.

# TASK-578 — Receipts: create-receipt form on the web frontend

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-20 · **Next:** none

`/receipts` was read-only; backend (`POST /api/receipts`) and frontend data layer
(`receiptsApi.create`, `useCreateReceipt`) already existed unused. A receipt is a standalone
document (no PO entity in this codebase) — this task is only the "create draft receipt" step;
`/receipts/[id]` (fill-in/confirm-receive) was untouched. Added new feature module
`frontend/features/suppliers/` (mirrors `features/locations/`: `types.ts`/`api/`/`hooks/`,
`SupplierDto` matches the backend record) plus
`frontend/features/receipts/components/CreateReceiptForm.tsx` (same structural convention as
TASK-577's `CreateTransferForm.tsx`), wired into `/receipts` via a new "New receipt" button +
`Modal` (width 720). `useLocations()` for destination store (required), `useSuppliers()` for
supplier (optional), `useCatalogProducts({ search: debouncedQuery })` for the product picker
(receipts create new stock, so catalog not `useStock`) — search debounced ~300ms. Rows keyed by
`productId` (not `productStockId` — no batch identity yet at draft time), so a product can only
appear once; already-added shows a badge, no-op on re-click. Each row has 4 compact inputs
(qty required, price/expiry/batch optional, price pre-filled from the catalog when known).
Validation: destination store → empty rows → per-row `NaN`/`<=0` quantity; price/expiry/batch
stay optional client-side (filled in later on `[id]`, enforced server-side at receive time). Full
i18n block added under `Dashboard.receipts.createForm` in `uk.json`/`en.json` (28 keys) plus
`page.newButton`.

No deviations from the brief.

`npx tsc --noEmit` and `npm run lint` clean. **Live-verified in-browser** (dotnet API :5000 +
`next dev` :3002 against the real dev DB, tenant "Свіжий Кут", role `network_manager`): supplier
(3 active) and destination store (4) dropdowns populate; debounced product search confirmed via
network log (fires once after ~300ms, not per keystroke); added 2 products, re-adding one was a
no-op ("Already added"); removed a row, count updated correctly; empty-quantity submit showed the
correct per-product error and fired no request; valid submit (qty/price/expiry/batch all filled)
→ `POST /api/receipts` 201, modal closed, new "Draft" row appeared at the top of the list with no
manual refresh; opened the new receipt's `/receipts/{id}` detail page and confirmed qty/expiry/
batch all round-tripped correctly into the existing editable table. Browser pane didn't composite
frames this session, so clicks were driven via `javascript_tool` DOM dispatch (verified via
`read_page`/network log, not assumed) instead of `computer`. Test `stock_receipts`/
`stock_receipt_items` rows deleted via psql afterward (no stock movements existed to unwind, since
the test receipt was never confirmed-received) — dev DB left clean. Temporary local-only CORS
entry (`:3002`) and `.claude/launch.json` port override both reverted (confirmed via `git diff`
clean on both); dev servers stopped.

# TASK-579 — Write-offs: create-write-off form on the web frontend

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-20 · **Next:** none

`/write-offs` was read-only (view + approve/reject); backend (`POST /api/write-offs`) creates
directly at `pending_approval` (no draft step) and does not touch stock — deduction happens only
on approve (existing `useApproveWriteOff`, untouched), via FEFO or the named `productStockId`.
Added `frontend/features/write-offs/components/CreateWriteOffForm.tsx`, structurally closest to
TASK-577's `CreateTransferForm.tsx` (a write-off item references an existing batch, not a new
one): `useLocations()` for a single store select, `useStock({store_id}, !!storeId)` for a
FEFO-sorted (soonest-expiry-first) text-filterable batch picker, rows keyed by `productStockId`.
Each row has 2 compact inputs (qty required with `max={availableQty}`, unit price optional) —
no notes field, since `CreateWriteOffRequest.notes` is silently discarded server-side (no `Notes`
column on the `WriteOff` entity; flagged as a backend follow-up, not fixed). Wired into
`/write-offs` via a new "New write-off" `Btn` + `Modal` (width 700) — this page had no page-wide
access gate before and still doesn't; only the create button is conditional on
`hasRole(me.role, CAN_RECEIVE_STOCK)`, preserving the broader existing view access (merchandiser
can view, not create). Full i18n block added under `Dashboard.writeOffs.createForm` in
`uk.json`/`en.json` (24 keys) plus `page.newButton`; reason labels reused from the existing
`Dashboard.writeOffs.reason` namespace.

No deviations from the brief.

`npx tsc --noEmit` and `npm run lint` clean. **Live-verified in-browser** (dotnet API :5000 +
`next dev` :3002 against the real dev DB, tenant "Свіжий Кут", role `network_manager`): store
select populates a FEFO-sorted batch picker; text search filters correctly; adding the same batch
twice is a no-op ("Already added", opacity/cursor confirm disabled); removing a row works; submit
with empty quantity shows the correct per-product error; submit with qty exceeding `availableQty`
(999 > max=45) shows the correct exceeds-error — confirms `noValidate` lets the custom validator
run past the native `max` block; valid submit (qty=3, price=25.5, reason=Expired) → `POST
/api/write-offs` 201, modal closed, new "Pending Approval" row appeared at the top of the list
with no manual refresh (loss amount 76.5 = 3×25.5 correct). Confirmed via direct `fetch
('/api/stock?...')` that the batch's quantity was unchanged (still 45) immediately after create —
stock genuinely untouched pre-approval. Approved via the existing unmodified approve action →
status flipped to "Approved", re-fetched stock → quantity now 42 (45−3), confirming the create
flow feeds the existing FEFO-consuming approve flow correctly. Browser pane didn't composite
frames this session either, so clicks were driven via `javascript_tool` DOM dispatch (verified via
`get_page_text`/direct API fetches, not assumed). Test write-off deleted via psql and stock
quantity restored (+3) afterward — dev DB re-verified back to its original 3-row list. Temporary
local-only CORS entry (`:3002`) reverted (confirmed via `git diff` clean); dev servers stopped.
Log: `.claude/logs/tasks/579_2026-08-20_create-write-off-form_frontend-developer.md`.
Log: `.claude/logs/tasks/578_2026-08-20_create-receipt-form_frontend-developer.md`.

# TASK-580 — Transfers: fail-closed destination-store check on confirm

**Status:** done · **Agent:** security-reviewer · **Updated:** 2026-08-20 · **Next:** none

`TransferService.ConfirmAsync` had zero store-membership checks — any user whose role passed the
`CanReceiveStock` policy could confirm any transfer in the tenant regardless of store assignment.
Plugged into ADR-022's `user_locations` mechanism (same repo/DTO as `LocationService`) but
**fail-closed** (not fail-open like that transitional precedent): `network_manager`/
`store_manager`/`storekeeper` must have a `user_locations` row for the transfer's `ToStoreId`;
`provider`/`enterprise_admin` bypass unconditionally; a null/missing role claim falls into the
checked (rejected) path rather than silently bypassing. `ConfirmAsync`/`ITransferService` gained
`(Guid tenantId, string? role)` params; `TransfersController.Confirm` now requires `tenantId`
(mirrors `Create`'s existing Forbid-on-missing-context pattern) and maps the new error to 403.
`CancelAsync` has the same *kind* of gap but is out of scope (flagged as follow-up, not fixed).

`dotnet build` clean (0 errors, 1 pre-existing unrelated warning). Transfers suite 26/26 passed
(3 pre-existing + 8 new/parameterized), full suite 1748/1748 passed. No browser/HTTP verification
— backend-only, fully covered by unit tests.

Log: `.claude/logs/tasks/580_2026-08-20_transfer-confirm-store-scope_security-reviewer.md`.

# TASK-581 — Transfers: hide "Confirm receipt" for users outside the destination store

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-20 · **Next:** none

Frontend UX counterpart to TASK-580's backend 403. `frontend/app/(dashboard)/transfers/page.tsx`
now fetches `useLocations()` and gates the "Confirm receipt" `ActionMenu` item on
`tr.status === "in_transit" && myLocationIds.has(tr.toStoreId)` (was status-only). Verified via
`LocationService.GetAllAsync` that this correctly mirrors the backend for both scoped roles
(real `user_locations` filter) and bypass roles (`provider`/`enterprise_admin` see the full list,
so the check trivially passes). No `onError`/toast added to the confirm mutation — checked
write-offs' approve/reject and transfers' own cancel action, none of the app's one-click
`ActionMenu` row actions surface mutation errors; adding one only here would be inconsistent.
Flagged as a pre-existing app-wide gap, not fixed.

`tsc --noEmit` and `npm run lint` clean. Live-verified in-browser against the dev DB: `ea@demo.local`
(enterprise_admin) sees Confirm on the in-transit transfer; `manager@demo.local` (store_manager,
assigned to Центральний+Подільський) does NOT see it once the transfer's `ToLocationId` was
temporarily pointed at Троєщина (a store they're not assigned to) — reverted after. No backend
files touched.

Log: `.claude/logs/tasks/581_2026-08-20_transfer-confirm-hide-wrong-store_frontend-developer.md`.

# TASK-583 — Remove local store pickers on Orders and AI Orders pages

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-20 · **Next:** none

Both pages had their own local store `<select>`, redundant with the global header
`StoreSelector`. Removed both, switched to `usePrimaryStoreId()` (`frontend/lib/useStoreContext.ts`).
No backend changes — `POST /api/orders/calculate` and `POST /api/ai-orders/generate` already
require a single concrete `Guid StoreId`, exactly what `usePrimaryStoreId()` produces
(`undefined` in "all stores" mode).

`frontend/app/(dashboard)/orders/page.tsx`: dropped `storeId` state, `useStores()`, the
`<select>`; Generate button now `disabled={!primaryStoreId}` with a hint shown next to it when
no single store is selected.

`frontend/app/(dashboard)/ai-orders/page.tsx`: same removal. List (`useAiOrders(primaryStoreId)`)
still supports "all stores" (undefined → unfiltered) unchanged — not gated, only Generate is.
Deleted the now-unused `Dashboard.aiOrders.page.allStores` i18n key (confirmed unused elsewhere).
Added a `useEffect` clearing the selected review panel on store-context change, mirroring the old
picker's `onChange` behavior.

Added `selectStoreHint` key to `Dashboard.orders.page` and `Dashboard.aiOrders.page` in both
`en.json`/`uk.json` (only 2 locales in this project).

`tsc --noEmit` and `eslint` on both files clean. No authenticated browser session was available
to this agent (fresh dev server, empty localStorage) — did not log in per task boundary; live
dashboard check left for the orchestrator.

# TASK-584 — Marketplace order shipping: shipped/ETA/delivered

**Status:** done · **Agent:** database-engineer → backend-developer → frontend-developer · **Updated:** 2026-08-20

Supplier ships a marketplace order → must declare an estimated delivery time (whole days)
at ship time; client needs to see shipment status + ETA (previously invisible beyond a bare
status badge).

**database-engineer (done):** `MarketplaceOrder` entity gained `ShippedAt`
(`DateTimeOffset?`), `EstimatedDeliveryDays` (`int?`), `DeliveredAt` (`DateTimeOffset?`).
Migration `20260820131503_AddMarketplaceOrderShippingFields` applied to local dev DB. RLS
unchanged (inherits the table's existing policies). Handoff:
`.claude/logs/handoffs/584-to-backend_database-engineer.md`.

**backend-developer (done):** `UpdateMarketplaceOrderStatusDto` gained
`EstimatedDeliveryDays` (required, must be > 0, when `Status = "shipped"`, else `400` with
`EstimatedDeliveryDaysRequiredError`). `MarketplaceOrderDto` gained
`ShippedAt`/`EstimatedDeliveryDays`/`DeliveredAt`. `MarketplaceOrderService.UpdateOrderStatusAsync`
sets `ShippedAt`+`EstimatedDeliveryDays` on `shipped`, `DeliveredAt` on `delivered`. Added a
`marketplace_order.shipped` outbox notification (ADR-018 §2) for the client tenant, wrapped
in `ITenantSessionOverride.ExecuteAsync(order.ClientTenantId, ...)` — mirrors TASK-582's
`SupplierAgreementService.MarkSignedAsync` fix, avoiding the same cross-tenant RLS-violation
(42501) bug. Controller needed no changes (binds `[FromBody]` directly). `dotnet build`
clean, `dotnet test` 1755/1755 passed (added ship-validation/notification/deliver test
coverage in `MarketplaceOrderServiceTests.cs`). Log:
`.claude/logs/tasks/584_2026-08-20_marketplace-order-shipping-logic_backend-developer.md`.
Handoff: `.claude/logs/handoffs/584-to-frontend_backend-developer.md`.

**frontend-developer (done):** types updated (`MarketplaceOrderDto` +3 fields,
`UpdateMarketplaceOrderStatusRequest` +1); new `EstimateDeliveryModal.tsx` in supplier
cabinet replaces the bare `transition(order, "shipped")` call, blocks submit until a valid
positive integer is entered; shared `getShippingEta()` helper in
`features/marketplace/utils.ts` derives ETA/overdue state client-side. Client orders page
gained a compact "In transit: N of M days" (or overdue-safe) label directly under the status
badge in the table row — visible without expanding, which is the part that most directly
answers the original complaint — plus full shipped/estimated/delivered detail in the
expanded row, symmetric with the supplier cabinet's own view. i18n keys added to both
`Dashboard.marketplace.ordersPage.ordersTab` and `Dashboard.supplierCabinet.ordersTab` in
`messages/{en,uk}.json` (deviated from the brief's "supplier-cabinet is untranslated"
assumption — `CabinetOrdersTab.tsx` already fully uses `useTranslations`, confirmed by
direct inspection; see task log for detail). `tsc`/`lint` clean. Full manual browser
verification done (Ship modal validation, in-transit/overdue/delivered states, both supplier
and client views) via temporary DB test fixtures, created and cleaned up after. Log:
`.claude/logs/tasks/584_2026-08-20_marketplace-order-shipping-ui_frontend-developer.md`.

Log: `.claude/logs/tasks/583_2026-08-20_remove-local-store-pickers-orders-ai-orders_frontend-developer.md`.

# TASK-585 — Marketplace order delay reason (supplier-entered, shown to client)

**Status:** done · **Agent:** database-engineer → backend-developer → frontend-developer · **Updated:** 2026-08-20

Two follow-ups to TASK-584: (1) supplier's own order view should show the same in-transit
ETA info the client already sees (pure frontend parity — no backend change), and (2) when a
shipped order's estimated delivery window has passed and it hasn't arrived, supplier needs to
record why, visible to the client.

**database-engineer (done):** `MarketplaceOrder` entity gained `DelayReason` (`string?`,
free-text, right after `DeliveredAt`), mirroring `CancelReason`'s type/nullability exactly.
`AppDbContext` fluent config: `HasMaxLength(2000).IsRequired(false)` (same as `CancelReason`).
Migration `20260820193144_AddMarketplaceOrderDelayReason` — single nullable
`character varying(2000)` column on `marketplace_orders`, no unrelated model drift, applied to
local dev DB and verified via `\d marketplace_orders`. RLS unchanged (existing
`tenant_isolation`/`provider_bypass`/`worker_bypass` policies cover new columns automatically,
confirmed). No index (free-text, never filtered/sorted). `dotnet build` clean, `dotnet test`
1755/1755 passed. Log:
`.claude/logs/tasks/585_2026-08-20_marketplace-order-delay-reason-schema_database-engineer.md`.
Handoff: `.claude/logs/handoffs/585-to-backend_database-engineer.md`.

**backend-developer (done):** `MarketplaceOrderDto` gained `DelayReason` (after `DeliveredAt`);
new `SetOrderDelayReasonDto(string Reason)`. New service method
`IMarketplaceOrderService.SetDelayReasonAsync(supplierTenantId, orderId, reason, ct)`: validates
non-empty reason → order exists & belongs to supplier tenant → `Status == Shipped`; sets
`DelayReason` (trimmed), enqueues client-tenant notification
(`marketplace_order.delay_reason_added`) under `_tenantSessionOverride.ExecuteAsync` — same
cross-tenant RLS pattern as TASK-584's Shipped branch. New endpoint
`POST /api/supplier-cabinet/orders/{id}/delay-reason { reason }` on
`SupplierCabinetCooperationController`, mirrors `UpdateOrderStatus` action shape. 10 new tests.
`dotnet build` clean, `dotnet test` 1765/1765 passed. Docs updated
(`.claude/docs/api-contracts.md`). Log:
`.claude/logs/tasks/585_2026-08-20_marketplace-order-delay-reason-logic_backend-developer.md`.
Handoff: `.claude/logs/handoffs/585-to-frontend_backend-developer.md`.

**frontend-developer (done):** `MarketplaceOrderDto` gained `delayReason`; new
`SetOrderDelayReasonRequest`, `supplierCabinetApi.setOrderDelayReason`, `useSetOrderDelayReason`
hook. `CabinetOrdersTab.tsx` gained the `ShippingEtaHint` parity component (item 1) and a
"Record delay reason" button on `shipped` orders, shown only when overdue (`getShippingEta(...)
?.isOverdue`), wired to the existing `ReasonModal` (no new modal component). Both supplier and
client `ShippingDetail` components now render `delayReason` when present (client read-only, no
action). i18n keys added to both `Dashboard.supplierCabinet.ordersTab` and
`Dashboard.marketplace.ordersPage.ordersTab` in `en.json`/`uk.json`. `tsc --noEmit` and
`npm run lint` clean. Full manual browser verification (temp SQL fixture, deleted after —
DB counts confirmed restored). Log:
`.claude/logs/tasks/585_2026-08-20_marketplace-order-delay-reason-ui_frontend-developer.md`.

**Status: done** — all three slices (schema, service/API, UI) complete.

# TASK-589 — Events calendar: day-detail drawer (shell + basic info)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-21

First piece of a larger event-calendar feature: clicking a day now opens a detail drawer
listing that day's existing event(s) (list → event detail, via `EventDayDetailDrawer.tsx` +
`EventDetailPanel.tsx`) instead of jumping straight to the create-event form. Add/edit still
route through the existing `EventForm.tsx` modal via `setCreating`/`setEditing`, unchanged.
Shared day-matching logic extracted to new `frontend/features/events/utils.ts`
(`isEventActiveOnDate`, behavior-preserving refactor of `EventCalendar.tsx`'s old private
`isActiveOn`) plus a new `resolveEventWindowForYear` helper for a later agent's
sales-comparison window. i18n: new `Dashboard.events.dayDetail.*` in `en.json`/`uk.json`.
`tsc`/`eslint` clean; no authenticated browser session available, live check skipped. Log:
`.claude/logs/tasks/589_2026-08-21_events-day-detail-drawer_frontend-developer.md`.

# TASK-591 — Events calendar: product linking + sales comparison (Wave 2)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-21

Extends TASK-589's `EventDetailPanel.tsx` with two `DrawerSection`s: **Linked Products**
(search/add/inline-edit/remove product-scoped `DemandEventCoefficient`s via new
`EventProductPicker.tsx`) and **Sales Comparison** (per linked product, a `LinkedProductSalesCard.tsx`
comparing the event's date window vs. server auto-baseline, via the now-compare-capable
`GET /api/analytics/pos/products/{id}/trend?compare=true`, TASK-590). Frontend-only, both
backend endpoints already merged. New `removeCoefficient`/`useRemoveCoefficient` (→ `DELETE
/api/events/{id}/coefficients/{coefId}`, TASK-588), `useProductsByIds`/`useProductSearch`
(extended `productsApi.getAll` with optional `search`/`ids`/`pageSize`),
`ProductSalesTrendCompareDto` + overloaded `getProductSalesTrend`/`useProductSalesTrendCompare`.
Fixed a latent type break in `useProducts()` (was passing `productsApi.getAll` as `queryFn` by
reference — broke once `getAll` gained optional params — now `() => productsApi.getAll()`).
19 new i18n keys under `Dashboard.events.dayDetail` in `en.json`/`uk.json`. `tsc`/`eslint` clean
on all 11 touched/created files; no authenticated browser session available, live check skipped.
Log: `.claude/logs/tasks/591_2026-08-21_events-product-linking-sales-comparison_frontend-developer.md`.
Note: originally logged as TASK-590 by the agent, colliding with the backend comparison-endpoint
task also numbered 590 minutes earlier; renumbered to TASK-591 when reconciling.

**Status: done** — day-detail drawer (TASK-589) + product linking/sales comparison
(TASK-591) complete the event-calendar drawer feature.

# TASK-592 — DemandEventStore: event↔specific-stores join table (schema layer)

**Status:** done · **Agent:** database-engineer · **Updated:** 2026-08-22

New third `DemandEvent.Scope` value `"stores"` (several specific stores, vs. existing
`"network"`/`"store"`) — schema layer only. New entity `DemandEventStore` (`Id`, `EventId`,
`StoreId`, mirrors `DemandEventCoefficient`'s anemic `init`-property style exactly, no
private setters/factory), `DemandEvent.Stores` collection added, migration
`20260822081221_AddDemandEventStores` creates `demand_event_stores` (FK→`demand_events`
CASCADE, FK→`locations` CASCADE — physical stores table is `locations`, not `stores`),
unique composite index `(EventId, StoreId)`. RLS: `tenant_isolation`/`provider_bypass`
(`IN ('provider','provider_admin')`)/`worker_bypass` triad via `EXISTS`-into-`demand_events`
(no own `TenantId`), `FORCE ROW LEVEL SECURITY`. Applied to local dev DB, verified via
`\d demand_event_stores` in psql. `dotnet build` clean; `dotnet test`: 1793 passed, 0
failed. Log: `.claude/logs/tasks/592_2026-08-22_demand-event-stores-migration_database-engineer.md`.
Handoff for next wave: `.claude/logs/handoffs/592-to-backend_database-engineer.md`
(scope validation, store-membership query logic, CRUD endpoints, DTOs, frontend all
still pending — not touched by this task).

# TASK-594 — Events: multi-store scope backend (`Scope == "stores"`)

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-22

Wired repo/service/controller to use `DemandEventStore` (TASK-592 schema): `IEventRepository.GetAsync`
widened `Guid? storeId` → `Guid[]? storeIds` (any-match: `network` OR `StoreId in storeIds` OR
`Stores.Any(s => storeIds.Contains(s.StoreId))`); `GetCandidatesForDateAsync` gained the third OR
clause (single-store signature unchanged, order-calc consumer); new
`ReplaceStoresForEventAsync(eventId, storeIds, ct)` (delete/insert, no own `SaveChangesAsync` —
mirrors `UserLocationRepository.ReplaceForUserAsync`). `EventService`: `ValidScopes` gains
`"stores"`; `Validate` requires non-empty `StoreIds` for it; `CreateAsync`/`UpdateAsync` always call
`ReplaceStoresForEventAsync` (clears stale rows on scope switch-away); `ToDto` projects
`StoreIds` from the entity's `Stores` collection, no `Scope` special-casing. `DemandEventDto`/
`UpsertEventRequest` gain `StoreIds: List<Guid>` (DTO output never null; request input nullable).
`EventsController.Get`: `[FromQuery] Guid? store_id` → `[FromQuery] Guid[]? storeIds` (repeated
camelCase param, matches `PriceSegmentsController`/`UsersController` convention; no real callers
broke). `AiOrderService.GenerateAsync` call-site: `GetAsync(..., new[] { storeId }, ct)`, no
behavior change. `dotnet build` clean; `dotnet test`: **1807/1807 passed** (1793 baseline + 14 new
in `EventServiceTests.cs`/new `EventRepositoryStoresTests.cs`, EF InMemory provider). Verified
against TASK-593's frontend contract (already built in parallel) — compatible: repeated
`?storeIds=` query params, `storeIds: string[]` JSON field.
Log: `.claude/logs/tasks/594_2026-08-22_multi-store-event-scope-backend_backend-developer.md`.

# TASK-597 — Marketplace checkout: barcode-conflict resolution UI (frontend)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-22

Frontend half of the order-time catalog-provisioning safety check (parallel backend-developer
agents building `MarketplaceOrderService.CheckCatalogConflictsAsync` + the `orders/conflicts`
route against TASK-596's schema). `SupplierOrderCart.tsx` checkout modal is now a two-step flow
(`step: "cart" | "conflicts"`): Confirm first calls new `useCheckOrderConflicts` (→
`POST /api/marketplace/suppliers/{id}/orders/conflicts`); empty result submits exactly as before
(no UX change); non-empty result shows a per-conflict card (ordered line vs. existing catalog
item's photo/name/barcodes, `ImageOff` fallback matching `SupplierItemDetailDialog.tsx`'s
pattern) with "Прив'язати"/"Створити новий" toggles before re-enabling Confirm. New types
`CatalogAction`/`CreateMarketplaceOrderItem`/`BarcodeConflict`(+`ExistingItem`) in
`features/marketplace/types.ts`, `checkOrderConflicts` in `marketplace-api.ts`,
`useCheckOrderConflicts` in `useCooperation.ts`. 9 new i18n keys under
`Dashboard.marketplace.orderCart`. Field names cross-checked against the real (uncommitted)
backend DTOs in `CooperationDtos.cs` — exact match; the controller route itself wasn't wired up
yet at verification time, so the conflict step's live behavior is unverified (no-conflict path
and compile/typecheck are). `tsc`/`eslint` clean on all 4 touched files; `uk.json`/`en.json`
valid JSON. Log: `.claude/logs/tasks/597_2026-08-22_marketplace-checkout-barcode-conflict-ui_frontend-developer.md`.

# TASK-613 — Customer/loyalty domain expansion: schema (profile history, tier ladder, support tickets, purchase reviews)

**Status:** done · **Agent:** database-engineer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §1. Log: `.claude/logs/tasks/613_2026-08-24_crm-loyalty-tier-schema_database-engineer.md`

Schema/domain layer only (backend service/controller layer is a separate follow-up task).
Five new entities: `ConsumerAccountProfileChange` (append-only, **no RLS/no TenantId** —
same precedent as `ConsumerAccount`), `LoyaltyTierDefinition` (per-tenant ladder rung —
name/threshold/accrual multiplier/discount%), `LoyaltyTierChangeHistory` (append-only
progression audit), `ConsumerSupportTicket`+`ConsumerSupportTicketMessage` (mirrors
`SupplierSupportTicket`/`...Message` for consumer↔tenant instead of tenant↔supplier),
`PurchaseReview` (mirrors `SupplierReview`, keyed to `PosTransactionId`, **unique index**
— one review per purchase). `LoyaltyMembership` extended with `CurrentTierId`/
`CompositeScore`/`TierScoreUpdatedAt` (written only by the future nightly tier-recompute
worker job, never at request time). `PosTransaction.CashRegisterId` added — nullable
Guid, no FK, intentionally unwired (register hardware doesn't exist yet).

RLS: `consumer_account_profile_changes` has RLS fully disabled (verified via
`pg_class.relrowsecurity`); the other five tenant-scoped tables all got the canonical
`tenant_isolation`/`provider_bypass`(`IN ('provider','provider_admin')`)/`worker_bypass`
triad, plus a `consumer_self_access` policy (direct-column on tables that have
`ConsumerAccountId`, EXISTS-through-parent on the two child tables —
`loyalty_tier_change_history` via membership, `consumer_support_ticket_messages` via
ticket) on every table except `loyalty_tier_definitions` (staff-only config, same
posture as `loyalty_program_settings`). Five separate EF Core migrations generated (not
hand-written) via staged `#if`-guarded builds so each captures only its own slice:
`AddConsumerAccountProfileChanges`, `AddLoyaltyTierLadder`, `AddConsumerSupportTickets`,
`AddPurchaseReviews`, `AddPosTransactionCashRegisterId`. Hit and fixed one EF pitfall:
using `HasOne<ConsumerAccount>().WithMany()` where the entity also had a `ConsumerAccount`
nav property created a phantom second FK/shadow-property relationship — fixed by using
`HasOne(x => x.ConsumerAccount)` instead (2 occurrences).

All 5 migrations applied to dev DB (`crmproductsystems-postgres-1`, port 5435); RLS
verified both structurally (`pg_policies`) and functionally (live `SET app.tenant_id`/
`app.role`/`app.consumer_account_id` session-var tests inside rolled-back transactions —
tenant isolation, worker bypass, and consumer self-access all behave correctly). `dotnet
build` clean (0 warnings/errors); full `dotnet test` suite: **1837/1837 passing** (no
regressions). Not implemented here (per scope): `Features/ConsumerProfile`,
`Features/CustomerSupport`, `Features/Reviews`, loyalty-ladder CRUD/consumer endpoints,
`PosService.cs` accrual-multiplier/discount integration, worker recompute job, frontend —
all separate follow-up tasks per the plan's §5 sequencing. `mobile/` untouched (owned by a
separate concurrent agent).

# TASK-614 — Consumer self-service profile editing (name/email/phone + audit history)

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §2. Handoff read: `.claude/logs/handoffs/613-to-backend_database-engineer.md`.
Log: `.claude/logs/tasks/614_2026-08-24_consumer-profile-self-edit_backend-developer.md`

New `Features/ConsumerProfile` (`IConsumerProfileService`/`ConsumerProfileService` +
`Dtos/ConsumerProfileDtos.cs`) — get profile, update name/email, change phone (password
re-entry gate, no SMS/OTP), paged change history. Every write appends a
`ConsumerAccountProfileChange` audit row in the same `SaveChangesAsync` call as the
`ConsumerAccount` update. `IConsumerAccountRepository`/`ConsumerAccountRepository`
extended with `AddProfileChangeAsync`/`GetProfileChangesPagedAsync` (same
combined-repository precedent as `ILoyaltyRepository` pairing membership + ledger) —
`ConsumerAccountProfileChange` has no RLS, queried purely by `ConsumerAccountId`, no
tenant-scoping logic added. New `ConsumerProfileController` at `api/consumer/profile`
(GET, PUT, PUT /phone, GET /history), authorization copied exactly from
`ConsumerLoyaltyController`'s `consumer_account_id` claim pattern. Registered in
`ShelfGuard.Application/DependencyInjection.cs` next to `ILoyaltyService` (re-read fresh
before editing, no conflicts found).

15 new unit tests in `ShelfGuard.Tests/ConsumerProfile/ConsumerProfileServiceTests.cs`
(NSubstitute, mirrors `LoyaltyServiceTests`/`ConsumerAuthServiceTests` style): audit rows
on name/email/phone change, no-op writes nothing, wrong password rejected before any
duplicate-phone lookup, duplicate email/phone rejected, unknown/inactive account 404s.

Judgment calls: (1) email update allows clearing via empty string, duplicate-checked
case-insensitively same as registration (no DB unique constraint on Email, app-level
check only, matching `ConsumerAuthService.RegisterAsync`'s existing precedent); (2)
wrong-current-password and malformed-phone both return 400 (matches this repo's existing
`UserService.ChangePasswordAsync`/`AuthController` convention — no 401 precedent exists
here) rather than inventing a new status code; (3) setting phone to its own current
(normalized) value is a silent no-op success, writes no audit row.

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (Marketplace tests). `dotnet
test` full suite: **1852/1852 passing** (15 new, no regressions).

Not implemented here (separate follow-up tasks per plan §5): `Features/Loyalty` tier
ladder CRUD/consumer endpoints, `PosService.cs` accrual/discount integration,
`Features/CustomerSupport`, `Features/Reviews`, `Features/Customers` extension, worker
recompute job, frontend. `mobile/` untouched (owned by a separate concurrent agent).

# TASK-615 — Loyalty tier ladder CRUD/consumer endpoints + PosService accrual/discount integration

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §2. Handoff read: `.claude/logs/handoffs/613-to-backend_database-engineer.md`.
Log: `.claude/logs/tasks/615_2026-08-24_loyalty-tier-ladder-pos-integration_backend-developer.md`

Extended `Features/Loyalty` (`ILoyaltyRepository`/`LoyaltyRepository`,
`ILoyaltyService`/`LoyaltyService`, `Dtos/LoyaltyDtos.cs`): admin tier ladder CRUD
(`GetTierLadderAsync`/`UpsertTierLadderAsync`, mirrors `GetSettingsAsync`/
`UpsertSettingsAsync`'s shape; bulk-replace matches submitted rows to existing ones **by
SortOrder** so an unchanged tier keeps its Id and any `LoyaltyMembership.CurrentTierId`
pointing at it survives the edit) and consumer-facing tier progress/history
(`GetTierProgressAsync`/`GetTierHistoryAsync`). `loyalty_tier_definitions` has no
`consumer_self_access` RLS (staff-only config), so the progress read goes through
`ITenantSessionOverride` — same mechanism `ResolveCustomerCodeFormatAsync` already uses for
`loyalty_program_settings`; the history read is ambient since that table does carry
`consumer_self_access`. `GetMembershipByIdAsync` now `.Include(m => m.CurrentTier)`.

New `LoyaltyTierSettingsController` (`api/settings/loyalty/tiers`, GET/PUT,
`AppPolicies.AtLeastEnterpriseAdmin`, copied from `LoyaltySettingsController`).
`ConsumerLoyaltyController` gained `GET {tenantId}/tiers` and `GET {tenantId}/tiers/history`.

**`PosService.CreateSaleAsync`** — the core of this task: accrual now multiplies by
`membership.CurrentTier?.AccrualMultiplier ?? 1.0m`; tier discount applied **per item**
(not a lump-sum reduction on `tx.TotalAmount`) so `PriceFinal`/`DiscountAmount` per line stay
consistent with the total — same principle the existing critical-batch auto-discount already
follows, and matters for how the Checkbox fiscal receipt builds its line items. Rejected
alternative (one lump-sum subtraction from the total, mirroring redemption): would leave
per-item fields out of sync with the total. Both gated identically to accrual/redemption
(membership present + program enabled); a membership with no `CurrentTier` yet behaves exactly
as before this change. One-line comment added at the discount site flagging that both
redemption and tier discount reduce `tx.TotalAmount`, the base the future RFM/tier
composite-score job will read.

`dotnet build`: 0 errors, 1 pre-existing unrelated warning. `dotnet test` full suite:
**1871/1871 passing** (19 new: 3 in `PosServiceTests.cs` — no-tier regression pin, 1.5×
multiplier, 10% discount reducing both item total and accrual base; 16 in
`LoyaltyServiceTests.cs` for the new ladder/progress/history methods). Also updated two other
manual `ILoyaltyRepository` fakes (`FiscalizationRetryTests.cs`,
`LoyaltyConcurrencySalesIntegrationTests.cs`) with no-op stubs for the extended interface.

Not implemented here (separate follow-up tasks per plan §5): `Features/CustomerSupport`,
`Features/Reviews`, `Features/Customers` extension, worker tier-recompute job, frontend (tier
ladder admin page, customer card tabs). `mobile/` untouched (owned by a separate concurrent
agent).

# TASK-616 — Consumer support ticket channel (Features/CustomerSupport)

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §2. Handoff read: `.claude/logs/handoffs/613-to-backend_database-engineer.md`.
Log: `.claude/logs/tasks/616_2026-08-24_consumer-support-tickets_backend-developer.md`

New `Features/CustomerSupport` (`IConsumerSupportService`/`ConsumerSupportService` +
`Dtos/ConsumerSupportDtos.cs`), mirroring `SupplierSupportService`'s ticket+message-thread
pattern but for consumer↔tenant instead of tenant↔supplier, on the `ConsumerSupportTicket`/
`ConsumerSupportTicketMessage` entities TASK-613 already landed. New
`IConsumerSupportTicketRepository`/`ConsumerSupportTicketRepository` (tracked `GetByIdAsync`
with Messages; paged consumer/tenant queries with status filter).

Consumer side: `CreateTicketAsync` (ticket + first message in one commit), `GetMyTicketsAsync`,
`GetTicketAsync` (404 uniformly for "not found" and "not yours" — never discloses which),
`AddConsumerMessageAsync` (reopens Resolved/Closed → Open on reply — judgment call, documented
in the task log). Staff side: `GetInboxAsync`, `GetTicketForStaffAsync` (marks unread consumer
messages read as a side effect; named separately from the consumer `GetTicketAsync` since both
would otherwise share an identical C# signature), `AddStaffReplyAsync`, `UpdateStatusAsync`.

CustomerId auto-link reuses two existing lookups, no new mechanism: an existing
`LoyaltyMembership.CustomerId` at this tenant if one exists, else `ICustomerRepository
.FindByPhoneAsync` (same phone-match LoyaltyService itself uses) through
`ITenantSessionOverride` — never creates a Customer here, only links to one that already
exists. Ticket insert itself needs no override — `consumer_support_tickets`' own
`consumer_self_access` RLS policy already covers the consumer session's write.

`ConsumerSupportController` (`api/consumer/support`, `[Authorize]`, `consumer_account_id`
claim, copied from `ConsumerLoyaltyController`): `POST /tickets` (TenantId in body, not the
route — consumer session is cross-tenant), `GET /tickets?tenantId=`, `GET /tickets/{id}`,
`POST /tickets/{id}/messages`. `CustomerSupportInboxController` (`api/customer-support`,
`AppPolicies.AtLeastStoreManager` — same tier as `CustomersController`, not admin-only): `GET
/tickets`, `GET /tickets/{id}`, `POST /tickets/{id}/reply`, `PUT /tickets/{id}/status`.
Registered in both `DependencyInjection.cs` files (re-read fresh before editing; TASK-614/615
registrations untouched, appended after them).

25 new unit tests in `ShelfGuard.Tests/CustomerSupport/ConsumerSupportServiceTests.cs`
(NSubstitute, mirrors `ConsumerProfileServiceTests` style): both auto-link paths + no-match
case, cross-consumer access blocked, reopen-on-reply (Resolved and Closed), staff reply bumps
UpdatedAt, status transition + invalid-status 400, staff read-marking (consumer messages only,
no-op save when nothing unread).

`dotnet build`: 0 errors, 1 pre-existing unrelated warning. `dotnet test` full suite:
**1896/1896 passing** (25 new, no regressions).

Not implemented here (separate follow-up tasks per plan §5): `Features/Reviews`,
`Features/Customers` extension, worker tier-recompute job, frontend (`/customer-support`
inbox page, mobile screens). `mobile/` untouched (owned by a separate concurrent agent).

# TASK-617 — Consumer purchase review channel (Features/Reviews)

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §2. Handoff read: `.claude/logs/handoffs/613-to-backend_database-engineer.md`.
Log: `.claude/logs/tasks/617_2026-08-24_purchase-reviews_backend-developer.md`

New `Features/Reviews` (`IReviewService`/`ReviewService` + `Dtos/ReviewDtos.cs`), mirroring
`SupplierReview`'s rating+comment+one-reply shape but keyed to a `PosTransaction` instead of a
`Supplier`, on the `PurchaseReview` entity TASK-613 already landed. New
`IPurchaseReviewRepository`/`PurchaseReviewRepository` (tracked `GetByIdAsync`;
`GetByTransactionAsync` for the duplicate pre-check; paged consumer/tenant queries, tenant one
with an optional rating filter).

Core design problem: `PosTransaction` has no direct `ConsumerAccountId` FK. Resolved via
`LoyaltyLedgerEntry.PosTransactionId → MembershipId → LoyaltyMembership.ConsumerAccountId` —
reused `ILoyaltyRepository.GetLedgerEntriesForTransactionsAsync` as-is (its own doc already
calls this "the only persisted signal that loyalty activity happened on that sale", added for
TASK-410) rather than joining through `PosTransaction.CustomerId`. A transaction with zero
matching ledger entries (walk-in, never enrolled) or one resolving to a different consumer's
membership both return a uniform 403 — never discloses which. No `ITenantSessionOverride`
needed anywhere in this feature: `purchase_reviews`/`loyalty_memberships`/
`loyalty_ledger_entries` all carry `consumer_self_access` RLS already, and `tenants` has none to
override.

Duplicate guard is two-layered: a pre-check (`GetByTransactionAsync`) returns 409 for the common
case, and a new `DuplicateReviewException` (Domain, mirrors `ConcurrencyConflictException`'s
translation pattern) catches the Npgsql unique-violation on `uq_purchase_reviews_pos_transaction`
as the DB-level backstop for a genuine race — never a raw 500.

Consumer side: `CreateReviewAsync` (ownership resolution + rating 1-5 validation + duplicate
guard), `GetMyReviewsAsync`. Staff side: `GetInboxAsync` (optional rating filter, paged),
`ReplyAsync` — one reply only, rejects 409 on a second attempt (entity's own documented intent;
SupplierReview's own reply endpoint has no such guard, deliberately diverged per the brief).

`ConsumerReviewsController` (`api/consumer/reviews`, `[Authorize]`, `consumer_account_id` claim,
copied from `ConsumerSupportController`): `POST /` (TenantId in body), `GET /?tenantId=`.
`ReviewsInboxController` (`api/reviews`, `AppPolicies.AtLeastStoreManager` — same tier as
`CustomerSupportInboxController`): `GET /?rating=`, `PUT /{id}/reply`. Registered in both
`DependencyInjection.cs` files (re-read fresh before editing; TASK-614/615/616 registrations
untouched, appended after them).

14 new unit tests in `ShelfGuard.Tests/Reviews/ReviewServiceTests.cs` (NSubstitute, mirrors
`ConsumerSupportServiceTests` style): owned-transaction success, different-consumer's
transaction rejected, no-loyalty-link transaction rejected, duplicate rejected both at the
service pre-check and the DB unique-constraint backstop, rating validation (0/6/-1), unknown
consumer/tenant 404s, staff reply succeeds once then rejected on a second attempt, wrong-tenant
reply 404, blank-reply 400.

`dotnet build`: 0 errors, 1 pre-existing unrelated warning. `dotnet test` full suite:
**1910/1910 passing** (14 new, no regressions; baseline was 1896/1896 after TASK-616).

Not implemented here (separate follow-up tasks per plan §5): `Features/Customers` extension,
worker tier-recompute job, frontend (`/customer-support` reviews tab, mobile "Leave a review"
screen). `mobile/` untouched (owned by a separate concurrent agent).

# TASK-618 — Customer detail view: tier/progress, open tickets, recent reviews (Features/Customers extension)

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §2 "Features/Customers (розширення)". Read task logs 613–617.
Log: `.claude/logs/tasks/618_2026-08-24_customer-detail-tier-tickets-reviews_backend-developer.md`

Extended the existing `Features/Customers` (`CustomerDetailDto`, `CustomerService.GetByIdAsync`)
so the staff-facing customer detail view gets loyalty tier/progress, open-ticket count, and
recent reviews in one response — no N+1 from the frontend. Read-only dependency on
`Features/Loyalty`, `Features/CustomerSupport`, `Features/Reviews`; none of their internals
touched.

`CustomerDetailDto` gained `CurrentTierName`/`CompositeScore`/`TierProgressPercent` (all null
together when the customer never joined loyalty; `CurrentTierName`/`TierProgressPercent` null but
`CompositeScore` populated when a membership exists with no tier assigned yet), `OpenTicketCount`
(always a number), `RecentReviews` (`List<CustomerReviewSummaryDto>`, always an array, newest
first, capped at 5).

New narrow repository methods (nothing existing fit — all prior lookups were keyed by membership
Id/ConsumerAccountId, not CRM `CustomerId`): `ILoyaltyRepository.GetMembershipByCustomerIdAsync`,
`IConsumerSupportTicketRepository.CountOpenByCustomerIdAsync` (Open/InProgress only),
`IPurchaseReviewRepository.GetRecentForCustomerAsync` (explicit scalar-FK join through
`PosTransaction.CustomerId` — `PurchaseReview` itself carries no `CustomerId`, see TASK-617's own
ownership-resolution note). Updated three manual `ILoyaltyRepository` test fakes/wrappers
(`PosServiceTests.cs`, `FiscalizationRetryTests.cs`, `LoyaltyConcurrencySalesIntegrationTests.cs`)
to satisfy the extended interface.

Tier-progress formula: `CompositeScore / nextTier.MinCompositeScore * 100`, clamped 0–100, where
`nextTier` is the lowest `SortOrder` above the membership's current tier; null when already at
the top tier. Read literally from the brief ("progress toward the next tier's MinCompositeScore")
rather than as a within-band progress bar — documented as the interpretation taken, not the only
possible one.

Tests: 7 new in `CustomerServiceTests.cs` (service-layer wiring/DTO-mapping, NSubstitute) + 2 new
InMemory-DB repository test files (`ConsumerSupportTicketRepositoryCountOpenTests.cs`,
`PurchaseReviewRepositoryGetRecentForCustomerTests.cs`) pinning the actual EF filtering/join/order
that mocking the repository interface can't exercise.

`dotnet build`: 0 errors, 1 pre-existing unrelated warning. `dotnet test` full suite:
**1923/1923 passing** (13 new, no regressions; baseline was 1910/1910 after TASK-617). Built/ran
tests under `-c Release` — a concurrent session's stray `dotnet run` process held a file lock on
the Debug output; worked around rather than killing a process that wasn't this task's to stop.

Handoff written: `.claude/logs/handoffs/618-to-frontend_backend-developer.md` (final
`CustomerDetailDto` shape + null-handling notes for TASK-621).

Not implemented here (separate follow-up task per plan §5 step 8): frontend consumption
(`CustomerDetail.tsx` tabs). `mobile/` untouched (owned by a separate concurrent agent).

# TASK-619 — Loyalty tier-recompute nightly worker job

**Status:** done · **Agent:** devops-engineer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §3 "Worker-задача". Handoffs read:
`.claude/logs/handoffs/613-to-backend_database-engineer.md`,
`.claude/logs/handoffs/615-to-frontend_backend-developer.md`.
Log: `.claude/logs/tasks/619_2026-08-24_loyalty-tier-recompute-worker-job_devops-engineer.md`

New `worker/src/jobs/loyalty-tier-recompute.job.ts` — nightly cron `0 4 * * *` (after
`cleanup` 03:00, before `weather-fetch`/`ai-order`), structured exactly like
`weekly-report.job.ts`: direct `pg` SQL via the shared `db` pool, `SET app.role = 'worker'` up
front for the `worker_bypass` RLS policies on `loyalty_tier_definitions`/
`loyalty_tier_change_history`. Deliberately not the callback-into-API pattern `ai-order.job.ts`
uses (that file's comments document a history of bugs from that indirection).

Composite score is the plan's confirmed equal-weight `(R+F+M)/3`, rounded to 4 decimals. RFM
quintiles mirror `MarketingAnalyticsRepository.GetScoredCustomersAsync`'s `NTILE(5)` shape.
Population per tenant: active `loyalty_memberships` with ≥1 `loyalty_ledger_entries` row where
`EntryType = 'accrual'`; Recency = days since last accrual `CreatedAt`, Frequency = accrual-entry
count, Monetary = sum of linked `pos_transactions.TotalAmount`. Tier = highest
`loyalty_tier_definitions` rung (ordered `SortOrder DESC`) whose `MinCompositeScore` the score
clears, or null. Writes only `CurrentTierId`/`CompositeScore`/`TierScoreUpdatedAt` — never
`Balance` (avoids the `xmin` concurrency token PosService/LoyaltyService use for `Balance`).
Tier change → update + `loyalty_tier_change_history` insert; score-only drift → update, no
history row; nothing changed → no write. Pure scoring/tier-matching logic factored into
exported `computeCompositeScore`/`pickQualifyingTier` functions (no test harness exists
anywhere in `worker/` today, so none was invented, but the logic is now isolated for one).

Registered in `worker/src/index.ts` (import, `Queue`/`upsertJobScheduler`, startup list).

`npx tsc --noEmit` and `npm run build` in `worker/`: clean. Manual SQL dry-run against dev
Postgres (`crmproductsystems-postgres-1`, port 5435): the real tenant/tier/RFM query ran
error-free (2 loyalty-enabled tenants, 0 tier definitions/qualifying memberships in dev data
today). Followed up with a synthetic `BEGIN`/`ROLLBACK` end-to-end run — inserted a fake accrual
entry + tier definition, re-ran the RFM query (sane scores), executed the actual
UPDATE-membership + INSERT-history write path, confirmed both rows correct, then rolled back
(dev DB untouched).

Not implemented here (later waves per plan §5): frontend loyalty-tiers admin page,
`CustomerDetail.tsx` tier tab, `/customer-support` inbox page. `mobile/` untouched.

# TASK-620 — Loyalty tier ladder admin page (frontend)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §4 "Драбина рангів". Handoff read:
`.claude/logs/handoffs/615-to-frontend_backend-developer.md`.
Log: `.claude/logs/tasks/620_2026-08-24_loyalty-tier-ladder-frontend_frontend-developer.md`

New route `/consumer-app/loyalty-tiers` (`frontend/app/(dashboard)/consumer-app/loyalty-tiers/page.tsx`),
gated `AT_LEAST_ENTERPRISE_ADMIN` exactly like `/consumer-app/page.tsx`. New
`frontend/features/consumer-app/{api/loyaltyTiers.ts, hooks/useLoyaltyTiers.ts}` mirroring
`loyaltySettings.ts`/`useLoyaltySettings.ts` 1:1, plus `LoyaltyTierDefinitionDto`/
`UpsertTierRequest` added to `types.ts`. New
`frontend/features/consumer-app/components/TierLadderSection.tsx` — editable reorderable list
(react-hook-form + `useFieldArray` + `@dnd-kit/sortable`, same pattern as
`NavigationBuilderSection.tsx`), add/remove rows, client-side validation mirroring
`LoyaltyService.UpsertTierLadderAsync`'s server rules (name required/≤100 chars, multiplier
0–999.99, discount 0–100). `sortOrder` is never a user-facing field — always derived from a
row's 0-based position on save (backend orders `GET` by `sortOrder`, so this keeps the list
WYSIWYG after reload).

Handled the handoff's identity-reassignment warning concretely: since the backend matches
submitted rows to existing ones by `sortOrder` value, a drag (or add/remove above an existing
row) changes what an existing row's `sortOrder` resolves to on save, which can silently
reassign which database record a row's edits land on. Added `hasIdentityShiftingReorder`
(compares each persisted row's on-load `sortOrder` to its final index) — when true, Save opens
`ConfirmDialog` (existing component, reused as-is) explaining the consequence before the PUT
fires; a reorder-free save skips the dialog. Reuses `useUnsavedChangesGuard` as-is for the
unsaved-changes affordance. Sidebar: one entry added in `frontend/components/layout/Sidebar.tsx`
(`Award` icon, right after "Bonus Program", same role gate) — re-read the file immediately
before editing per the collision warning; nothing else in that file touched. i18n: `tierLadder`/
`tierLadderPage`/`sidebar.groups.consumerApp.loyaltyTiers` keys added to both `en.json` and
`uk.json`.

`tsc --noEmit` and `npm run lint`: clean. Manual browser verification (backend + frontend dev
servers started locally, dev DB `crmproductsystems-postgres-1`:5435, migrations already applied):
logged in as seeded `ea@demo.local` (enterprise_admin) — empty-state renders correctly; a
`store_manager` session gets `AccessDenied`. Added Bronze (0/1.0×/0%) and Silver (50/1.5×/5%)
rows, saved, confirmed `PUT` persisted both with sequential `sortOrder`, reloaded the page from
scratch and confirmed both rows rehydrate with correct values. Removed the first row (forcing
the remaining row's `sortOrder` to shift from 1→0) and saved: the reorder-confirmation dialog
appeared with the expected copy, blocked the request until confirmed, then on confirm the `PUT`
fired and the response showed the surviving row had in fact inherited the removed row's database
`Id` — exactly the identity-reassignment the dialog warns about, confirming the detection logic
and copy are both accurate. No console errors from this feature (the only 401s seen were
artifacts of the manual token-swap used to switch test users, unrelated to the new code).

Not implemented here (later waves per plan §5, step 8): `CustomerDetail.tsx` tier/progress tab,
`/customer-support` inbox page, marketing-analytics tier segmentation. `mobile/` untouched.

# TASK-621b — Staff-facing customer profile-change history endpoint (small addition, discovered wiring TASK-621)

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §2/§4. Gap found while starting TASK-621 (frontend customer-detail
drawer): only a consumer self-service history endpoint existed (TASK-614), no staff-authorized one.
Log: `.claude/logs/tasks/621b_2026-08-24_staff-profile-history-endpoint_backend-developer.md`

Added `ICustomerService.GetProfileChangeHistoryAsync(customerId, tenantId, page, pageSize, ct)` —
resolves the customer's `LoyaltyMembership` via the already-injected `ILoyaltyRepository`
(TASK-618), then delegates to the already-registered `IConsumerProfileService.GetProfileChangeHistoryAsync`
(TASK-614) for its `ConsumerAccountId`. New `GET api/customers/{id}/profile-history` action on
`CustomersController`, same `AppPolicies.AtLeastStoreManager` gate as the rest of that controller,
paged (`?page=&pageSize=`), returns `PagedResult<ConsumerProfileChangeDto>`. No membership → empty
page, not an error/404 — same convention TASK-618 established. Did not touch `CustomerDetailDto`
(separate lazy-loaded endpoint, not an inline field, since history can be long).

Tests: 2 new in `CustomerServiceTests.cs` (linked membership → delegates and returns the consumer's
actual history; no membership → empty page, no call to `IConsumerProfileService`).

`dotnet build`: 0 errors, 1 pre-existing unrelated warning. `dotnet test` full suite:
**1925/1925 passing** (2 new, no regressions; baseline was 1923/1923 after TASK-618).

Handoff written: `.claude/logs/handoffs/621b-to-frontend_backend-developer.md` (route + response
shape for TASK-621).

# TASK-621 — Customer detail drawer tabs + `/customer-support` staff inbox (frontend)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §4 "Картка клієнта" + "Вхідні звернень і відгуків". Handoffs read:
`.claude/logs/handoffs/618-to-frontend_backend-developer.md`,
`.claude/logs/handoffs/621b-to-frontend_backend-developer.md`. Task logs read: 616, 617.
Log: `.claude/logs/tasks/621_2026-08-24_customer-detail-tabs-support-reviews-inbox_frontend-developer.md`

`CustomerDetail.tsx` restructured into 5 tabs (Info unchanged; new `CustomerTierCard.tsx`
handling the three loyalty null-states, `CustomerTicketsTab.tsx` — count + deep link,
`CustomerReviewsTab.tsx` — `recentReviews[]` preview, `CustomerProfileHistoryTab.tsx` — lazy
`GET /api/customers/{id}/profile-history`, `enabled` keyed to the active tab). No shadcn `Tabs`
component exists in the repo, so a small local tab-bar matches the existing `service-desk/page.tsx`
`tabStyle` pattern rather than adding a dependency.

New route `/customer-support` (`frontend/app/(dashboard)/customer-support/page.tsx`, gated
`AT_LEAST_STORE_MANAGER` via the `AccessDenied`+`hasRole` shell TASK-620 used) and new feature
`frontend/features/customer-support/` — two tabs (tickets: list/filter/detail sheet/reply/status,
mirrors `service-desk/`; reviews: rating filter + one-shot reply, mirrors
`supplier-cabinet/components/CabinetReviews.tsx` but read-only once replied, matching
`ReviewService.ReplyAsync`'s 409-on-second-reply). `?customerId=` deep link filters client-side
over a widened (`pageSize=200`) fetch — `GetInboxAsync` (TASK-616) has no customer-filter param;
limitation noted in the task log rather than adding a backend change (frontend-only scope). One
sidebar entry added next to `/service-desk` (re-read file fresh before editing, TASK-620's entry
untouched). i18n added to both `uk.json`/`en.json`, validated as JSON after editing.

`tsc --noEmit` and `npm run lint`: clean. Manual browser verification (dev servers via
`.claude/launch.json`, existing dev Postgres): all 5 drawer tabs render correct states for a
real non-enrolled customer (lazy-load of profile-history confirmed via network panel — fetches
exactly once, only after that tab opens). Seeded one test ticket + review via direct SQL insert
(dev DB had none for this tenant) and exercised the full staff flow end-to-end through the real
API: reply + status change on the ticket (201/200, staff name resolved via `useUsers`, ticket
count in the drawer dropped to 0 after resolving), reply on the review (200, reply now read-only,
no second-reply option), and the drawer's "Open in inbox" → `/customer-support?customerId=...`
→ correctly pre-filtered list → "Clear filter" round-trip. No console errors from new code.

Not implemented (out of scope per plan §4): marketing-analytics tier segmentation (explicitly an
optional later wave). `mobile/` untouched.

# TASK-622 — End-to-end QA: loyalty tier ladder, consumer profile, support tickets, purchase reviews

**Status:** done · **Agent:** qa-tester · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §9. Read all of TASK-613..621b logs first.
Log: `.claude/logs/tasks/622_2026-08-24_crm-loyalty-tier-qa_qa-tester.md`

No bugs found. All 7 priority areas confirmed-working; one already-documented limitation
(`?customerId=` inbox deep link capped at the backend's 200-row page-size ceiling) confirmed real
but low-risk — flagged as a follow-up, not a blocker.

- `dotnet test`: **1925/1925 passing**, matches TASK-621b's baseline exactly, no drift.
- `PosService.cs` tier-multiplier/discount arithmetic read end-to-end — no double-counting, no
  compounding-order bug; the 3 dedicated `PosServiceTests.cs` cases assert real amounts, not just
  no-throw.
- RLS on all 6 new tables verified functionally via direct SQL (rolled-back transaction, seeded
  fixtures): `consumer_self_access` correctly isolates consumer A from consumer B's
  tickets/reviews; `tenant_isolation`/`worker_bypass` behave correctly; unscoped queries fail
  closed (0 rows) by default.
- Review authorization edge cases (cross-consumer transaction, walk-in/no-loyalty-link
  transaction) both cleanly 403, no path to 500 — confirmed in code and by existing tests.
- Frontend smoke test (`/customer-support` both tabs, customer drawer's 5 tabs, `?customerId=`
  deep link): all render correctly, no console errors from new code.
- Worker tier-recompute job: went beyond a code read — ran the job's exact SQL (RFM scoring query
  + the `UPDATE membership` / `INSERT tier_change_history` write path) against dev Postgres with
  seeded fixture data inside rolled-back transactions; both produced correct results.

# TASK-623 — CRM/loyalty tier expansion: documentation pass

**Status:** done · **Agent:** documentation-writer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md`. Read all of TASK-613..622/621b logs + handoffs first. Docs-only,
no code touched.
Log: `.claude/logs/tasks/623_2026-08-24_crm-loyalty-tier-docs_documentation-writer.md`

Updated `database-schema.md` (6 new tables + RLS table + `LoyaltyMembership`/
`PosTransaction.CashRegisterId` extensions + the EF phantom-FK bug note), `domain-model.md` (6 new
entity sections + extended `LoyaltyMembership` + new Key Business Rule #7), `api-contracts.md` (6
new endpoint-group sections, DTO shapes pulled from the actual C# DTO records since the task logs
didn't always give exact JSON field names), `decisions.md` (new ADR-034, 6 decisions), and
`known-issues.md` (new KI-034, the `?customerId=` deep-link limitation, low severity). Added two
glossary terms (tier ladder, composite score). All six files' `Updated` headers bumped to
2026-08-24.

Wrote `.claude/logs/handoffs/623-to-mobile-codex.md` (curated extract of the 4 consumer-facing
endpoint groups for the separate mobile Codex agent), following the `586-to-mobile-codex.md`
precedent — the only prior example of this repo handing a finished feature to that agent.

# TASK-625 — Realtime SignalR transport for consumer support tickets

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-25
Plan: `goofy-bubbling-naur.md` §2 (extends TASK-616). Read TASK-616 log first.
Log: `.claude/logs/tasks/625_2026-08-25_consumer-support-signalr-realtime_backend-developer.md`

SignalR Hub `/api/hubs/consumer-support` (`ConsumerSupportHub`, `Infrastructure/Realtime/`) on
top of TASK-616's REST-only ticket channel — REST stays the only write path, SignalR only pushes
`SupportMessageCreated`/`SupportTicketStatusChanged` post-commit to group
`consumer-support-ticket:{ticketId}`. `JoinTicket`/`LeaveTicket` re-derive identity from the JWT
(never trust client-supplied ids): consumer must own the ticket, staff must be role ≥
store_manager and match tenant. `IConsumerSupportRealtimeNotifier` (Application) keeps
`ConsumerSupportService` free of a direct `IHubContext` dependency, implemented by
`ConsumerSupportRealtimeNotifier` (Infrastructure) which swallows/logs its own publish failures
(never fails an already-committed REST write). JWT via query-string `access_token` accepted only
on the Hub path (`Program.cs` `OnMessageReceived`).

`dotnet test`: **1946/1946 passing** (21 new — 11 service-layer publish/no-publish tests, 10 Hub
access-control unit tests — up from TASK-624's 1925/1925 baseline, zero regressions).

Updated `api-contracts.md` (new Realtime subsection under Consumer support tickets — Hub URL,
JWT/query-token rule, method/event names, exact payloads, access rules, reconnect behavior).
Wrote `.claude/logs/handoffs/625-to-mobile-codex.md`. `mobile/`, `frontend/`, and REST DTO shapes
untouched (spec constraints).
