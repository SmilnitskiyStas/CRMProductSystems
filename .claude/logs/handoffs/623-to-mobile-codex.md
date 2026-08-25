# Handoff: TASK-613..622 → mobile (Codex agent)

**From:** Claude session, backend + web-only implementation (database-engineer TASK-613,
backend-developer TASK-614/615/616/617/618/621b, devops-engineer TASK-619, frontend-developer
TASK-620/621, qa-tester TASK-622 — no bugs found). This is the documentation-writer's (TASK-623)
mobile hand-off extract, per the approved plan's own instruction (`goofy-bubbling-naur.md` §4,
"Специфікація для мобільної команди") that mobile screens for this feature are **out of scope for
this session** and are this repo's separate, concurrently-running mobile (Codex) agent's job.

**Full contract source:** `.claude/docs/api-contracts.md` — the four sections "Consumer Profile
self-service", "Loyalty tier ladder — consumer-facing", "Consumer support tickets", and "Purchase
reviews" carry every route, DTO field, and status code below verbatim (copied from there, not
re-derived). If the two documents ever disagree, `api-contracts.md` is the source of truth — this
file is a curated extract, not a duplicate spec.

**Rationale/domain background:** `.claude/docs/decisions.md` ADR-034 (why phone-change uses
password re-entry not SMS/OTP, why the composite score is nightly-only, why review ownership
resolves through the loyalty ledger and can't cover walk-in purchases). `.claude/docs/domain-model.md`
(new entities: `ConsumerAccountProfileChange`, `LoyaltyTierDefinition`, `LoyaltyTierChangeHistory`,
`ConsumerSupportTicket`/`Message`, `PurchaseReview`).

This document is written to stand alone — you do not need to read the conversation that produced
it.

## What this feature is

Four independent additions to the existing consumer-facing (`ConsumerAccount`-JWT) API surface,
all already live on the backend and exercised end-to-end via Swagger/Postman + direct SQL by QA
(no mobile UI existed to test through):

1. **Self-service profile editing** — a logged-in consumer can change their own name/email/phone,
   with phone changes gated by password re-entry (no SMS involved anywhere in this repo).
2. **Loyalty tier ladder (consumer-facing read)** — the existing wallet screen's balance/QR code
   gains a rank/tier concept: current tier, composite score, progress toward the next tier, and a
   history of past tier changes.
3. **Support tickets** — a consumer can open a ticket to a specific tenant and exchange messages
   with that tenant's staff (async, no live chat).
4. **Purchase reviews** — a consumer can leave a 1-5 star rating + comment on one specific
   completed purchase, and see the tenant's staff reply once one exists.

None of these require new native capabilities (no camera, no barcode scanner, no new picker
libraries) — this is form input + list/detail screens + one existing pattern (star rating), unlike
TASK-586's marketplace-receiving feature.

## Existing mobile patterns to build on

- `mobile/app/(personal)/account.tsx` — the plan names this as the natural home for profile
  editing; check what's already there before adding a new screen vs. extending this one.
- `mobile/app/(app)/wallet.tsx` (if it exists under that name — check the current wallet/loyalty
  screen, whichever file backs `GET api/consumer/loyalty/memberships`/`.../code` today) — natural
  home for the tier/progress display, alongside the existing balance + rotating QR code UI.
  `LoyaltyTierProgressDto`'s "no tier yet" vs. "at the top tier" vs. "normal progress" null-states
  (see `api-contracts.md`) need three distinct renders, not one generic empty-state.
  `LoyaltyTierChangeHistoryDto`'s paged list is a natural second screen/tab off the same area.
- `mobile/app/(app)/history.tsx` (or wherever past purchases/receipts render for a consumer) — the
  natural place for a "Leave a review" action per past purchase, per the plan. A purchase with no
  loyalty-ledger link (rare — only for a walk-in-style sale, see ADR-034 Decision 5) will 403 on
  review creation; decide how/whether to surface that vs. simply not offering the action for such
  purchases (this repo's web frontend doesn't face this question, since it never lets a consumer
  browse transactions directly — you may be the first to need a client-side answer here).
- No existing support-ticket screen exists anywhere in `mobile/` today (`ServiceDesk` is a
  staff-only web feature; this is a brand-new consumer-facing screen) — build fresh: a "Contact
  us" entry point (per-tenant, since `tenantId` is required on every consumer support/review call —
  a consumer session is cross-tenant, so the screen needs to know which of the consumer's joined
  tenants/stores it's opening a ticket against), a ticket list, and a thread view (poll or
  pull-to-refresh, no push/websocket infrastructure exists for this).

## API contract (all under the existing consumer JWT bearer auth — same token every other
`/api/consumer/*` call already uses, `consumer_account_id` claim, no `tenant_id` claim; nothing new
on the auth side)

### a. Profile — `/api/consumer/profile`
```
GET  api/consumer/profile                            -> 200 ConsumerProfileDto
PUT  api/consumer/profile          { fullName?, email? }           -> 200 ConsumerProfileDto | 400 | 409
PUT  api/consumer/profile/phone    { newPhone, currentPassword }   -> 200 ConsumerProfileDto | 400 | 409
GET  api/consumer/profile/history?page=&pageSize=     -> 200 PagedResult<ConsumerProfileChangeDto>
```
`email: ""` (empty string) clears it. `fullName` may not be blank if provided. Setting phone to its
own current value is a silent no-op. `409` = duplicate email/phone already in use by another
account.

### b. Loyalty tier — `/api/consumer/loyalty/{tenantId}/tiers*`
```
GET api/consumer/loyalty/{tenantId}/tiers                          -> 200 LoyaltyTierProgressDto
GET api/consumer/loyalty/{tenantId}/tiers/history?page=&pageSize=  -> 200 PagedResult<LoyaltyTierChangeHistoryDto>
```
`LoyaltyTierProgressDto`: `currentTierId`/`currentTierName` both null = no tier assigned yet
(`accrualMultiplier`/`discountPercent` sit at neutral defaults 1.0/0 in that case, don't render
them as if they were a real bonus). `nextTierId`/`nextTierName`/`scoreToNextTier` all null = either
already at the top tier, or the tenant has no ladder configured at all — same rendering either way
("no further tier to show"), the two cases aren't distinguished by this DTO.

### c. Support tickets — `/api/consumer/support`
```
POST api/consumer/support/tickets                { tenantId, subject, body }  -> 201 ConsumerSupportTicketDto
GET  api/consumer/support/tickets?tenantId=&page=&pageSize=                    -> 200 PagedResult<ConsumerSupportTicketDto>
GET  api/consumer/support/tickets/{id}                                          -> 200 ConsumerSupportTicketDto | 404
POST api/consumer/support/tickets/{id}/messages   { body }                     -> 201 ConsumerSupportTicketMessageDto
```
`tenantId` is required on the list GET (a consumer's tickets are per-tenant, unlike the
cross-tenant wallet list) — the screen needs a tenant/store picker or context, same as opening a
review. `messages` on the ticket-detail GET is oldest-first; render as a chat thread, deriving
"mine vs. theirs" from which of `senderConsumerAccountId`/`senderUserId` is non-null on each
message (yours is always `senderConsumerAccountId`). A reply on a ticket the tenant already marked
Resolved/Closed silently reopens it — no special client action needed, just re-render the returned
`status`.

### d. Reviews — `/api/consumer/reviews`
```
POST api/consumer/reviews   { tenantId, posTransactionId, rating (1-5), comment? }  -> 201 PurchaseReviewDto | 403 | 409
GET  api/consumer/reviews?tenantId=&page=&pageSize=                                  -> 200 PagedResult<PurchaseReviewDto>
```
`403` — not your purchase, or the purchase has no loyalty link at all (same generic error either
way, don't try to distinguish in the UI). `409` — a review already exists for this purchase; if
you're offering "Leave a review" from a purchase-history list, check `GET .../reviews` first (or
just catch the 409) to decide whether to show "Leave a review" vs. "View your review."
`ReplyText`/`RepliedAt` on the returned/listed `PurchaseReviewDto` are read-only from the mobile
side — only staff can reply (`PUT /api/reviews/{id}/reply`, not a consumer-facing route).

## DTO shapes (camelCase over the wire) — copied verbatim from `api-contracts.md`

```ts
type ConsumerProfileDto = {
  consumerAccountId: string; fullName: string; email: string | null;
  phone: string; registeredAt: string; // ISO 8601
};

type ConsumerProfileChangeDto = {
  fieldName: "phone" | "email" | "full_name";
  oldValue: string | null; newValue: string | null; changedAt: string;
};

type LoyaltyTierProgressDto = {
  currentTierId: string | null; currentTierName: string | null;
  accrualMultiplier: number; discountPercent: number; compositeScore: number;
  nextTierId: string | null; nextTierName: string | null; scoreToNextTier: number | null;
};

type LoyaltyTierChangeHistoryDto = {
  id: string; fromTierName: string | null; toTierName: string | null;
  fromScore: number; toScore: number; changedAt: string;
};

type ConsumerSupportTicketDto = {
  id: string; tenantId: string; consumerAccountId: string;
  consumerName: string; consumerPhone: string;
  customerId: string | null; customerName: string | null;
  subject: string; status: "open" | "in_progress" | "resolved" | "closed";
  createdAt: string; updatedAt: string;
  messages: ConsumerSupportTicketMessageDto[] | null; // populated only on single-ticket GET
};

type ConsumerSupportTicketMessageDto = {
  id: string; ticketId: string;
  senderConsumerAccountId: string | null; senderUserId: string | null; // exactly one set
  body: string; isRead: boolean; createdAt: string;
};

type PurchaseReviewDto = {
  id: string; tenantId: string; consumerAccountId: string;
  consumerName: string; consumerPhone: string; posTransactionId: string;
  rating: number; comment: string | null; createdAt: string;
  replyText: string | null; repliedAt: string | null; repliedByUserId: string | null;
};
```

## Known limitations, don't try to build around them

- No push notifications for new staff replies (ticket or review) — this repo has no push
  infrastructure wired to the consumer app at all yet. Poll or pull-to-refresh only.
- A walk-in-style purchase (no loyalty ledger entry) can never be reviewed — this is a backend
  design limitation (ADR-034 Decision 5), not something the mobile client can work around by
  passing different fields.
- Composite score and tier are nightly-batch only — a brand-new membership or a very recent
  purchase will not move the displayed tier/score until the next 04:00 recompute. Don't build any
  "refresh to recalculate" affordance; there is nothing for it to trigger.
- Reordering the tier ladder (admin side, web-only) is a delete+insert under the hood — irrelevant
  to mobile, but if a tier's `id` you cached client-side stops resolving after an admin edits the
  ladder, that's expected, not a bug to report.
