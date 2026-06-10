# Domain Model

**Owner:** project-architect
**Updated:** 2026-06-04
**Source:** v1-spec.md

## Core Entities

### Tenant
Multi-tenant root. All data tables have TenantId FK + RLS.
Fields: id, name, slug, plan, modules (JSONB), is_active, created_at

### User
Belongs to tenant (or NULL for provider role).
Roles: provider / enterprise_admin / network_manager / store_manager / merchandiser / storekeeper / cashier
Fields: id, tenant_id, email, role, store_id, telegram_chat_id, push_token, is_active

### Store
Physical location. Types: shop / central_warehouse / production / distribution
Fields: id, tenant_id, name, address, latitude, longitude, type, floor_plan (JSONB)

### StoreZone
Zone within a store (shelf, fridge, freezer, display, production, warehouse).
Fields: id, store_id, name, type, position (JSONB), shelves_count, temp_min, temp_max
Note: no direct tenant_id — RLS enforced via stores join.

### Category
Hierarchical product categories. Self-referencing parent_id.
Fields: id, tenant_id, name, parent_id, is_active

### ProductSegment
Demand segment (e.g. "Milk 2.5%"). Used for cannibalization analysis in v2.
Fields: id, tenant_id, name, category_id, description

### Supplier
Fields: id, tenant_id, name, edrpou, contact_person, delivery_days, has_supplier_portal, return_policy

### CatalogProduct (v1 tenant-aware product)
EF entity: `CatalogProduct` → table: `catalog_products`
Supports ABM management types (MTS/MTO/NA/NM), buffers, shelf life.
Fields: id, tenant_id, barcode, name, category_id, segment_id, unit, management_type, min_stock, max_stock, safety_buffer, shelf_life_days, default_supplier_id, vat_rate, price_purchase, price_retail

> ⚠️ Legacy POC `Product` entity → `Products` table (no tenant_id) still exists for the catalog API. Will be removed in TASK-003b.

### ProductSupplierSetting
ABM params per product-supplier pair: MOQ, USQ, price, delivery days.
Fields: id, tenant_id, product_id, supplier_id, moq, usq, price_purchase, delivery_days, is_primary

### ProductStock (Batch) — CORE FEFO TABLE
One product can have multiple batches with different expiry dates.
**FEFO rule**: always consume the batch with the lowest `expiry_date` where `quantity > 0`.
Fields: id, tenant_id, product_id, store_id, zone_id, shelf_number, batch_number, quantity, quantity_initial, expiry_date (DATE NOT NULL), status, source_type, source_id, added_by, notified_warning_at, notified_critical_at

Status values: safe / warning / critical / expired / sold_out / archived / needs_verification
Status computed by `expiry-check.job` (BullMQ cron, every hour).

### StockMovement
Audit trail for every quantity change.
Types: receipt / transfer / production / discount / write_off / sale / adjustment / return
Fields: id, tenant_id, movement_type, product_stock_id, product_id, from_store_id, to_store_id, quantity, quantity_before, quantity_after, unit_price, reference_id, performed_by

### StockEvent
IoT/sensor event placeholder (v3). Stores confidence score for sensor readings.
Fields: id, tenant_id, event_type, product_stock_id, source_device_id, quantity_delta, confidence (0-100), meta (JSONB)

### StockReceipt
Goods receiving document. status: draft → ordered → in_transit → received → cancelled.
Fields: id, tenant_id, supplier_id, destination_store_id, via_central_store, status, expected_at, received_at, created_by, received_by

### StockReceiptItem
Line item in a receipt. expiry_date and batch_number entered at receiving time.
Fields: id, receipt_id, product_id, quantity_ordered, quantity_received, price_purchase, expiry_date, batch_number, discrepancy_notes

### StockTransfer
Stock movement between stores. status: draft → in_transit → received → cancelled.
Fields: id, tenant_id, from_store_id, to_store_id, transfer_type, status, initiated_by, confirmed_by
**Rule:** expiry_date and batch_number are COPIED from ProductStock — never change.

### StockTransferItem
Fields: id, transfer_id, product_stock_id, product_id, quantity, expiry_date (copied), batch_number (copied)

### WriteOff
Write-off document. status: draft → pending_approval → approved → rejected.
Reasons: expired / damaged / theft / production_loss / other
Fields: id, tenant_id, store_id, status, reason, total_loss_amount, pdf_url, created_by, approved_by, approved_at

### WriteOffItem
Fields: id, write_off_id, product_stock_id, product_id, quantity, unit_price, loss_amount

### Discount
Price reduction for near-expiry stock. status: pending → active → expired → cancelled.
Fields: id, tenant_id, product_stock_id, product_id, store_id, discount_percent, price_original, price_discounted, reason, valid_from, valid_until, auto_applied, webhook_sent_at

### NotificationSetting
Per-user per-event per-channel notification preferences.
Fields: id, user_id, event_type, channel (telegram/push/email/webhook), is_enabled
Unique constraint: (user_id, event_type, channel)

### NotificationQueue
BullMQ-backed delivery queue. status: pending → sent / failed.
Fields: id, tenant_id, user_id, channel, event_type, payload (JSONB), status, retry_count, sent_at, error

### ActivityLog
Immutable audit log. All impersonated actions flagged.
Fields: id, tenant_id, user_id, action, entity_type, entity_id, meta (JSONB), ip_address, is_impersonated, created_at

---

## Key Business Rules
1. **FEFO** — always pick the batch with the nearest `expiry_date` where `quantity > 0`
2. **Expiry dates never change** — `expiry_date` and `batch_number` are copied as-is on transfer
3. **Batch status** — computed by `expiry-check.job` cron (hourly); never computed on-the-fly in queries
4. **Safety buffer** — reserved quantity for shelf presentation; not available for sale
5. **Soft delete** — all business entities use `is_active = false`, never hard DELETE
6. **Tenant isolation** — enforced at DB level via RLS; application layer never filters by tenant_id manually
