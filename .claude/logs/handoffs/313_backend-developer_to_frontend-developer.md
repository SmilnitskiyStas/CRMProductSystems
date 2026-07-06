# Handoff: Clients tab + Supplier↔client chat backend → Frontend

**From:** backend-developer (TASK-313)
**To:** frontend-developer
**Date:** 2026-07-06
**Plan:** `calm-singing-marble.md`, Частина 1 + Частина 2

## New permission

`SupplierPermissions.ClientManagement = "client_management"` (gates both the
Clients tab and chat, per plan). Same nav-gate pattern as `/supplier/team`,
`/supplier/tasks`.

## Частина 1 — Clients tab

`GET /api/supplier-cabinet/clients` (auth: `AppPolicies.SupplierCabinet`, same
as every other `/api/supplier-cabinet/*` endpoint — send the normal JWT).

Response: `SupplierClientDto[]`, sorted by `lastInteractionAt` descending.

```ts
type SupplierClientDto = {
  tenantId: string;          // Guid
  tenantName: string;
  reviewCount: number;
  avgRating: number | null;  // null when reviewCount === 0
  taskCount: number;
  lastInteractionAt: string; // ISO datetime (DateTimeOffset)
};
```

404 body `{ error: "Supplier cabinet is not available for this tenant." }` if
the caller's tenant has no owner-managed supplier (same as every other
cabinet endpoint).

## Частина 2 — Chat

Two independent surfaces sharing the same session/message shapes.

```ts
type SupplierChatSessionDto = {
  id: string;              // Guid, session id — needed for chat panel state, not for routing (routes use the OTHER party's id, see below)
  otherTenantId: string;   // the other side's tenant id
  otherTenantName: string;
  createdAt: string;
  updatedAt: string;
  lastMessage: string | null;
  lastMessageAt: string | null;
};

type SupplierChatMessageDto = {
  id: string;
  sessionId: string;
  senderTenantId: string;  // compare to "my" tenantId to decide mine/theirs — works identically on both sides
  senderUserId: string;
  senderName: string;
  body: string;
  isRead: boolean;
  createdAt: string;
};

// POST body for sending a message on either side:
type SendSupplierChatMessageRequest = { body: string };
```

Validation errors from `POST .../messages` come back as `400` with
`{ error: string }` — body empty (`"Message body is required."`) or over
4000 chars (`"Message body cannot exceed 4000 characters."`).

### Supplier side (in `SupplierCabinetController`, `AppPolicies.SupplierCabinet`)

- `GET /api/supplier-cabinet/chat/sessions` → `SupplierChatSessionDto[]`
  (all threads with clients, `otherTenantId`/`otherTenantName` = the client).
  Use this to render the conversation list, e.g. from the Clients tab's
  "Написати" button you'd normally already know the `clientTenantId` (row
  data) rather than needing this list — this endpoint is for a dedicated
  "all my conversations" view if you want one.
- `GET /api/supplier-cabinet/chat/sessions/{clientTenantId}/messages` →
  `SupplierChatMessageDto[]`, oldest first. **Auto-creates the session** on
  first call — no separate "start conversation" call needed. Call this when
  the supplier clicks "Написати" on a client row in `ClientsTab.tsx`.
- `POST /api/supplier-cabinet/chat/sessions/{clientTenantId}/messages` with
  `SendSupplierChatMessageRequest` → `201` + `SupplierChatMessageDto`. Also
  auto-creates the session if it doesn't exist yet.

### Client side (new `MarketplaceChatController`, route `api/marketplace`, plain `[Authorize]` + `marketplace` module — same auth as leaving a review)

- `GET /api/marketplace/suppliers/{supplierId}/chat/messages` →
  `SupplierChatMessageDto[]`. `supplierId` is the **public supplier id**
  (same id used everywhere else in the marketplace UI, e.g.
  `/marketplace/[id]/page.tsx`), NOT a tenant id — resolved server-side.
  Auto-creates the session. Returns `404 { error: "Supplier not found." }`
  if the supplierId doesn't exist.
- `POST /api/marketplace/suppliers/{supplierId}/chat/messages` with
  `SendSupplierChatMessageRequest` → `201` + `SupplierChatMessageDto`.

### Suggested polling pattern

Existing reference: `ClientChatPanel.tsx` (`frontend/features/chat/`) already
does 3000ms polling via React Query with no SignalR — same pattern applies
here on both sides. Poll the messages endpoint on an interval, invalidate on
send.

### UI wiring per the plan (calm-singing-marble Частина 2)

- Supplier side: new `SupplierClientChatPanel.tsx`, opened from a row in
  `ClientsTab.tsx` (Частина 1) — needs `clientTenantId` from that row.
- Client side: "Написати постачальнику" button on
  `frontend/app/(dashboard)/marketplace/[id]/page.tsx`, opens the same kind
  of panel wired to the client-side API (`supplierId` = the `[id]` route
  param already on that page).
- Gate supplier-side chat + Clients tab nav entry behind `client_management`
  permission, same pattern as `staff_management`/`task_board`.

## Verification status from backend

`dotnet build`/`dotnet test` green (590/590). Migration applied to local dev
DB, RLS policies confirmed present. **Not yet verified**: a live end-to-end
HTTP round-trip (send as supplier, read as client) — a backend dev server
was already occupying port 5000 outside this session and I didn't want to
force-restart it. Recommend doing a manual smoke test once you're wired up
against a running backend: send from the supplier cabinet UI, confirm it
shows up on the client-side marketplace page and vice versa.
