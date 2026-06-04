# Domain Model

**Owner:** project-architect
**Updated:** 2026-06-03
**Source:** v1-spec.md

## Core Entities

### Tenant
Multi-tenant root. All data tables have tenant_id FK.
Fields: id, name, slug, plan, modules (JSONB), is_active

### User
Belongs to tenant (or NULL for provider).
Roles: provider / enterprise_admin / network_manager / store_manager / merchandiser / storekeeper / cashier
Fields: id, tenant_id, email, role, store_id, telegram_chat_id, push_token

### Store
Physical location. Types: shop / central_warehouse / production / distribution
Fields: id, tenant_id, name, address, latitude, longitude, type

### Product
Catalog item. Supports ABM statuses (MTS/MTO/NA/NM).
Fields: id, tenant_id, barcode, name, category_id, unit, management_type, min_stock, max_stock, safety_buffer

### ProductStock (Batch)
One product can have multiple batches with different expiry dates.
FEFO: always consume batch with lowest expiry_date where quantity > 0.
Fields: id, tenant_id, product_id, store_id, zone_id, batch_number, quantity, expiry_date, status

### StockMovement
Audit trail for all stock changes.
Types: receipt / transfer / production / discount / write_off / sale / adjustment / return
Rule: expiry_date and batch_number NEVER change on movement.

## Key Business Rules
- FEFO: always pick batch with nearest expiry_date
- Batch status computed by cron: safe / warning / critical / expired / sold_out / needs_verification
- Transfer preserves expiry_date and batch_number unchanged
- safety_buffer is visual/reserved — not available for sale
