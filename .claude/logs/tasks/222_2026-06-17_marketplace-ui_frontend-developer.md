# TASK-222 — Frontend: Supplier Marketplace UI

**Agent:** frontend-developer
**Date:** 2026-06-17
**Status:** done
**Depends on:** TASK-221 (Marketplace API) ✅

---

## Summary

Implemented the full Supplier Marketplace UI across three areas:
1. `/marketplace` — supplier grid with filters + search
2. `/marketplace/[id]` — supplier profile page with tabs
3. Settings → "Профіль маркетплейсу" tab

---

## Files Created

### Feature folder `frontend/features/marketplace/`
- `types.ts` — TS interfaces matching TASK-221 API DTOs
- `api/marketplace-api.ts` — all API calls via `api` from `@/lib/api`
- `hooks/useMarketplace.ts` — React Query hooks for all queries + mutations
- `components/StarRating.tsx` — inline SVG star rating (static + interactive)
- `components/PlanBadge.tsx` — Free / Premium badge
- `components/SupplierCard.tsx` — card for grid view
- `components/SupplierFilters.tsx` — region text input + category dropdown + plan toggle
- `components/SupplierMetrics.tsx` — 6-metric grid (rating, delivery, accuracy, quality, response, cancellation)
- `components/SupplierItemsTab.tsx` — supplier catalog table with availability badge
- `components/SupplierReviewsTab.tsx` — reviews list + "Залишити відгук" button
- `components/ReviewModal.tsx` — modal with star picker + textarea; handles 409 as "Ви вже залишили відгук"
- `components/SupplierProfileForm.tsx` — self-management form with TagInput, isPublic toggle, plan selector

### Pages
- `frontend/app/(dashboard)/marketplace/page.tsx` — grid + filters + search + pagination
- `frontend/app/(dashboard)/marketplace/[id]/page.tsx` — profile header + metrics + catalog/reviews tabs

### Settings integration
- `frontend/features/settings/components/MarketplaceProfileTab.tsx` — thin wrapper
- Updated `frontend/app/(dashboard)/settings/page.tsx` — added "Профіль маркетплейсу" tab (visible only when `marketplace` module active)

### Sidebar
- Updated `frontend/components/layout/Sidebar.tsx` — added `marketplace` group (module-gated on `"marketplace"`) with `/marketplace` link

---

## Acceptance Criteria

- [x] `tsc --noEmit` — green (no TypeScript errors)
- [x] `next build` — green (28 routes, all compiled)
- [x] `/marketplace` loads supplier grid with working filters and search
- [x] `/marketplace/[id]` shows profile with tabs (catalog + reviews); review modal posts and handles 409
- [x] Settings tab "Профіль маркетплейсу" loads and saves (visible only when marketplace module active)
- [x] Sidebar "Маркетплейс" link visible only when `marketplace` module is active
- [x] Task log created
- [x] TASK-222 marked `done` in backlog

---

## Key Design Decisions

- **No new npm packages**: star rating implemented as inline SVG polygon; no external library needed.
- **Module gating**: `/marketplace` page checks `useModules()` and shows a locked state if module inactive. Sidebar group uses `moduleKey: "marketplace"`. Settings tab filtered by `marketplaceActive`.
- **Public endpoints**: `marketplaceApi` uses the shared `api` client (auth token attached if available, optional per spec).
- **Search mode**: `POST /api/marketplace/search` result replaces the grid; "Скинути" clears back to paginated browse.
- **409 handling**: `ReviewModal` catches `ApiError` with status 409 and shows a specific Ukrainian-language message.
- **TagInput**: custom inline component (no shadcn dep needed), handles Enter/comma/Backspace for tag management.
