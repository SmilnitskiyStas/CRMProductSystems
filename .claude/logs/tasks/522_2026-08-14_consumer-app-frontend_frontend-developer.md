# TASK-522: Consumer App admin frontend — banners, promo products, catalog card

**Agent:** frontend-developer
**Date:** 2026-08-14
**Status:** done — built, tsc/lint clean, all three sections verified end-to-end against the
real dev backend (create/edit/delete/analytics/image-upload for banners; add/cancel for promo
products; live count for the catalog card). No blocker.

## Context

Final frontend slice of the Consumer App plan (`quirky-questing-hoare.md`), blocked on TASK-520
(schema, done) and TASK-521 (backend API, done). Adds three sections to the existing
`/consumer-app` admin page, under `BonusProgramSection`, per the plan and TASK-521's exact DTO
contracts.

## Done

- `frontend/features/consumer-app/types.ts` — appended `BannerDto`/`CreateBannerRequest`/
  `UpdateBannerRequest`/`BannerAnalyticsDto` (mirroring `BannerDtos.cs` field-for-field) and
  `DiscountDto`/`CreateDiscountRequest` (mirroring the pre-existing `DiscountDtos.cs` — no
  frontend feature called `/api/discounts` before this task, so this feature now owns that
  client since promo products is its only consumer).
- `frontend/features/consumer-app/api/banners.ts` (new) — `bannersApi` wrapping all 6
  `BannersController` endpoints, including `postForm` multipart image upload.
- `frontend/features/consumer-app/api/discounts.ts` (new) — thin client for the pre-existing
  `DiscountsController` (get/create/approve/cancel), reused as-is per the plan (no backend
  changes).
- `frontend/features/consumer-app/hooks/useBanners.ts`, `usePromoProducts.ts` (new) — React
  Query hooks. `useCreatePromoProduct` chains `create` → `approve` in one mutation (POST then
  immediately PUT approve) so "+ Add product to promo" never leaves an item in `pending` —
  there's no separate approval UI anywhere else in the product.
- `frontend/features/consumer-app/components/BannersSection.tsx` + `BannerForm.tsx` (new) —
  list (badge from `isCurrentlyActive`, store count, date range, views/clicks) + create/edit
  modal (title/eyebrow/description/body/terms, image upload with preview, icon+color pickers,
  internal/external detail-mode toggle, `LocationsMultiSelectDropdown` reused as-is for stores,
  a plain search+checkbox list over `useCatalogProducts()` for products, date range) + per-row
  analytics popover (`GET /{id}/analytics`) + soft-delete with confirm.
- `frontend/features/consumer-app/components/PromoProductsSection.tsx` (new) — store selector
  (defaults to the first active location) + list of that store's active discounts joined
  client-side against the catalog for name/price + inline add-form + "Remove from promo"
  (`PUT .../cancel`).
- `frontend/features/consumer-app/components/CatalogSection.tsx` (new) — read-only status card
  (active product count + link). No CRUD, per the plan.
- `frontend/app/(dashboard)/consumer-app/page.tsx` — wired all three under `BonusProgramSection`
  in the existing vertical stack.
- `frontend/messages/uk.json` / `en.json` — added `banners` (incl. nested `form`),
  `promoProducts`, `catalog` blocks as siblings of `bonusProgram` under `Dashboard.consumerApp`.

## Bugs found and fixed during verification (both genuine, not test-only issues)

1. **Date fields sent to `timestamp with time zone` columns crashed the API (500).** A native
   `<input type="date">` gives `"YYYY-MM-DD"` with no timezone; System.Text.Json parses that as
   `DateTime.Kind=Unspecified`, and Npgsql rejects writing that to a `timestamptz` column
   (`Banner.ValidFrom/ValidUntil`, `Discount.ValidUntil` are all `timestamp with time zone` —
   confirmed via the migration files, unlike `DemandEvent.StartsAt/EndsAt` which are `DateOnly`
   and don't have this problem). Fixed by pinning both `BannerForm.tsx` and
   `PromoProductsSection.tsx` date submissions to UTC midnight (`${value}T00:00:00.000Z`) before
   sending. Confirmed via a live `POST /api/banners` that failed with the Npgsql `Kind=Unspecified`
   exception before the fix and returned `201` after.
2. **Discount-percent input had a step-mismatch that silently blocked native form submission.**
   `min={0.01} step="0.1"` with a default value of `"10"` fails HTML5 step-value validation
   (`(10 - 0.01) / 0.1` isn't an integer), which makes the browser cancel form submission before
   dispatching any `submit` event — no visible error, no network call, easy to miss. Fixed to
   `min={0}` (matching the `min={0}`/`step="0.1"` convention already used for percent fields in
   `BonusProgramSection.tsx`). Server-side `discountPercent <= 0` rejection in
   `DiscountService.CreateAsync` is unaffected — `min={0}` here is purely for step alignment, not
   a relaxed validation boundary.
3. **Plan's `/catalog` link target doesn't exist** — there is no `/catalog` route in this app;
   the actual product-catalog CRUD page is `/inventory` (`ProductForm.tsx`/`ProductsTable.tsx`
   live in `features/inventory`, not `features/catalog`, which is a read-only wrapper over
   `/api/items`). `CatalogSection.tsx` links to `/inventory` instead, with a comment explaining
   the deviation.

## Verification

- `npx tsc --noEmit` — clean.
- `npm run lint` (`next lint`) — clean, no warnings.
- Live browser verification (Chrome preview, logged in as `ea@demo.local` / enterprise_admin,
  dev backend on `:5000` + dev Postgres via `crmproductsystems-postgres-1`):
  - Created a banner with title/description/body(2 paragraphs)/terms(2 lines), 2 stores, 2
    products, both date fields (`validFrom`=today, `validUntil`=+47 days) → `201 Created`,
    correct `locationIds`/`productIds`/dates in the response, appeared in the list with
    `ACTIVE` badge, "2 stores", correct date range, "0 views"/"0 clicks".
  - Uploaded a real PNG to `POST /api/banners/{id}/image` → `200`, file confirmed written to
    `wwwroot/uploads/banners/{id}.png` on disk; re-opened the Edit form and confirmed the image
    preview (`<img src="/uploads/banners/{id}.png">`) and every other field (title, description,
    2 checked products, both dates, `detailMode`) pre-filled correctly from `GET /{id}`.
  - Opened the Analytics popover → `GET /{id}/analytics` → `{viewCount:0, clickCount:0, ctr:0}`,
    rendered as "0 Views / 0 Clicks / 0.0% CTR" — matches "expect 0/0 right after creation" from
    the brief.
  - Promo Products: added a product to a store (`POST /api/discounts` reason=promo → `201`
    pending, then `PUT .../approve` → `200` active, both fired from the real "+ Add product to
    promo" submit), confirmed it appeared in the list with resolved product name and
    `priceOriginal → priceDiscounted`; "Remove from promo" → `PUT .../cancel` → `200`, item
    disappeared from the list.
  - Catalog card showed "50 active products in the catalog" (real count from `useCatalogProducts()`).
  - Deleted (soft) the test banner via the UI's Delete button is equivalent to what a real admin
    would do; for this session's dev-DB residue I instead hard-deleted the test banner + its
    join rows directly via `psql` (same cleanup precedent as TASK-521's log) so the dev DB is
    left with zero leftover rows, and removed the local test PNG file.
- **Screenshot not captured** — the Browser pane in this environment never composited frames
  this session (`computer{action:"screenshot"}` timed out every attempt, "the Browser pane is
  not displayed"), a tooling/environment limitation, not an application issue. All verification
  above was done via the accessibility tree (`read_page`/`get_page_text`), live network request
  inspection, and direct DB checks instead.
- **Automation quirk, not an app bug:** in this same session, `mcp__Claude_Browser__computer
  left_click` (ref-based) intermittently failed to deliver a working click to two specific
  submit/plain buttons inside `PromoProductsSection`'s inline add-form (no click/submit event
  observed at all, even with capturing listeners) while working reliably everywhere else on the
  same page (banner create/edit, checkboxes, dropdowns). Switching to a direct `element.click()`
  call resolved it immediately and consistently, and the resulting network calls matched the
  code exactly — noted here in case this recurs for a future agent so it isn't mistaken for an
  application defect.

## Deviations from the plan / judgment calls

- No `SortOrder` field in `BannerForm.tsx` — the plan's own frontend field list for the banner
  form (section 1) doesn't mention it, only the backend DTO does; server defaults to `0`.
  Skipped per "don't over-build" guidance in the brief.
- `CatalogSection` links to `/inventory`, not `/catalog` — see bug #3 above.
- Store-scoped fields (`PromoProductsSection`'s store selector) reuse `useLocations()` from
  `frontend/features/locations/hooks`, the same hook `InviteUserModal`/`UserLocationsEditor`
  already use — confirmed `useStores` in `features/stores/hooks/useStores.ts` is just a
  backward-compat re-export of the same hook.

## Not in scope (per brief)

- No backend changes.
- No `mobile/` changes — separate task, tracked via
  `.claude/logs/handoffs/521-to-mobile-developer_consumer-content-api.md` (already written by
  TASK-521).
- `.claude/docs/` not updated — consistent with TASK-520/521 precedent (deferred to a
  documentation-writer pass once the full 3-task feature ships).

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `frontend/features/consumer-app/types.ts` (Banner/Discount types appended)
- `frontend/features/consumer-app/api/banners.ts` (new)
- `frontend/features/consumer-app/api/discounts.ts` (new)
- `frontend/features/consumer-app/hooks/useBanners.ts` (new)
- `frontend/features/consumer-app/hooks/usePromoProducts.ts` (new)
- `frontend/features/consumer-app/components/BannersSection.tsx` (new)
- `frontend/features/consumer-app/components/BannerForm.tsx` (new)
- `frontend/features/consumer-app/components/PromoProductsSection.tsx` (new)
- `frontend/features/consumer-app/components/CatalogSection.tsx` (new)
- `frontend/app/(dashboard)/consumer-app/page.tsx` (3 sections wired in)
- `frontend/messages/uk.json`, `frontend/messages/en.json` (new i18n blocks)
