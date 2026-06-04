# Database Schema

**Owner:** database-engineer
**Updated:** 2026-06-04
**Source:** v1-spec.md section 4

## Multi-Tenancy
Row Level Security (RLS) on every tenant table.
`app.tenant_id` set per-connection by `TenantConnectionInterceptor`.
`app.role = 'provider'` bypasses all tenant isolation via `provider_bypass` policy.

## RLS Template
All column names are double-quoted to match EF Core PascalCase naming.
```sql
ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON {table}
  USING ("TenantId" = current_setting('app.tenant_id', true)::uuid);
CREATE POLICY provider_bypass ON {table}
  USING (current_setting('app.role', true) = 'provider');
```
Child tables without direct TenantId use EXISTS subquery through parent.

## Migration History
| Migration | Date | Description |
|---|---|---|
| InitialCreate | 2026-06-01 | POC Products table |
| AddAuth | 2026-06-03 | tenants, users, refresh_tokens + RLS |
| FullSchema | 2026-06-04 | Full v1 schema (19 new tables) |

## Tables

### Auth (existing)
| Table | RLS | Notes |
|---|---|---|
| tenants | — | Root entity, no RLS needed |
| users | ✅ | TenantId IS NULL for provider users |
| refresh_tokens | ✅ | Via user sub-select |

### POC (deprecated, kept for catalog API compat)
| Table | Notes |
|---|---|
| Products | No tenant_id. Will be removed when catalog API migrates to catalog_products |

### Structure
| Table | RLS | Notes |
|---|---|---|
| stores | ✅ | TenantId direct |
| store_zones | ✅ | Via stores.TenantId subquery |
| categories | ✅ | TenantId direct, self-referencing parent_id |
| product_segments | ✅ | TenantId direct |
| suppliers | ✅ | TenantId direct |

### Products (v1)
| Table | RLS | Notes |
|---|---|---|
| catalog_products | ✅ | Tenant-aware, maps to "catalog_products" table |
| product_supplier_settings | ✅ | Unique(product_id, supplier_id, tenant_id) |

### Stock
| Table | RLS | Notes |
|---|---|---|
| product_stock | ✅ | Core FEFO table. ExpiryDate DATE NOT NULL |
| stock_movements | ✅ | Audit log of all quantity changes |
| stock_events | ✅ | IoT/sensor events placeholder (v3) |

### Documents
| Table | RLS | Notes |
|---|---|---|
| stock_receipts | ✅ | TenantId direct |
| stock_receipt_items | ✅ | Via stock_receipts subquery |
| stock_transfers | ✅ | TenantId direct |
| stock_transfer_items | ✅ | Via stock_transfers subquery. ExpiryDate + BatchNumber COPIED, never changed |
| write_offs | ✅ | TenantId direct |
| write_off_items | ✅ | Via write_offs subquery |
| discounts | ✅ | TenantId direct |

### Notifications
| Table | RLS | Notes |
|---|---|---|
| notification_settings | — | No RLS (tied to user, not tenant directly) |
| notification_queue | ✅ | TenantId nullable (system messages have NULL) |

### Logs
| Table | RLS | Notes |
|---|---|---|
| activity_logs | ✅ | TenantId nullable (provider actions have NULL) |

## Key Indexes
```sql
-- FEFO: always consume nearest expiry first
idx_stock_expiry_active ON product_stock("TenantId", "StoreId", "ProductId", "ExpiryDate")
  WHERE "Quantity" > 0 AND "Status" NOT IN ('sold_out', 'archived')

-- Fast store dashboard queries
idx_stock_tenant_store ON product_stock("TenantId", "StoreId")
```

## Architecture Rules
- `expiry_date` and `batch_number` are NEVER modified on transfer — copied as-is to `stock_transfer_items`
- All soft deletes via `is_active`, never hard DELETE on business data
- UUID PKs with `gen_random_uuid()` default
- All timestamps in UTC (`TIMESTAMPTZ`)
