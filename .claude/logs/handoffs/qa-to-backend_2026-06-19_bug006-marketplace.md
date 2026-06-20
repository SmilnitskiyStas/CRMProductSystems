# Handoff: qa-tester → backend-developer

**Date:** 2026-06-19  
**Bug:** BUG-006 — Marketplace public endpoints return 403 for anonymous callers

## What to fix

`backend/ShelfGuard.Api/Controllers/MarketplaceController.cs`

Remove `[RequireModule("marketplace")]` from these 4 `[AllowAnonymous]` actions:

```csharp
// GET suppliers listing
[HttpGet("suppliers")]
[AllowAnonymous]
// DELETE this line: [RequireModule("marketplace")]

// GET supplier by id
[HttpGet("suppliers/{id:guid}")]
[AllowAnonymous]
// DELETE this line: [RequireModule("marketplace")]

// GET supplier items
[HttpGet("suppliers/{id:guid}/items")]
[AllowAnonymous]
// DELETE this line: [RequireModule("marketplace")]

// POST search
[HttpPost("search")]
[AllowAnonymous]
// DELETE this line: [RequireModule("marketplace")]
```

Keep `[RequireModule("marketplace")]` on:
- `POST /api/marketplace/suppliers/{id}/reviews` (Authorize)
- `POST /api/marketplace/ai-recommend` (Authorize)

## Why

`RequireModuleFilter` reads `tenant_id` from JWT. Anonymous requests have no JWT → null tenant_id → immediate 403. `[AllowAnonymous]` doesn't suppress action filters — only the auth middleware.

## After fix

`dotnet build` + `dotnet test` must stay green (all 459 pass). No new tests needed — existing `RequireModuleFilterTests.MissingTenantClaim_Returns403` still covers the authenticated case.

**Manual verify:** `GET /api/marketplace/suppliers` without Authorization header → 200 (empty list is fine if no seed data).

## Bug log

`.claude/logs/reviews/bug006_2026-06-19_marketplace-anonymous-blocked.md`
