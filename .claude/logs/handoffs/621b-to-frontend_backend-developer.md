# Handoff: TASK-621b backend-developer → frontend-developer

Plan: `C:\Users\stass\.claude\plans\goofy-bubbling-naur.md` (§4 "Картка клієнта" — the "історія
змін профілю" tab specifically). Task log:
`.claude/logs/tasks/621b_2026-08-24_staff-profile-history-endpoint_backend-developer.md`.

Context: TASK-618's handoff (`618-to-frontend_backend-developer.md`) flagged that profile change
history was "not surfaced by this endpoint at all, would need its own call if the tab wants it".
This task adds that call.

## What's ready

New endpoint, separate from `GET /api/customers/{id}` (`CustomerDetailDto` is unchanged):

```
GET /api/customers/{id}/profile-history?page=1&pageSize=50
```

`AppPolicies.AtLeastStoreManager` (same class-level gate as every other `CustomersController`
route). `{id}` is the CRM `Customer.Id` (the same id used everywhere else in `/customers`), not a
`consumerAccountId` — the backend resolves the link internally.

Response — `PagedResult<ConsumerProfileChangeDto>`:

```ts
{
  items: {
    fieldName: string;      // "full_name" | "email" | "phone" — see ConsumerAccountProfileChangeField
    oldValue: string | null;
    newValue: string | null;
    changedAt: string;      // ISO 8601 (DateTimeOffset)
  }[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
```

Newest-first (same ordering as the consumer's own self-service history endpoint, TASK-614's
`GET api/consumer/profile/history` — this staff endpoint delegates to the exact same underlying
data, just reached via the CRM `Customer` id instead of a JWT-derived `consumerAccountId`).

## Null/empty-state notes for the UI

- A customer who never joined this tenant's loyalty program (no linked `LoyaltyMembership`) has no
  consumer-side profile to show history for. The endpoint returns **200 with an empty page**
  (`items: []`, `totalCount: 0`) in this case — not a 404 or error. Treat it as "no history yet" /
  empty state, not a failure to render.
- Lazy-load this tab: fetch only when the profile-history tab is actually opened in the drawer (per
  plan §4's own framing — "буде lazy-load лише при відкритті цієї вкладки"), not on initial
  customer-detail load, since history can be arbitrarily long for an old account.

## What's NOT done (this wave's job, per plan §4/§8, unchanged from TASK-618's handoff)

`CustomerDetail.tsx` tabs themselves (rank/progress, tickets, reviews, and now profile history) —
all still TASK-621's job. This task only adds the one missing backend route the profile-history tab
needs; the tier/progress, open-ticket-count, and recent-reviews data was already available from
`GET /api/customers/{id}` per the TASK-618 handoff above. `/customer-support` staff inbox page and
tier-ladder admin page remain separate, already-completed/scoped waves (TASK-616/617/620) — not
this handoff's concern.
