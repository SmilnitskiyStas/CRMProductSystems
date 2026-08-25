# Handoff: TASK-625 → mobile (Codex agent)

**From:** Claude session, backend-only implementation (backend-developer TASK-625). Extends the
TASK-613/616 consumer support-ticket channel already handed off in
`.claude/logs/handoffs/623-to-mobile-codex.md` (§3 there covers the REST side — read that first
if you haven't wired the ticket screens yet; this document assumes REST create/list/get/messages
already work and adds realtime push on top).

**Full contract source:** `.claude/docs/api-contracts.md`, subsection "Realtime — SignalR Hub
(TASK-625)" under "Consumer support tickets (TASK-613/616)". This file is a curated extract for
mobile wiring — if the two ever disagree, `api-contracts.md` is the source of truth.

This document is written to stand alone — you do not need to read the conversation that produced
it.

## What this adds

The mobile app currently gets new ticket messages by polling (per the orchestrating brief for
this task — polling code lives somewhere under `mobile/features/consumer-support/`, which this
backend session did not touch). This task adds a SignalR Hub so the backend can push new
messages and status changes instead. **Polling removal is explicitly this task's mobile
follow-up, not done here** — this backend session only builds the Hub; wiring the mobile client
to it and then deleting the poll loop is a separate step for you.

REST is unchanged and still does all the writing:
```
POST /api/consumer/support/tickets/{id}/messages   -- consumer sends a message (unchanged)
POST /api/customer-support/tickets/{id}/reply       -- staff replies (unchanged, staff-side, FYI only)
PUT  /api/customer-support/tickets/{id}/status      -- staff changes status (unchanged, staff-side, FYI only)
```
SignalR only tells you when one of those has happened — it never accepts a write itself.

## Connecting

```
Hub URL:    /api/hubs/consumer-support   (relative to the same API base URL every other
                                           /api/consumer/* call already uses)
Auth:       same ConsumerAccount JWT as every other consumer request (Authorization: Bearer ...
                                           does NOT work for the SignalR handshake — see below)
```

Standard `@microsoft/signalr` client usage:
```ts
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${API_BASE_URL}/api/hubs/consumer-support`, {
    accessTokenFactory: () => getStoredConsumerJwt(), // same token useConsumerAuth/store.ts already holds
  })
  .withAutomaticReconnect() // exponential backoff by default — fine as-is, no custom retry policy needed
  .build();

await connection.start();
```
`accessTokenFactory` is the standard SignalR mechanism — the client library itself appends the
token as `?access_token=<jwt>` on the connection URL (that's how it gets past the WebSocket
handshake, which can't carry a custom header). You do not need to build that query string
yourself; just supply the factory. The backend only honors this query-string fallback on this
exact Hub path — it will not work if you try the same trick against a REST endpoint.

## Subscribing to a ticket thread

```ts
await connection.invoke("JoinTicket", ticketId); // call every time a thread screen opens
// ... screen is open, listening for events ...
await connection.invoke("LeaveTicket", ticketId); // call when the thread screen closes/unmounts
```
`JoinTicket` can reject — wrap in try/catch:
```ts
try {
  await connection.invoke("JoinTicket", ticketId);
} catch (err) {
  // SignalR HubException "Access denied." — this ticket isn't yours (shouldn't happen if the
  // ticket id came from your own GET .../support/tickets list, but treat it the same as any
  // other 403/404 you'd get from a REST call: don't crash, just don't show the thread).
}
```
**Reconnect behavior (important for correctness, not just style):**
- `withAutomaticReconnect()` handles the socket-level reconnect, but group membership does NOT
  survive a reconnect — call `JoinTicket(ticketId)` again for the currently-open thread every
  time `connection.onreconnected(...)` fires (or after any manual reconnect you build yourself).
- After every reconnect (auto or manual), also re-fetch the ticket via
  `GET api/consumer/support/tickets/{id}` — SignalR replays nothing sent while disconnected. This
  is the same "GET is the source of truth" pattern you'd already want for a first load; just run
  it again on reconnect, not only on initial screen mount.

## Listening for events

```ts
connection.on("SupportMessageCreated", (payload: {
  ticketId: string;
  message: ConsumerSupportTicketMessageDto; // same shape POST .../messages already returns
}) => {
  if (payload.ticketId !== currentTicketId) return; // defensive; you only joined one group anyway
  appendMessageIfNew(payload.message); // de-dupe on message.id — see below
});

connection.on("SupportTicketStatusChanged", (payload: {
  ticketId: string;
  status: "open" | "in_progress" | "resolved" | "closed";
  updatedAt: string; // ISO 8601
}) => {
  updateTicketStatus(payload.ticketId, payload.status);
});
```

**De-duplication is required, not optional.** The event can arrive back to whoever sent the
message (if you're joined to the group when you send your own message via REST, you'll get your
own `SupportMessageCreated` echoed back). Key your message list by `message.id` and ignore an
event whose id you already have — the same discipline you'd want anyway for "optimistic local
message + server confirmation" UI, just make sure the SignalR echo path also respects it, not
just the REST-response path.

**One gap to be aware of, not a bug to report:** a consumer's own reply reopening a
Resolved/Closed ticket (existing TASK-616 server behavior) does **not** emit
`SupportTicketStatusChanged` — that event is wired only to the staff `PUT .../status` endpoint.
If you show ticket status in the thread header, refresh it from the `POST .../messages` HTTP
response's own `status`-adjacent ticket data if you need it live after sending your own message
(or just re-GET on next screen focus) — don't wait on a SignalR event that won't come for this
specific case.

## What's explicitly NOT changed by this task

- REST request/response shapes — identical to what TASK-616's handoff already documented.
- Staff-side web UI — untouched, out of scope for this task.
- No push notifications (APNs/FCM) — this is in-app-only realtime, requires the app to hold an
  open SignalR connection (or be foregrounded) to receive events. A backgrounded/killed app still
  needs the existing poll-or-refresh-on-open pattern as a fallback; deciding how long to keep the
  socket open in the background is a mobile-side product/battery tradeoff this backend task
  doesn't prescribe.

## DTO shapes (unchanged from the TASK-616 handoff, repeated here for convenience)

```ts
type ConsumerSupportTicketMessageDto = {
  id: string; ticketId: string;
  senderConsumerAccountId: string | null; senderUserId: string | null; // exactly one set
  body: string; isRead: boolean; createdAt: string;
};
```
