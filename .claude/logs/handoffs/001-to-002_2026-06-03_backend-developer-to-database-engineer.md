# Handoff: TASK-001 → TASK-002

**Date:** 2026-06-03
**From:** backend-developer
**To:** database-engineer
**Task:** TASK-002 — Implement full v1 database schema

## What was completed

TASK-001 done. All backend projects renamed to ShelfGuard.*:
- `backend/ShelfGuard.sln` — solution entry point
- `dotnet build` → 0 errors
- `dotnet test` → 6/6 passed
- No `CRM.` references remain anywhere in source

## What to do next

1. Read `v1-spec.md` section 4.2 — full SQL DDL for all tables
2. Implement the full v1 schema in `ShelfGuard.Infrastructure`:
   - Replace/extend `AppDbContext` with all domain entities
   - Add entity classes in `ShelfGuard.Domain/Entities/`
   - Add EF Core configurations (entity type configurations or fluent API in OnModelCreating)
   - Add RLS policies via raw SQL in migration
3. Create new EF Core migration: `dotnet ef migrations add FullV1Schema --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api`
4. Verify migration SQL matches v1-spec.md tables, indexes, and RLS policies
5. Verify `dotnet build` passes after schema changes

## Important context

- **Tables needed (v1):** tenants, users, stores, store_zones, categories, product_segments, suppliers, products, product_supplier_settings, product_stock, stock_movements, stock_events, stock_receipts, stock_receipt_items, stock_transfers, stock_transfer_items, write_offs, write_off_items, discounts, notification_settings, notification_queue, activity_logs
- **RLS pattern:** every tenant table needs `ALTER TABLE ENABLE ROW LEVEL SECURITY` + two policies (tenant_isolation, provider_bypass) — see `.claude/docs/database-schema.md`
- **Key indexes:** `idx_stock_expiry_active` on `product_stock` is critical for FEFO performance
- The existing `products` table from InitialCreate will be superseded — either delete the old migration or add a new one that drops and recreates properly
- DB name stays `crm`, port 5435 (Docker) or 5432 (local)
- Do NOT change auth logic — that is TASK-003

## Risks / Blockers

- The existing `InitialCreate` migration has a `products` table that conflicts with the new schema — plan how to handle (new migration that alters/drops, or squash)
- RLS policies require running as a superuser in migration — verify Docker PostgreSQL user has sufficient privileges

## Files to review

- `v1-spec.md` section 4.2 — authoritative DDL
- `.claude/docs/database-schema.md` — RLS template and table list
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` — current context (products only)
- `backend/ShelfGuard.Infrastructure/Migrations/` — existing InitialCreate migration

## Definition of done

- All v1 tables exist in database after `dotnet ef database update`
- RLS policies applied to all tenant tables
- Key indexes created (especially idx_stock_expiry_active)
- `dotnet build` exits 0
- `.claude/docs/database-schema.md` updated with current migration state
