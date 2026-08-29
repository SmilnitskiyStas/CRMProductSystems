# Consumer loyalty tier ladder API gap

The mobile rank-progress screen needs the complete configured ladder for the selected retailer.
The existing consumer endpoint returns only the current and next tier, while
`GET /api/settings/loyalty/tiers` is correctly restricted to enterprise administrators and must
not be called with a consumer token.

Required backend addition:

```http
GET /api/consumer/loyalty/{tenantId}/tiers/ladder
Authorization: Bearer <consumer JWT>
```

Access rules must match `GET /api/consumer/loyalty/{tenantId}/tiers`: the consumer must have an
active membership in that tenant; otherwise return 404/403 according to the existing policy.

Response `200` (ordered by `sortOrder`, empty array when no ladder is configured):

```ts
type LoyaltyTierDefinitionDto = {
  id: string;
  name: string;
  sortOrder: number;
  minCompositeScore: number;
  accrualMultiplier: number;
  discountPercent: number;
};
```

This data is retailer-visible loyalty-program configuration, not an assignment mutation. The
endpoint must be read-only and tenant-scoped. Mobile implementation and graceful current/next
fallback are already present.
