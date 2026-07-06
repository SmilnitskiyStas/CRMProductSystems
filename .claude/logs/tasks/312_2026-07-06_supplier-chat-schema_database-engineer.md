# TASK-312: Supplier ↔ Client chat — schema

**Agent:** database-engineer
**Date:** 2026-07-06
**Plan:** `calm-singing-marble.md`, Частина 2

## Scope
Schema only — entities + migration. No services/controllers (next: backend-developer).

## What was done
- New entities: `backend/ShelfGuard.Domain/Entities/SupplierChatSession.cs`,
  `SupplierChatMessage.cs` (plain mutable classes, styled after `ChatSession`/`ChatMessage`
  but without Subject/Status/AssignedAgentName/Rating/IsSystem — two-tenant persistent
  thread, no close concept).
- `AppDbContext.cs`: `DbSet<SupplierChatSession>`, `DbSet<SupplierChatMessage>` + Fluent
  config (tables `supplier_chat_sessions`, `supplier_chat_messages`; unique index on
  `(SupplierTenantId, ClientTenantId)`; FK `SupplierTenantId` → tenants CASCADE,
  `ClientTenantId` → tenants RESTRICT — two FKs to the same table can't both cascade;
  `SessionId` → supplier_chat_sessions CASCADE).
- Migration `20260706110628_AddSupplierChat`: tables, indexes (SupplierTenantId,
  ClientTenantId, unique pair, SessionId, CreatedAt), plus hand-added RLS SQL:
  - `supplier_chat_sessions`: `tenant_isolation` — `SupplierTenantId = NULLIF(...)::uuid OR
    ClientTenantId = NULLIF(...)::uuid` (NULLIF guard per known-issues.md), `provider_bypass`,
    `FORCE ROW LEVEL SECURITY`.
  - `supplier_chat_messages`: `tenant_isolation` via `EXISTS` subquery against the parent
    session's tenant pair (pattern from `notification_settings`/`FixRlsAndForeignKeys`),
    `provider_bypass`, `FORCE ROW LEVEL SECURITY`.
  - `Down()` drops policies/disables RLS before dropping tables.
- Docs: `.claude/docs/database-schema.md` — new "v4.2" section.

## Verification
- `dotnet build`: 0 warnings, 0 errors.
- `dotnet test`: 575/575 passed.
- Migration **not applied** to any database (per instructions) — only generated and
  build-verified.

## Notes
- Had to `taskkill` a stray `ShelfGuard.Api` process holding a file lock on first build
  attempt (unrelated to this change).
