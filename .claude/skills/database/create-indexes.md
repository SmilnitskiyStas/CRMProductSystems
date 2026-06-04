# Skill: Create Indexes

Required indexes for ShelfGuard:

FEFO index (product_stock):
CREATE INDEX idx_stock_expiry_active
  ON product_stock(tenant_id, store_id, product_id, expiry_date)
  WHERE quantity > 0 AND status NOT IN ('sold_out', 'archived');

Tenant + store lookups:
CREATE INDEX idx_stock_tenant_store ON product_stock(tenant_id, store_id);

Rule:
Partial indexes (WHERE clause) for hot query paths.
Every FK column should have an index.
