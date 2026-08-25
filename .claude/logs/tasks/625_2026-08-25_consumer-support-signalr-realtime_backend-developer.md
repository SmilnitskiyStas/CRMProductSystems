# TASK-625 — Realtime SignalR transport for consumer support tickets

**Status:** done · **Agent:** backend-developer · **Updated:** 2026-08-25
Plan: `goofy-bubbling-naur.md` §2 (extends the TASK-616 support-ticket channel). Read
`.claude/logs/tasks/616_2026-08-24_consumer-support-tickets_backend-developer.md` first.

## What changed

Layered a SignalR realtime push on top of TASK-616's REST-only support-ticket channel. REST
stays the only write path (message creation, status change) — SignalR only pushes an event after
the triggering `SaveChangesAsync` has already committed.

### 1. Hub URL

`/api/hubs/consumer-support` — final, under the `/api` prefix (matches every REST route). No
alternate `/hubs/...` mapping registered.

### 2. Hub methods

```
JoinTicket(ticketId: Guid)   // adds connection to group "consumer-support-ticket:{ticketId}"
LeaveTicket(ticketId: Guid)  // explicit exit; disconnect also drops all groups automatically
```
`JoinTicket` re-derives identity from the JWT on every call, never from client input:
consumer token → must own the ticket (`ticket.ConsumerAccountId` match); staff token → must be
role ≥ `store_manager` (`AppPolicies.AtLeastStoreManagerRoles`, same floor as the REST staff
inbox) **and** `ticket.TenantId` match. Any failure throws `HubException("Access denied.")` and
the connection is never added to the group.

### 3. Server events + exact payloads

`SupportMessageCreated` (after `POST .../messages` or `POST .../reply` commits):
```json
{ "ticketId": "uuid",
  "message": { "id": "uuid", "ticketId": "uuid", "senderConsumerAccountId": "uuid|null",
               "senderUserId": "uuid|null", "body": "string", "isRead": false, "createdAt": "ISO8601" } }
```
`message` is the exact same `ConsumerSupportTicketMessageDto` instance returned in the HTTP
response — `message.id` always matches.

`SupportTicketStatusChanged` (after `PUT .../status` commits):
```json
{ "ticketId": "uuid", "status": "open|in_progress|resolved|closed", "updatedAt": "ISO8601" }
```
Judgment call: the consumer-reply auto-reopen side effect (Resolved/Closed → Open, from TASK-616)
does **not** publish this event — spec text ties it explicitly to the `PUT .../status` endpoint
only; a reconnecting client picks up the implicit reopen via its own `GET` refetch.

Both events sent only to group `consumer-support-ticket:{ticketId}` via
`IHubContext<ConsumerSupportHub>.Clients.Group(...).SendAsync(...)`. Ticket creation
(`POST /api/consumer/support/tickets`) does **not** publish an event — spec §3's trigger list
names only the two message endpoints, and nobody is joined to a brand-new ticket's group yet
anyway.

### 4. JWT connection

Same bearer JWT as REST (`[Authorize]`, no policy — Hub accepts both a consumer token and a
staff token; the specific-identity check lives in `JoinTicket`, not a class-level policy).
WebSocket handshakes can't carry an `Authorization` header, so
`JwtBearerEvents.OnMessageReceived` (added in `Program.cs`) reads `?access_token=` from the query
string — but **only** when the request path starts with `/api/hubs/consumer-support`; every
other route still requires a real header, so this doesn't open a token-via-query-string hole
anywhere else (logs/proxies/history exposure risk). Transport: WebSocket first, SignalR's default
SSE/long-polling fallback otherwise (`HubConnectionBuilder` default — nothing pinned
server-side). `KeepAliveInterval`/`ClientTimeoutInterval`/`HandshakeTimeout` = 15s/30s/15s,
set explicitly in `AddSignalR()` (framework defaults, made explicit rather than implicit).

### 5. Architecture

`IConsumerSupportRealtimeNotifier` (Application layer,
`Features/CustomerSupport/IConsumerSupportRealtimeNotifier.cs`) — `ConsumerSupportService` calls
this, never `IHubContext` directly (CLAUDE.md's "AI integrations are isolated" layering spirit,
applied here to SignalR). Concrete `ConsumerSupportRealtimeNotifier`
(`Infrastructure/Realtime/`) wraps `IHubContext<ConsumerSupportHub>` and swallows/logs its own
exceptions — a publish failure must never turn an already-committed REST write into a failed HTTP
response (spec §5: SignalR is not a guaranteed-delivery store). `ConsumerSupportHub` itself also
lives in `Infrastructure/Realtime/` (not Api) specifically so the notifier implementation can
reference `IHubContext<ConsumerSupportHub>` without an Api→Infrastructure→Api cycle;
`Program.cs` only maps the endpoint (`app.MapHub<ConsumerSupportHub>(...)`), same thin-composition-root
role it already plays for everything else `AddInfrastructure`/`AddApplication` register.
Group-name string (`consumer-support-ticket:{ticketId:D}`) centralized in one `internal` helper
(`ConsumerSupportGroups`) shared by the Hub and the notifier so the two can't drift.

## Changed files

- `backend/ShelfGuard.Application/Features/CustomerSupport/IConsumerSupportRealtimeNotifier.cs` — new
- `backend/ShelfGuard.Application/Features/CustomerSupport/ConsumerSupportService.cs` — new ctor
  dependency + publish calls in `AddConsumerMessageAsync`/`AddStaffReplyAsync`/`UpdateStatusAsync`
- `backend/ShelfGuard.Infrastructure/Realtime/ConsumerSupportHub.cs` — new
- `backend/ShelfGuard.Infrastructure/Realtime/ConsumerSupportRealtimeNotifier.cs` — new
- `backend/ShelfGuard.Infrastructure/Realtime/ConsumerSupportGroups.cs` — new
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` — `AddSignalR()` +
  `IConsumerSupportRealtimeNotifier` registration
- `backend/ShelfGuard.Api/Program.cs` — `OnMessageReceived` query-string JWT fallback (Hub path
  only) + `app.MapHub<ConsumerSupportHub>("/api/hubs/consumer-support")`
- `backend/ShelfGuard.Tests/CustomerSupport/ConsumerSupportServiceTests.cs` — updated ctor call +
  11 new tests (publish-on-success for both message endpoints and status change; no-publish on
  validation failure, wrong-owner/wrong-tenant, and DB-save failure; payload `message.Id` matches
  the returned DTO)
- `backend/ShelfGuard.Tests/Realtime/ConsumerSupportHubTests.cs` — new, 10 tests (consumer
  joins own ticket / denied on another consumer's / denied on unknown ticket; staff joins own
  tenant's ticket / denied on another tenant's / denied below the store_manager floor for 3 roles;
  denied with neither claim, without even looking up the ticket; `LeaveTicket` removes from group)
- `.claude/docs/api-contracts.md` — new "Realtime — SignalR Hub (TASK-625)" subsection under
  Consumer support tickets
- `.claude/logs/handoffs/625-to-mobile-codex.md` — new, mobile hand-off

Not touched: `mobile/` (separate concurrent Codex agent owns it), `frontend/` (no web SignalR
client wired — out of scope per the brief), REST DTOs/response shapes (no changes, additive only).

## Test results

`dotnet build ShelfGuard.sln`: 0 errors, 1 pre-existing unrelated warning (Marketplace tests,
same one TASK-616 already noted).

`dotnet test` full suite: **1946/1946 passing** (21 new — 11 in
`ConsumerSupportServiceTests.cs`, 10 in the new `ConsumerSupportHubTests.cs` — up from the
1925/1925 TASK-624 baseline, zero regressions).

Hub tests are pure unit tests (`Hub.Context`/`Hub.Groups` have public setters specifically for
this) — no live WebSocket/TestServer exercised, so the `IHttpContextAccessor`-driven RLS session
variables (`TenantConnectionInterceptor`) were not verified against a live SignalR connection in
this session. `JoinTicket`'s access check does not rely on RLS for correctness regardless — it
loads the ticket via the repository and then explicitly compares `ConsumerAccountId`/`TenantId`
against the JWT-derived values in C#, so even if RLS/HttpContext flow behaved unexpectedly for a
WebSocket connection the worst case is a functional false-negative (a legitimate join wrongly
denied), never a cross-tenant/cross-consumer leak. Recommend one manual smoke test (real mobile
or `wscat`/browser client with a real JWT) before this ships to a live client, to confirm the
happy path end-to-end.
