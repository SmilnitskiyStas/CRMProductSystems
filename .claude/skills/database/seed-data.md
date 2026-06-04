# Skill: Seed Data

Location: ShelfGuard.Infrastructure/Data/Seeders/

What to seed in dev:
- 1 test tenant
- 1 enterprise_admin + 1 store_manager + 1 merchandiser user
- 2-3 stores (shop + central_warehouse)
- 5-10 categories
- 10-20 products with realistic Ukrainian product names
- 5-10 product_stock records with different statuses (safe/warning/critical/expired)

Rule:
Seed only in Development environment.
Never hardcode production data.
