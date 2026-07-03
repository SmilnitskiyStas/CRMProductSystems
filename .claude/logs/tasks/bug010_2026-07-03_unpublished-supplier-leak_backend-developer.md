# BUG-010 — Unpublished supplier profile leak via detail endpoint

**Agent:** backend-developer · **Date:** 2026-07-03 · **Status:** done

## Problem
`GET /api/marketplace/suppliers/{id}` (AllowAnonymous) повертав профіль з
`IsPublic=false`. Листинг і search фільтрують `IsPublic`, detail — ні.

## Fix
`MarketplaceService.GetSupplierProfileAsync`: після завантаження профілю —
`if (!profile.IsPublic) return null;` → контролер віддає 404. Діє однаково для
анонімних і автентифікованих викликів (unpublished = не існує для публіки).

## Call-site check (legitimate access не зламано)
- Supplier cabinet читає власний профіль через `SupplierCabinetController` →
  `ISupplierCabinetService` → `GetOwnerManagedProfileAsync` (tenant RLS) — не зачіпається.
- `MarketplaceAdminController` використовує лише `AdminCreateSupplierAsync` /
  `AdminAddSupplierItemAsync` / `AdminDeleteSupplierItemAsync` — detail не викликає.
- `GetSupplierProfileAsync` має єдиний call site: публічний detail-ендпоінт.

## Tests
- `GetSupplierProfileAsync_Unpublished_ReturnsNull` (anon + authenticated)
- `GetSupplierProfileAsync_Published_ReturnsProfile`
`dotnet build` — 0 warnings/errors. `dotnet test` — 496/496 passed.

## Note (out of scope)
`GET /suppliers/{id}/items` і `GET /suppliers/{id}/reviews` теж не перевіряють
`IsPublic` unpublished-постачальника — варто окремий фікс, якщо QA підтвердить.
