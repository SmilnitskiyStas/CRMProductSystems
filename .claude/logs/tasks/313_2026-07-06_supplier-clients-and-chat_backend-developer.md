# TASK-313 — Supplier cabinet: Clients tab + Supplier↔client chat (backend)

**Agent:** backend-developer
**Date:** 2026-07-06
**Plan:** `calm-singing-marble.md`, Частина 1 + Частина 2
**Status:** done (backend only; frontend follows in TASK-314+)

## Scope

Backend-only implementation (Application + Api layers, C#). Schema (entities,
migration, RLS) was already delivered by database-engineer in TASK-312.

## Частина 1 — Clients tab

- `SupplierPermissions.ClientManagement = "client_management"` added to
  `backend/ShelfGuard.Domain/Constants/SupplierPermissions.cs` (`All` updated).
- `ISupplierTaskRepository.GetDistinctClientTenantsAsync(tenantId)` (new) —
  distinct `ClientTenantId`s from the supplier's tasks, with `TaskCount` and
  `LastTaskAt` (max CreatedAt), joined with tenant display names.
  Implemented in `SupplierTaskRepository.cs`.
- `SupplierCabinetService.GetClientsAsync(tenantId)` (new) — merges:
  - reviews via existing `IMarketplaceRepository.GetReviewsBySupplierAsync`/
    `CountReviewsBySupplierAsync` (fetched in full, no new pagination —
    per-supplier volume doesn't justify SQL-side aggregation, per plan)
  - tasks via the new repo method above
  In-memory union keyed by client tenant id: `ReviewCount`, `AvgRating`
  (rounded to 2 decimals), `TaskCount`, `LastInteractionAt` (max of both
  sources' dates).
- DTO `SupplierClientDto(TenantId, TenantName, ReviewCount, AvgRating,
  TaskCount, LastInteractionAt)` in `MarketplaceDtos.cs`.
- `GET /api/supplier-cabinet/clients` on `SupplierCabinetController`, same
  `AppPolicies.SupplierCabinet` auth as the rest of the cabinet.

## Частина 2 — Supplier↔client chat

- `ISupplierChatRepository`/`SupplierChatRepository` (new,
  `Domain/Interfaces` + `Infrastructure/Data/Repositories`) — session
  get/get-by-id/add, session list (joined with other-party tenant name +
  last message preview), message list/add, tenant display name lookup.
  Relies on standard TenantConnectionInterceptor RLS (tenant_isolation
  policy matches either `SupplierTenantId` or `ClientTenantId` — no
  provider-role bypass needed for either side).
- `ISupplierChatService`/`SupplierChatService` (new,
  `Application/Features/Marketplace`):
  - `GetOrCreateSessionAsync(myTenantId, otherTenantId, isSupplierSide,
    createdByUserId)` — maps my/other tenant onto Supplier/Client columns
    depending on `isSupplierSide`; race-safe create (catches
    `DbUpdateException`-class failures on the unique pair index, re-fetches
    the winner).
  - `GetSessionsAsync(tenantId, isSupplierSide)` — session list with the
    other party's tenant id/name resolved per side.
  - `GetMessagesAsync(sessionId, callerTenantId)` — 403-style
    `AccessDeniedError` if caller's tenant isn't either side of the session.
  - `SendMessageAsync(sessionId, senderTenantId, senderUserId, senderName,
    body)` — validates non-blank body, 4000-char cap (`MaxBodyLength`,
    matches entity `HasMaxLength`), tenant-membership guard.
  - DTOs: `SupplierChatSessionDto`, `SupplierChatMessageDto`,
    `SendSupplierChatMessageRequest` in `MarketplaceDtos.cs`.
- Supplier-side endpoints on `SupplierCabinetController` (same
  `AppPolicies.SupplierCabinet`):
  - `GET /api/supplier-cabinet/chat/sessions`
  - `GET /api/supplier-cabinet/chat/sessions/{clientTenantId}/messages`
  - `POST /api/supplier-cabinet/chat/sessions/{clientTenantId}/messages`
- Client-side: new `MarketplaceChatController` (`api/marketplace`, plain
  `[Authorize]` + `[RequireModule("marketplace")]`, tenant id/user id
  resolved from JWT claims exactly like `MarketplaceController`):
  - `GET /api/marketplace/suppliers/{supplierId}/chat/messages`
  - `POST /api/marketplace/suppliers/{supplierId}/chat/messages`
  - Resolves `supplierId` → supplier's tenant id via new
    `IMarketplaceRepository.GetSupplierTenantIdAsync(supplierId)`
    (provider-bypass read, mirrors `GetSupplierByRawIdAsync`).

## DI

- `Application/DependencyInjection.cs`: `ISupplierChatService` →
  `SupplierChatService`.
- `Infrastructure/DependencyInjection.cs`: `ISupplierChatRepository` →
  `SupplierChatRepository`.

## Tests

- `SupplierCabinetServiceTests.cs`: +2 (`GetClientsAsync` merge across
  review-only/task-only/shared tenants incl. avg-rating rounding and
  max-date selection; no-owner-managed-profile error path).
- `SupplierChatServiceTests.cs` (new, 12 tests): get-or-create (existing
  session reuse, new session creation, both-side tenant mapping),
  get-messages (happy path, access-denied, session-not-found),
  send-message (happy path, blank body, over-length body, access-denied,
  session-not-found), get-sessions (both sides' other-tenant mapping).

## Verification

- `dotnet build`: 0 errors, 0 new warnings (1 pre-existing unrelated
  warning in `MarketplaceServiceTests.cs`, untouched by this task).
- `dotnet test`: 590/590 green (575 baseline + 15 new).
- Migration `20260706110628_AddSupplierChat` applied to local dev DB
  (docker compose, port 5435, db `crm`) via
  `dotnet ef database update --project ShelfGuard.Infrastructure
  --startup-project ShelfGuard.Api --connection "Host=localhost;Port=5435;
  Database=crm;Username=crm;Password=crm_dev_password"` (had to pass
  `--connection` explicitly — the design-time host did not pick up
  `appsettings.Development.json`'s connection string on this machine even
  with `ASPNETCORE_ENVIRONMENT=Development` set; runtime `dotnet run` picks
  it up fine, so this only affects the `dotnet-ef` CLI path). Verified via
  `psql \d` that `supplier_chat_sessions`/`supplier_chat_messages` and both
  RLS policies (`tenant_isolation`, `provider_bypass`, FORCE RLS) exist as
  specified in the TASK-312 handoff.
- **Not done:** a live end-to-end curl round-trip (send as supplier tenant,
  read as client tenant). A backend dev server was already running on
  port 5000 from outside this session; the sandbox correctly blocked
  killing/replacing it without the user's confirmation. Build/tests/schema
  are verified; the actual HTTP round-trip is still open — recommend
  qa-tester or the user re-run it against a running instance.

## Files touched

- `backend/ShelfGuard.Domain/Constants/SupplierPermissions.cs`
- `backend/ShelfGuard.Domain/Interfaces/ISupplierTaskRepository.cs`
- `backend/ShelfGuard.Domain/Interfaces/IMarketplaceRepository.cs`
- `backend/ShelfGuard.Domain/Interfaces/ISupplierChatRepository.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/SupplierTaskRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/MarketplaceRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/SupplierChatRepository.cs` (new)
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/Dtos/MarketplaceDtos.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/ISupplierCabinetService.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/SupplierCabinetService.cs`
- `backend/ShelfGuard.Application/Features/Marketplace/ISupplierChatService.cs` (new)
- `backend/ShelfGuard.Application/Features/Marketplace/SupplierChatService.cs` (new)
- `backend/ShelfGuard.Application/DependencyInjection.cs`
- `backend/ShelfGuard.Api/Controllers/SupplierCabinetController.cs`
- `backend/ShelfGuard.Api/Controllers/MarketplaceChatController.cs` (new)
- `backend/ShelfGuard.Tests/Marketplace/SupplierCabinetServiceTests.cs`
- `backend/ShelfGuard.Tests/Marketplace/SupplierChatServiceTests.cs` (new)
