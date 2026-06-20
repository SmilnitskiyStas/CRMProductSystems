# Bug: BUG-006 — Marketplace public endpoints blocked by RequireModuleFilter

**Date:** 2026-06-19  
**Severity:** critical  
**Task:** TASK-221  
**Reporter:** qa-tester

## Steps to Reproduce

```http
GET /api/marketplace/suppliers
(no Authorization header — anonymous request)
```

## Expected

```
200 OK
{ "items": [...], "totalCount": 0, ... }
```

## Actual

```
403 Forbidden
{ "error": "Module not activated" }
```

## Root Cause

`MarketplaceController.cs` decorates public endpoints with both `[AllowAnonymous]` and `[RequireModule("marketplace")]`:

```csharp
[HttpGet("suppliers")]
[AllowAnonymous]
[RequireModule("marketplace")]  // ← this runs for anonymous requests too
```

`RequireModuleFilter` reads `tenant_id` from JWT claims. Anonymous requests have no JWT → no `tenant_id` → the filter short-circuits with 403 before the action executes.

`[AllowAnonymous]` only skips the ASP.NET Core authentication middleware (401 checks). It does NOT suppress `IAsyncActionFilter` instances — they always run.

## Affected Endpoints

```
GET  /api/marketplace/suppliers
GET  /api/marketplace/suppliers/{id}
GET  /api/marketplace/suppliers/{id}/items
POST /api/marketplace/search
```

## Fix

Remove `[RequireModule("marketplace")]` from the four `[AllowAnonymous]` endpoints in `MarketplaceController.cs`.

Public discovery endpoints have no tenant context by design — module gating is not applicable. Keep `[RequireModule]` on the two authenticated endpoints (`POST /reviews`, `POST /ai-recommend`).

## Files to Change

`backend/ShelfGuard.Api/Controllers/MarketplaceController.cs` — lines 37, 60, 84, 93
