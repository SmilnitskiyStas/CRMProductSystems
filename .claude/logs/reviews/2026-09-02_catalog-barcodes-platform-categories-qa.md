# QA regression pass — catalog barcodes + global platform categories

**Date:** 2026-09-02 · **Agent:** qa-tester · **Target:** HEAD `743a4f9c` on `main`
**Scope:** TASK-675 (barcodes) · TASK-676 (migration) · TASK-677 (backend) · TASK-678 (frontend)
Plan: `.claude/plans/1-giggly-catmull.md`

## Verdict: SHIP-WITH-FIXES

One MEDIUM interaction bug (BUG-1) and one LOW cosmetic (BUG-2), both in the same area:
the new `ItemService` `CategoryId` validation vs. a provider soft-deleting an in-use
category. Neither is data loss / security / crash. Everything else — migration, RLS,
barcodes, provider CRUD, subtree/uncategorized filters, i18n, all automated checks —
passes clean.

## Automated checks

| Check | Result |
|---|---|
| `dotnet build backend/ShelfGuard.sln` | **0 errors**, 1 warning (pre-existing, MarketplaceServiceTests) |
| Full `dotnet test` | **2200 / 2200 passed, 0 skipped** — exact baseline match |
| `dotnet ef migrations has-pending-model-changes` | none |
| Migration Down/Up round-trip (scratch DB `crm_qa_rt`) | **clean** — Up→Down→Up, no errors. Down recreates `categories` + RLS triad (`tenant_isolation` w/ NULLIF guard, `provider_bypass` incl. `provider_admin`, `worker_bypass`), FKs swap back (items/product_segments SET NULL, weather_coefficients CASCADE — original). Up leaves `platform_categories` RLS-off, `items`/`product_segments`/`weather_coefficients` FORCE RLS restored, all 3 FKs SET NULL. |
| `frontend` `npx tsc --noEmit` | clean |
| `frontend` `npm run lint` | clean (no warnings/errors) |
| `frontend` `npm run build` | exit 0, clean (`/provider/categories` 7.35 kB, `/inventory` present) |
| i18n `uk.json` vs `en.json` | **4710 / 4710 keys, 0 only-in-one, 0 duplicates**. All new keys present w/ ICU plurals. |
| `worker/` `tsc` | clean |
| `mobile/` `tsc` | clean (incl. the other session's uncommitted `receiptPrinting.ts`) |

## RLS / tenant isolation (critical area) — PASS

- `platform_categories` genuinely global: `relrowsecurity = f`, `relforcerowsecurity = f`
  in the dev DB; `CategoryRepository` / `CategoryService` carry **zero** tenant filter on
  the table (only an in-memory `BusinessType` narrowing). Provider `GET /api/provider/categories`
  and tenant `GET /api/categories` both return the same 86 rows.
- RLS-audit test `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass`
  still green — confirmed the reason: its query filters `relrowsecurity = true AND
  relforcerowsecurity = true`, so a no-FORCE-RLS `platform_categories` is out of scope by
  design, not by a gap.
- `items` / `product_segments` / `weather_coefficients` still `relforcerowsecurity = true`
  after the migration (verified live).
- `ItemRepository.ApplyCategoryFilterAsync` reads the whole `platform_categories` (Id,ParentId)
  set for subtree closure — global table, no RLS concern — then filters `items` through the
  normal RLS-scoped query. A supplier-type tenant (`alpha@supplier.local`) is 403 on
  `/api/categories` and `/api/items` (role, not RLS) — no cross-tenant item leak via any
  category path observed.

## Category consumers

| Consumer | Result |
|---|---|
| Mobile `GET /api/items/by-barcode/{code}` | PASS — resolves item, returns `categoryId` + `categoryName` from `platform_categories` |
| `AudienceBuilder` category typeahead (`GET …/audience-builder/categories`) | PASS — returns categories + per-tenant `itemCount`; `?search=` filters |
| Analytics `by-category` / drill-down | **NOT E2E-verified** — the `analytics` module is not active for the demo tenant (TASK-674 breaking change) and enabling it was blocked. Code review clean (`_db.Categories`→`_db.PlatformCategories` one-liner at `AnalyticsRepository.cs:~390`; filter still `s.Product.CategoryId == id` exact-match, documented). Covered by passing analytics integration tests in the 2200. |
| POS loyalty bonus exclusion (`PosService`) | **NOT E2E-verified** (no quick full-shift path). Code review: `item.Product.CategoryId` + `ExcludedCategoryIdsJson` semantics unchanged; ids now point at `platform_categories`. Covered by passing PosServiceTests. |
| WriteOff / Transfer / Receipt / Stock `category_id` filter | route through the same `ItemRepository` path verified below (flat category ⇒ direct items; dev data is flat so no behaviour change). |

## Barcodes (TASK-675) — PASS

- Create with `["  111  ","111","","222","   "]` → stored `["111","222"]` (trim, dedupe,
  drop blanks, order preserved).
- "Set as primary" → `PUT` `["222","111"]` → re-fetch returns `["222","111"]`, persists as
  `Barcodes[0]`.
- `GET /api/items/by-barcode/111` (non-primary) still resolves.
- Table cell: single barcode → no pill; 3 barcodes → `3333333333333` + `+2` pill; click
  opens portal popover "All barcodes / ★ 3333333333333 Primary / 1111111111111 / 2222222222222".
- No console errors on `/inventory`, `/inventory/[id]`, `/provider/categories` (fresh server).

## Provider category CRUD edge cases (TASK-677) — PASS (11/11)

POST root → 201 · POST child → 201 · POST bad business type → 400 · POST as store_manager →
403 · PUT self-parent → 400 · PUT ancestor-under-descendant (cycle) → 400 · DELETE parent
w/ active child → 400 · DELETE leaf → 204 + row stays `IsActive=false` · DELETE unknown → 404
· PUT unknown → 404 · DELETE now-childless parent → 204. Retag `businessTypes:["auto_service"]`
→ category drops out of retail `GET /api/categories`, item keeps its FK. `ItemCount` reflects
reality (assign item → grandchild shows 1; exact-match not subtree, as designed).
Browser: `/provider/categories` tree renders w/ itemCount badges + business-type chips,
expand/collapse works, create/add-sub (parent preset)/edit/soft-delete modals all work,
"active sub-categories" 400 surfaced as toast, non-provider → redirect to `/dashboard`.

## uncategorized + subtree filter — PASS

- `GET /api/items?uncategorized=true` → 20 (== DB `CategoryId IS NULL` for the tenant at
  test time); UI "Uncategorized" filter → 18 after test-data cleanup (== DB). Match.
- 3-level tree, item on the grandchild: filter by parent / mid / leaf all return it (1);
  filter by an unrelated category returns 0.

---

## BUG-1 — Item edits blocked after a provider soft-deletes an in-use category

**Severity:** medium · **Task:** TASK-677 · **Area:** `ItemService.UpdateAsync` × `ProviderCategoryService.DeleteAsync`

**Steps**
1. Provider creates category C, a tenant assigns item I to C.
2. Provider soft-deletes C (`DELETE /api/provider/categories/{C}` → 204, row kept
   `IsActive=false` — the documented "items keep their FK" behaviour).
3. Tenant user opens I in the product form, changes an unrelated field (e.g. name), saves.

**Expected:** the edit saves — I keeps its (now-inactive) category link, matching the
soft-delete design intent that items keep working.

**Actual:** `PUT /api/items/{I}` → **400 `{"error":"Category not found or inactive."}`**;
UI shows a toast "Category not found or inactive." and the edit is rejected. The form's
category `<select>` renders **blank** (`value=""`, `selectedIndex=-1`) with no hint that it
is the blocker — it looks identical to a normal "no category" item. React-hook-form still
submits the stale `categoryId` from its `reset()` state (the missing `<option>` doesn't
clear RHF's internal value), and the new `_categoryRepo.ActiveExistsAsync` guard in
`UpdateAsync` rejects it. The user must notice the empty dropdown and explicitly pick a
category (or "— no category —") to save anything.

**Regression:** before this commit `UpdateAsync` did not validate `CategoryId`, so this
edit succeeded. Recoverable (reassign category), no data loss.

**Suggested fix (main session's call):** in `ItemService.UpdateAsync`, only validate
`CategoryId` when it actually changed — `if (request.CategoryId is Guid id && id !=
product.CategoryId && !await _categoryRepo.ActiveExistsAsync(id, ct))` — so an unchanged
stale FK passes through, consistent with the soft-delete intent. (Optionally also: in
`ProductForm`, inject a disabled synthetic `<option>` for an out-of-list current category
so the user can see it.)

## BUG-2 — Product form category dropdown blank when the item's category is hidden from the tenant

**Severity:** low (cosmetic) · **Task:** TASK-678 · **Area:** `frontend/features/inventory/components/ProductForm.tsx`

**Steps:** provider retags category C to a business type the tenant doesn't have (C stays
active); tenant user opens an item assigned to C in the edit form.

**Expected:** the form shows the item's current category (even if not otherwise selectable).

**Actual:** the `<select>` is blank (`selectedIndex=-1`) — the item's category name is not
shown anywhere in the form, though the catalog table column still shows it correctly.
Saving **does** preserve the link (RHF resubmits the real id, backend accepts it because C
is still active), so this is display-only. Same missing-`<option>` root cause as BUG-1.
Fixing BUG-1's form half (synthetic disabled option) covers this too.

---

## Not filed (accepted / pre-existing / environmental)

- Analytics category filter exact-match not subtree — documented, intended.
- `/api/analytics/by-category` not E2E-tested — `analytics` module inactive for the demo
  tenant (TASK-674), enabling it was blocked. Integration tests cover it.
- POS loyalty exclusion not E2E-tested — covered by unit tests + code review.
- `ProductForm` `ITEM_TYPE_VALUES` omits `"packaging"` (backend allows it); form
  `managementType` only exposes MTS/MTO (backend allows NA/NM) — both pre-existing, not
  touched by this commit.
- Dev DB leftovers `ZZ Штрихкод Тест` (TASK-675 dev), `QA Test NullPrice Item`
  (2026-08-23) — pre-existing test pollution, not from this pass. All of my QA test data
  was cleaned up (DB back to 86 platform_categories / 224 items).
- `openapi.json` not regenerated — deferred by the user.

## Environment notes

- Stopped/restarted `backend-dev` and `frontend-dev` preview servers (DLL locks for the
  build; `.next` rebuild for the clean `npm run build`). Both running again on :5000/:3001.
- The stale `seed` browser tab shows buffered `MISSING_MESSAGE` console errors from before
  the `.next` rebuild — **not real**; a fresh tab on the rebuilt server has zero console
  errors and every new i18n key resolves.
- Scratch DB `crm_qa_rt` created for the migration round-trip and dropped afterwards.
