# TASK-576 — QA: Catalog curation regression pass

**Status:** done · **Agent:** qa-tester

Full brief: `.claude/logs/tasks/570_2026-08-19_catalog-curation-architecture_project-architect.md`
section "### TASK-576". ADR-032. Verified live against local dev (`backend-dev`/`frontend-dev`,
`ea@demo.local` enterprise_admin, tenant "Свіжий Кут", 200-item/50-active-in-default-page catalog —
enough SKUs to construct real outside-window cases without seeding throwaway data).

## Result: clean pass, no bugs

## What was verified

**Picker (Product Carousel, `/consumer-app/pages` Catalog page):** typed search "Кабачки" —
network tab confirmed `GET /api/items?search=Кабачки` (not a client filter), returned exactly the
1 matching item, which sits at alphabetical position ~55 (outside the picker's default browse
window and outside admin's default `pageSize=50`). Selected it — live preview updated instantly to
show only that item, proving the by-ids fetch (`GET /api/items?ids=...`) resolves a pick outside
the default window, not just search. Selected 19 more via the default browse list up to
`MaxItems=20` (productCarousel) — search input correctly disappeared at the cap; removed 1 chip,
count/search-input reappeared correctly. Repeated the cap check on a fresh Product Grid block up to
`MaxItems=30` — correct.

**Empty selection = unchanged fallback:** verified on the pre-existing Product Carousel block
before any curation, and on a freshly-added Product Grid block — both rendered the standard
alphabetical-first-`limit` list with the documented hint text ("With nothing selected, the first
products alphabetically are shown").

**Chosen-order + `limit` cap:** with 19 items curated (deliberately starting with the out-of-order
pick, "Кабачки вагові", then 18 more in browse/alphabetical order) and `limit=10`, the live preview
showed exactly 10 items, chosen-order-first (Кабачки vagovi first despite being alphabetically much
later) — not alphabetical, confirming `limit` caps the curated list rather than driving pagination.

**Outside-window regression (the core bug this feature exists to avoid reintroducing):**
- Web preview: confirmed above (Кабачки вагові, alphabetical position ~55, outside `/api/items`
  default `pageSize=50`) resolved correctly via the new `ids` fetch.
- Mobile/consumer side: `PageRenderer.tsx`/`resolveBlocks.ts` already had 30/30 unit coverage from
  TASK-573 for this exact logic; to prove the actual HTTP contract end-to-end (not just the unit
  fake), called `GET /api/consumer/{tenantId}/catalog/by-ids?storeId=&ids=<Кабачки's id>` directly
  (after temporarily enabling the tenant's `catalog` consumer feature flag, which this local dev
  tenant had off for all 8 flags pre-existing — restored to off afterward, see Notes) — returned
  the item correctly, confirming the same bounded by-ids read path mobile's `PageRenderer.tsx`
  calls works correctly outside the first-30-alphabetical window.

**Deactivation:** deactivated a curated, in-limit-window item ("Банан ваговий") via
`/inventory`'s Edit product → unchecked "Active product" → Save. Reloaded the App Builder preview:
the item silently disappeared from the rendered list (remaining items shifted up, count now 9 of
10), no broken card, no placeholder, no console error, all network requests 200/204. Confirmed
server-side too: the consumer `catalog/by-ids` endpoint (`IsActive` filter) excludes it entirely,
while the admin `/api/items?ids=` endpoint still returns it (by design — admin catalog browse) with
`isActive:false`, correctly filtered client-side by `AppPreviewPanel.tsx`'s `toPreviewProductItem`.

**Full publish loop (ADR-031/032 "preview must never lie"):** Saved draft → Publish → Confirm →
fetched `GET /api/v1/mobile/config?tenantId=...` directly. The published Catalog page's
`productCarousel` block has `productIds` = the exact 19 curated ids, in the exact chosen order,
`limit: 10` — byte-identical to what the web preview showed pre-publish. The `productGrid` block
has `productIds: []`, `limit: 12` — unaffected, confirming the untouched fallback block round-trips
correctly too.

**`promotionGrid`/`promotionCarousel` unaffected:** added both to the Catalog page, opened their
Property Editors — neither has a "Product Ids" field (only Title/Limit/Columns, or Title/Limit/
Show View All/Card Width Px for the carousel). Confirms TASK-571's registry change is scoped to the
two product block types only. Test blocks removed after verification.

**Regression spot-checks (TASK-539/540/541, TASK-560-569):**
- Device picker: switching to iPhone 15 Pro Max correctly resized the preview frame to 430×932.
- Show/hide preview toggle: correctly hides/restores the preview column.
- Interactive bottom nav (TASK-568): clicking "Профіль" in the mockup switched the live preview to
  a non-editable-tab notice; "Головна" returned to the edited page — editor's own left-panel page
  selection stayed independent, as designed.
- Dirty-guard: edited Hero Banner's title without saving, switched Home→Catalog→Home in-app (no
  data loss, edit persisted across tab switches — correct, since all pages share one draft), then
  attempted to navigate to `/dashboard` — `window.confirm("You have unsaved changes. Leave this
  page without saving?")` fired and, when cancelled, navigation was correctly blocked
  (`location.pathname` stayed `/consumer-app/pages`). Edit reverted and draft re-saved clean
  afterward.
- Drag-and-drop reorder: not independently re-driven this session — this pane's synthetic
  pointer/keyboard events don't reliably trigger dnd-kit's sensors (same limitation TASK-575's own
  implementer noted for block-palette clicks). Mitigated by code inspection instead:
  `git diff --stat` confirms `AppBuilderCanvas.tsx` (owns the sortable list) has **zero** changes
  in this feature's diff — no regression surface was touched.
- Resize handles: confirmed present and correctly reflecting current value (Hero Banner's drag
  handle title read "Висота: 225px", matching its `heightPx` prop). `blockPreviews.tsx`'s diff is
  scoped to the new `catalogById`/`resolveProductItems` addition only — the resize-handle rendering
  code is untouched.

**Static verification:** `npx tsc --noEmit` (frontend) clean. `dotnet test --filter
"FullyQualifiedName~MobileConfig|FullyQualifiedName~Catalog|FullyQualifiedName~ConsumerContent"` —
307/307 pass. `git diff --stat` confirms the full changeset is exactly the 26 files described
across TASK-571-575's logs, nothing else touched.

## Notes

- A build-time error in the console log history (`previewBlocks` defined multiple times,
  `ProductPickerField` module-not-found) is **stale** — it's from an earlier point in the
  implementing agent's own dev session, before the file reached its final state. Confirmed current
  `AppPreviewPanel.tsx` has a single `previewBlocks` declaration, the page renders and functions
  correctly, and all live network requests during this QA pass returned 200/204. Not a live bug.
- This local dev tenant had all 8 consumer feature flags (`loyalty`/`promotions`/`catalog`/etc.)
  published as `false` — pre-existing environment state, unrelated to this feature (the existing
  sibling `GetCatalog` endpoint 403s identically). Temporarily flipped `catalog` on to prove the new
  `catalog/by-ids` endpoint's live behavior, then flipped it back off and republished to leave the
  environment exactly as found.
- Left the Catalog page's App Builder draft/published state with the curated Product Carousel (19
  items, "Банан ваговий" now deactivated) and empty-selection Product Grid — this is real
  QA-exercised state, not accidental leftover; deactivated "Банан ваговий" back to active if a
  future session needs the original 200-active-item baseline restored (not done here since the
  deactivation was itself the requirement-5 test subject, not incidental test debris).
