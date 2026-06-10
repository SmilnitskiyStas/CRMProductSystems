# Glossary

**Owner:** documentation-writer
**Updated:** 2026-06-04

## Business Terms

**FEFO** — First Expired, First Out. Stock consumption rule: always sell/use the batch with the nearest expiry date first.

**Batch (Партія)** — A specific delivery of a product with a unique expiry date and optional batch number. One product can have multiple active batches in the same store with different expiry dates and quantities.

**Expiry status** — Computed hourly by `expiry-check.job`:
- `safe` — more than 14 days remaining
- `warning` — 7–14 days remaining
- `critical` — 1–6 days remaining
- `expired` — 0 or fewer days remaining
- `sold_out` — quantity = 0
- `archived` — sold_out for more than 30 days (cleanup job)
- `needs_verification` — last checked more than 90 days ago

**Safety buffer (ББ)** — Reserved minimum quantity for shelf presentation (facing). Not available for sale. If sold, counts as a lost sale.

**MOQ** — Minimum Order Quantity. Cannot order less than this from a supplier.

**USQ** — Unit Step Quantity. Order must be a multiple of this (after MOQ).

**ADU** — Average Daily Usage. Mean daily consumption over 30/60/90 days of valid sales.

**CDA** — Consumption Driven Algorithm. Buffer calculation method with Green/Yellow/Red zones for reorder point.

**MTS** — Make to Stock. Product always on shelf, regularly ordered automatically.
**MTO** — Make to Order. Special orders only, not stocked.
**NA** — Not Active. Removed from assortment.
**NM** — Not Managed. Tracked but not ordered automatically.

**RLS** — Row Level Security. PostgreSQL feature enforcing tenant isolation at DB level via policies on each table.

**Tenant** — A client company using the ShelfGuard platform (e.g. a retail chain).

**Provider** — The ShelfGuard platform owner. Role = `provider`. Has access to all tenants. TenantId = NULL in JWT.

**Impersonation** — Provider accessing a specific tenant's account for support purposes. Always logged in `activity_logs` with `is_impersonated = true`.

**TenantConnectionInterceptor** — EF Core `DbConnectionInterceptor` that sets `app.tenant_id` and `app.role` PostgreSQL session variables on every connection open. Activates RLS automatically for all queries.

## Technical Terms

**FEFO index** — `idx_stock_expiry_active` on `product_stock("TenantId", "StoreId", "ProductId", "ExpiryDate")` WHERE quantity > 0 AND status NOT IN ('sold_out', 'archived'). Critical for performant FEFO batch selection queries.

**POC Products** — Legacy `Products` table (EF entity `Product`) created for initial testing. Has no `TenantId`. Will be replaced by `catalog_products` in TASK-003b.

**catalog_products** — V1 tenant-aware product catalog table (EF entity `CatalogProduct`). Has `TenantId`, RLS, full ABM fields. This is the production product table.

**apiFetch** — Frontend HTTP wrapper in `lib/api.ts`. Handles Authorization header injection, 401 refresh retry, and session expiry redirect. All feature API modules must use `import { api } from "@/lib/api"` — never define a local apiFetch.
