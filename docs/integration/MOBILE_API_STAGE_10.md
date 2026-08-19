# Mobile API follow-up — Catalog and promotion details

Stage 10 uses the existing tenant/store-scoped catalog and promotion endpoints. Three read
contracts are still needed for complete navigation and cold-start deep-link support:

1. `GET /api/consumer/{tenantId}/categories?storeId={storeId}` returning stable category IDs,
   names, and ordering.
2. `GET /api/consumer/{tenantId}/catalog/{productId}?storeId={storeId}` returning one product with
   its current price and store availability.
3. A promotion/campaign detail endpoint if promotions should be entities beyond the current
   discounted-product projection.

All endpoints must keep `tenantId` explicit and validate that `storeId` belongs to that tenant.
Mobile currently derives categories from up to 100 returned catalog items and fails closed for a
product detail that is not already in the active tenant's runtime cache.
