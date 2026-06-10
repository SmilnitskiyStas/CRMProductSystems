---
task_id: BUG-001
date: 2026-06-10
agent: security-reviewer
status: done
---

# BUG-001 — RLS Tenant Leakage via Connection Pool

## Problem
Provider user (no `tenant_id` JWT claim) could read all rows of any tenant's catalog.

**Root cause:** `TenantConnectionInterceptor.BuildSetSql()` only called `SET app.tenant_id = '...'`
when a valid UUID was present in the claim. For provider users (no claim), the SET was skipped.
PostgreSQL connection pooling means a reused connection retains the previous request's
`app.tenant_id` session variable. RLS policy `USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)`
then matched that stale tenant's rows — returning full catalog data.

## Fix
`backend/ShelfGuard.Infrastructure/Interceptors/TenantConnectionInterceptor.cs`

`BuildSetSql()` now **always** emits `SET app.tenant_id = '...'`:
- Valid UUID present → set to that UUID (normal tenant user path)
- Absent or invalid → set to `'00000000-0000-0000-0000-000000000000'` (null UUID)

The null UUID never matches any real `TenantId` → RLS returns 0 rows → provider sees `[]`.

## Tests updated
`ShelfGuard.Tests/Infrastructure/TenantConnectionInterceptorTests.cs`

Old tests asserted `null` return or absence of `app.tenant_id` for no-claim cases.
Updated to assert null-UUID is always set. All 13 tests pass.

## Verification
- `dotnet build` → 0 Warnings, 0 Errors
- `dotnet test --filter TenantConnection` → 13/13 Passed
