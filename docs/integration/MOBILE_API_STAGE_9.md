# Mobile API follow-up — Loyalty tier

The Stage 9 mobile wallet supports an optional `tier: string | null` on the consumer membership
summary. The current backend `LoyaltyMembershipSummaryDto` does not expose a retailer loyalty tier.

When retailer-defined tiers are implemented, add the resolved display value to:

- `GET /api/consumer/loyalty/memberships` responses;
- join and preferred-store mutation responses that return `LoyaltyMembershipSummaryDto`.

Until then mobile displays `Не налаштовано`. No balance-based tier inference is performed.
