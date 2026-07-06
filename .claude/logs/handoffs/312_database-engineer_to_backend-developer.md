# Handoff: Supplier ↔ Client chat schema → Backend

**From:** database-engineer (TASK-312)
**To:** backend-developer
**Date:** 2026-07-06
**Plan:** `calm-singing-marble.md`, Частина 2

## What's ready

### `SupplierChatSession` (`backend/ShelfGuard.Domain/Entities/SupplierChatSession.cs`)
`Id` (Guid), `SupplierTenantId` (Guid), `ClientTenantId` (Guid), `CreatedByUserId` (Guid),
`CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset), `Messages` (collection nav).
One persistent thread per (SupplierTenantId, ClientTenantId) pair — unique index enforces
this, so "get or create" must upsert on that pair, not create new rows per conversation.
No Status/close — just keep appending messages.

### `SupplierChatMessage` (`backend/ShelfGuard.Domain/Entities/SupplierChatMessage.cs`)
`Id` (Guid), `SessionId` (Guid), `SenderUserId` (Guid), `SenderTenantId` (Guid),
`SenderName` (string, max 200), `Body` (string, max 4000), `IsRead` (bool), `CreatedAt`
(DateTimeOffset), `Session` (nav).

`SenderTenantId` is the key to "mine vs. theirs" on the frontend: compare against the
caller's own tenant id (`SenderTenantId == myTenantId`) — works identically whether the
viewer is the supplier or the client, no need to track "which side" separately.

### Tables / DbSets
`AppDbContext.SupplierChatSessions`, `AppDbContext.SupplierChatMessages` →
`supplier_chat_sessions`, `supplier_chat_messages`. Migration:
`20260706110628_AddSupplierChat` — **not applied to any DB yet**, run
`dotnet ef database update` against your dev DB before testing.

### FKs / indexes already in place
- `supplier_chat_sessions`: unique `(SupplierTenantId, ClientTenantId)`; FK
  `SupplierTenantId` → tenants (CASCADE), `ClientTenantId` → tenants (RESTRICT — can't
  have two cascade paths to the same `tenants` table); idx on both tenant columns.
- `supplier_chat_messages`: FK `SessionId` → supplier_chat_sessions (CASCADE); idx on
  `SessionId`, `CreatedAt`.

### RLS (both sides can read/write; provider bypasses everything)
- `supplier_chat_sessions.tenant_isolation`: visible if either
  `SupplierTenantId` or `ClientTenantId` matches `app.tenant_id` (NULLIF-guarded cast).
- `supplier_chat_messages.tenant_isolation`: `EXISTS` subquery joining back to the
  parent session's tenant pair (no direct tenant column on the message itself).
- `provider_bypass` present on both, `FORCE ROW LEVEL SECURITY` set on both — **provider
  role sees everything**, same convention as every other `supplier_*` table. Nothing
  extra needed for provider access; just don't add tenant-scoping logic in the service
  layer that would accidentally exclude the provider path.

## What to build next (per plan)

- `ISupplierChatService`/`SupplierChatService`: `GetOrCreateSessionAsync(myTenantId,
  otherTenantId, isSupplierSide)`, `GetSessionsAsync(tenantId, isSupplierSide)`,
  `GetMessagesAsync(sessionId, tenantId)`, `SendMessageAsync(sessionId, tenantId, userId,
  senderName, body)`. One service, differentiate side by which tenant column matches.
- Supplier-side endpoints on `SupplierCabinetController.cs`:
  `GET /api/supplier-cabinet/chat/sessions`,
  `GET /api/supplier-cabinet/chat/sessions/{clientTenantId}/messages`,
  `POST /api/supplier-cabinet/chat/sessions/{clientTenantId}/messages`.
- Client-side endpoints — new `MarketplaceChatController` or method on
  `MarketplaceController.cs`: `GET /api/marketplace/suppliers/{supplierId}/chat/messages`,
  `POST /api/marketplace/suppliers/{supplierId}/chat/messages`. Resolve `supplierId` →
  supplier's tenant id via `IMarketplaceRepository` (reverse lookup similar to
  `GetOwnerManagedProfileAsync`).
- Gate supplier-side with the new `SupplierPermissions.ClientManagement` permission
  (Частина 1 of the plan, also assigned to backend-developer) — same
  `AppPolicies.SupplierCabinet` base auth.

## Verify before you start
- `dotnet build` / `dotnet test`: 0 warnings/errors, 575/575 tests green as of this
  handoff.
- Migration not applied anywhere — run `dotnet ef database update` against your dev DB
  first.
