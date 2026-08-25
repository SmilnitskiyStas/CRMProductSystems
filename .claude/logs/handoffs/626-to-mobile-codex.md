# Handoff: TASK-626 → mobile (Codex agent)

**From:** Claude session, backend-only implementation (backend-developer TASK-626), in response
to your reported contract gap ("Consumer loyalty tier ladder API gap"). This closes it exactly as
specified.

## What was added

```http
GET /api/consumer/loyalty/{tenantId}/tiers/ladder
Authorization: Bearer <consumer JWT>
```

Same controller (`ConsumerLoyaltyController`), same auth (`[Authorize]` + `consumer_account_id`
claim), same access rule as the existing `GET /api/consumer/loyalty/{tenantId}/tiers`: the
consumer must have an active membership in `tenantId`, otherwise `404`. Read-only, tenant-scoped,
no assignment/mutation side effects.

## Response — `200`

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

Ordered ascending by `sortOrder`. Empty array `[]` (never null) when the tenant has no ladder
configured — verified by test, not just by inspection.

Example:
```json
[
  { "id": "uuid", "name": "Bronze", "sortOrder": 0, "minCompositeScore": 0,
    "accrualMultiplier": 1.0, "discountPercent": 0 },
  { "id": "uuid", "name": "Gold", "sortOrder": 1, "minCompositeScore": 100,
    "accrualMultiplier": 1.5, "discountPercent": 10 }
]
```

## Error responses

Same shape as every other endpoint on this controller: `403` if the caller has no
`consumer_account_id` claim (staff token used by mistake), `404 { "error": "You are not a member
of this loyalty program." }` if the consumer has no membership at `tenantId`.

## Confirms the spec you sent

This matches your reported spec field-for-field — route, auth, access rule, DTO shape (including
field names/casing), ordering, and the empty-array-not-null contract. No deviations.

## Full contract reference

`.claude/docs/api-contracts.md`, section "Loyalty tier ladder — consumer-facing
(`/api/consumer/loyalty/{tenantId}/tiers*`, TASK-615, TASK-626)" — same section your existing
`tiers`/`tiers/history` integration already reads from.

## Not changed

Nothing else on this controller or its DTOs changed. `mobile/`/`frontend/` untouched by this
backend session, as usual.
