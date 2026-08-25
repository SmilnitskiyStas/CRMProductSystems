# TASK-616 — Consumer support ticket channel (Features/CustomerSupport)

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §2. Handoff read: `.claude/logs/handoffs/613-to-backend_database-engineer.md`.

## What changed

New `Features/CustomerSupport` (`IConsumerSupportService`/`ConsumerSupportService` +
`Dtos/ConsumerSupportDtos.cs`), mirroring the existing `SupplierSupportTicket`/
`SupplierSupportTicketMessage` pattern (`Features/Marketplace/SupplierSupportService.cs`)
but for consumer↔tenant instead of tenant↔supplier, on top of the `ConsumerSupportTicket`/
`ConsumerSupportTicketMessage` entities TASK-613 already landed.

- `IConsumerSupportTicketRepository`/`ConsumerSupportTicketRepository` — tracked
  `GetByIdAsync` (with Messages, oldest first) so staff read-marking and status changes
  don't need a separate `Update` call; paged `GetPagedForConsumerAsync`/
  `GetPagedForTenantAsync` (status filter, newest-first).
- Consumer-facing service methods: `CreateTicketAsync`, `GetMyTicketsAsync`,
  `GetTicketAsync`, `AddConsumerMessageAsync`.
- Staff-facing: `GetInboxAsync`, `GetTicketForStaffAsync` (marks unread consumer messages
  read as a side effect), `AddStaffReplyAsync`, `UpdateStatusAsync`. Named
  `GetTicketForStaffAsync` rather than an overload of `GetTicketAsync` — both would
  otherwise share the identical `(Guid, Guid, CancellationToken)` signature, which C#
  cannot overload on.
- `ConsumerSupportController` (`api/consumer/support`, `[Authorize]`,
  `consumer_account_id` claim — copied from `ConsumerLoyaltyController`/
  `ConsumerProfileController`): `POST /tickets` (TenantId in body, not the route — a
  consumer session is cross-tenant), `GET /tickets?tenantId=`, `GET /tickets/{id}`,
  `POST /tickets/{id}/messages`.
- `CustomerSupportInboxController` (`api/customer-support`,
  `AppPolicies.AtLeastStoreManager` — same tier as `CustomersController`, since this is a
  customer-facing staff surface, not admin-only): `GET /tickets` (status filter + paging),
  `GET /tickets/{id}`, `POST /tickets/{id}/reply`, `PUT /tickets/{id}/status`.
- Registered `IConsumerSupportService`/`IConsumerSupportTicketRepository` in
  `ShelfGuard.Application`/`ShelfGuard.Infrastructure` `DependencyInjection.cs` (both
  re-read fresh immediately before editing — TASK-614/615 registrations already present
  and untouched, appended after them, no conflicts).

## Auto-link (CustomerId)

Reuses two existing lookups, no new linking mechanism:
1. If the consumer already has a `LoyaltyMembership` at this tenant, reuse its own
   already-resolved `CustomerId` directly.
2. Otherwise falls back to `ICustomerRepository.FindByPhoneAsync` (the same phone-match
   LoyaltyService itself uses) through `ITenantSessionOverride` (`customers` has no
   `consumer_self_access` RLS policy, so a consumer session's ambient null
   `app.tenant_id` needs the override — same reasoning as LoyaltyService's own
   consumer-session call sites).
3. Never *creates* a Customer here (unlike Loyalty's find-or-create) — the entity's own
   doc says "when one exists"; opening a ticket isn't consent to create a CRM record.

Ticket insert itself needed no `ITenantSessionOverride` — `consumer_support_tickets`'
`consumer_self_access` RLS policy (USING, doubling as the implicit WITH CHECK) already
lets the consumer session insert its own row, same as `LoyaltyMembership`'s insert in
`LoyaltyService.JoinAsync`.

## Judgment calls

- **Reopen on reply** (explicitly flagged as a judgment call in the brief): a consumer
  replying after staff marked the ticket Resolved/Closed flips it back to Open. Nothing
  else in this service changes status automatically, so a staff close stays sticky
  against everything except the customer's own next message.
- **404 vs 403 for ownership**: uniform 404 for both "ticket doesn't exist" and "exists
  but belongs to another consumer" (never discloses which) — same posture the brief left
  open ("403/404 if not the owner").
- **CustomerName resolution cost**: staff-side DTO conversion resolves `CustomerName`
  directly (real tenant session, no override needed); consumer-side goes through
  `ITenantSessionOverride` per lookup, with a same-request cache in the list variant
  (`ToDtosForConsumerAsync`) since a consumer's own ticket list only ever has one
  consumer identity to resolve.
- No `[RequireModule]` gate on either controller — matches `CustomersController`'s own
  unconditional access; support isn't a separately-activatable module like loyalty.

## Tests

25 new unit tests in `ShelfGuard.Tests/CustomerSupport/ConsumerSupportServiceTests.cs`
(NSubstitute, mirrors `ConsumerProfileServiceTests`/`LoyaltyServiceTests` style):
ticket+first-message creation, both auto-link paths (membership reuse, phone-match
fallback, no-match leaves CustomerId null), 404/400 validation paths, cross-consumer
ticket access blocked (404), reopen-on-reply for both Resolved and Closed, staff reply
bumps UpdatedAt, status transition + invalid-status 400, staff read-marking (consumer
messages flip to read, staff's own messages untouched, no-op skips the extra
`SaveChangesAsync` when nothing was unread).

## Build/test status

`dotnet build`: 0 errors, 1 pre-existing unrelated warning (Marketplace tests).
`dotnet test` full suite: **1896/1896 passing** (25 new, no regressions — up from
1871 after TASK-615).

## Not implemented here (separate follow-up tasks per plan §5)

`Features/Reviews`, `Features/Customers` extension (tier/progress/open-ticket
count/recent reviews on `CustomerDetailDto`), worker tier-recompute job, frontend
(`/customer-support` staff inbox page, mobile screens). `mobile/` untouched (owned by a
separate concurrent agent).
