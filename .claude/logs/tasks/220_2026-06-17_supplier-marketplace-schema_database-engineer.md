# TASK-220 — DB: Supplier Marketplace Schema

**Agent:** database-engineer
**Date:** 2026-06-17
**Status:** done
**Migration:** `20260617183005_V4SupplierMarketplace`

---

## What was done

### Domain entities created (ShelfGuard.Domain/Entities/)
- `SupplierProfile.cs` — 1-to-1 extended profile for a supplier; `is_public`, `plan` (free|premium), JSONB fields for categories and delivery_regions
- `SupplierItem.cs` — supplier catalog entry; either `item_id` (FK → items) or `custom_name` must be non-null (CHECK constraint)
- `SupplierMetrics.cs` — 1-to-1 aggregated metrics (avg_delivery_days, order_accuracy, quality_score, rating, cancellation_rate, response_time_hours); updated by background job
- `SupplierReview.cs` — tenant review of a supplier; unique (supplier_id, tenant_id); rating CHECK 1–5

### AppDbContext wired (ShelfGuard.Infrastructure/Data/AppDbContext.cs)
Added DbSets and Fluent API configuration for all 4 entities:
- FK relationships with correct cascade/restrict semantics
- Unique indexes: `supplier_profiles.supplier_id` (1-to-1), `supplier_metrics.supplier_id` (1-to-1), `supplier_reviews.(supplier_id, tenant_id)`
- JSONB column types for categories, delivery_regions
- Numeric precision: price numeric(12,2), metrics numeric(5,4) / numeric(3,2) etc.

### Migration generated
`20260617183005_V4SupplierMarketplace.cs` — EF Core auto-generated + manual SQL appended:
- CHECK constraint: `supplier_items` — `"ItemId" IS NOT NULL OR "CustomName" IS NOT NULL`
- CHECK constraint: `supplier_reviews` — `"Rating" >= 1 AND "Rating" <= 5`
- RLS enabled on all 4 tables with standard `tenant_isolation` + `provider_bypass` policies

### Build
`dotnet build` — green, 0 errors.

---

## Architecture decision: public marketplace listing vs. RLS

**Decision:** RLS policies on `supplier_profiles` use standard tenant isolation
(`tenant_id = current_setting('app.tenant_id')::uuid`). The public API endpoint
`GET /api/marketplace/suppliers` (TASK-221) will use a provider-level DB connection
(with `app.role = 'provider'` set in the connection context), which triggers the
`provider_bypass` policy and returns all rows where `is_public = true`.

**Rationale:** Adding an OR clause (`OR "IsPublic" = true`) to the tenant_isolation
policy would allow cross-tenant reads for every connection, which undermines the
principle of keeping RLS policies simple and predictable. It's cleaner to keep RLS
strict and have the public-listing endpoint intentionally bypass it using the provider
connection — the same pattern already used by the admin API layer.

This approach is consistent with the existing `provider_bypass` policy pattern
established in InitialCreate / FullSchema migrations.

---

## Files changed
- `backend/ShelfGuard.Domain/Entities/SupplierProfile.cs` (new)
- `backend/ShelfGuard.Domain/Entities/SupplierItem.cs` (new)
- `backend/ShelfGuard.Domain/Entities/SupplierMetrics.cs` (new)
- `backend/ShelfGuard.Domain/Entities/SupplierReview.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` (updated)
- `backend/ShelfGuard.Infrastructure/Migrations/20260617183005_V4SupplierMarketplace.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260617183005_V4SupplierMarketplace.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (updated by EF)

---

## Handoff → TASK-221

**Next agent:** backend-developer
**Task:** TASK-221 — Backend: Supplier Marketplace API
**Status:** unblocked — schema is in place, entities are wired

Key notes for TASK-221:
- Public listing (`GET /api/marketplace/suppliers`) must use provider-level DB context (set `app.role = 'provider'`) and filter by `is_public = true`
- Premium-gated fields: check `supplier_profiles.plan = 'premium'` before returning premium-only fields
- Module guard: `[RequireModule("marketplace")]` on all endpoints except public listing
- `supplier_metrics` is populated by background job — TASK-221 reads only, no write path needed
