# Handoff: TASK-003 → TASK-004

**Date:** 2026-06-03
**From:** backend-developer
**To:** backend-developer
**Task:** TASK-004 — TenantInterceptor (RLS middleware)

## What was completed

TASK-003 done. Full JWT auth stack:
- POST /api/auth/login → accessToken (body) + refreshToken (HttpOnly cookie)
- POST /api/auth/refresh → rotates refresh token, returns new accessToken
- POST /api/auth/logout → revokes refresh token
- GET /api/auth/me → returns current user info (requires [Authorize])
- 13/13 tests pass, 0 build warnings

## What to do next

Implement `TenantMiddleware` (or `TenantInterceptor` as IDbCommandInterceptor):
1. Read the JWT from `HttpContext.User` — extract claim `"tenant_id"` and `ClaimTypes.Role`
2. After EF Core opens a connection, execute: `SET LOCAL app.tenant_id = '{tenantId}'; SET LOCAL app.role = '{role}';`
3. This activates PostgreSQL RLS automatically for all queries in that request
4. Provider users (role = "provider") have no tenant_id claim — SET app.role = 'provider' so provider_bypass policy activates

## Implementation options

**Option A (recommended): EF Core IDbCommandInterceptor**
- Implement `DbCommandInterceptor`, override `ReaderExecutingAsync`, `NonQueryExecutingAsync`, `ScalarExecutingAsync`
- In each, prepend `SET LOCAL app.tenant_id = '...'; SET LOCAL app.role = '...';` before executing
- Register in `AddDbContext` options: `.AddInterceptors(new TenantInterceptor(...))`

**Option B: ASP.NET Core middleware**
- Middleware reads JWT claims, calls `_db.Database.ExecuteSqlRawAsync("SET LOCAL app.tenant_id = ...")`
- Simpler but requires injecting AppDbContext into middleware (scoped lifetime issue)

## Important context

- JWT custom claims: `"tenant_id"` (Guid as string) and `ClaimTypes.Role` (= `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`)
- Provider users: `User.FindFirstValue("tenant_id")` returns null → skip tenant_id SET, only set role = 'provider'
- RLS SQL: `SET LOCAL` scopes to the current transaction; use `SET` if you don't use transactions per-request
- The `current_setting('app.tenant_id', true)` in RLS policies uses `true` = "missing OK" to avoid errors on unauthenticated requests

## Risks / Blockers

- `SET LOCAL` only works within a transaction — ensure EF Core uses an ambient transaction or switch to `SET` (session-scoped)
- Anonymous endpoints (login, refresh) must NOT set tenant_id (no JWT available)
- The interceptor needs access to IHttpContextAccessor — register it as a singleton

## Files to review

- `backend/ShelfGuard.Infrastructure/Migrations/20260603181341_AddAuth.cs` — RLS policy definitions
- `backend/ShelfGuard.Api/Program.cs` — where to register middleware/interceptor

## Definition of done

- Authenticated requests automatically filter all DB queries by tenant_id via RLS
- Provider role bypasses RLS (sees all tenant data)
- Anonymous requests to /auth/login work without errors
- `dotnet build` exits 0, existing 13 tests pass
