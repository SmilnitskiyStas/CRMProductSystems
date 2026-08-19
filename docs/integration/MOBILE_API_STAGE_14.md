# Mobile API follow-up — Consumer analytics ingestion

Mobile now exposes an injectable analytics transport and emits a strict allowlisted event shape:

```json
{
  "name": "product_opened",
  "tenantId": "tenant-id",
  "properties": {
    "productId": "product-id",
    "source": "catalog"
  }
}
```

A future ingestion endpoint should authenticate where possible, validate the same event/property
allowlists server-side, rate-limit abuse, add server receipt time, and enforce retention policy.
It should reject unknown properties rather than storing arbitrary JSON.

Do not request or derive customer phone, email, name, loyalty balance, QR/barcode content, auth
tokens, exact location, or store address for these six product analytics events.
