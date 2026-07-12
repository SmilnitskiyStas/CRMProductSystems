# Handoff 341 → 342 (backend-developer)

Schema for ADR-019 temporary permission grants is done (TASK-341). Exact names to build
`AuthService.BuildEffectivePermissionsAsync` + `UsersController` endpoints +
`worker/src/jobs/permission-grant-expiry.job.ts` against:

## Table
`user_permission_grants` (EF entity `UserPermissionGrant`,
`backend/ShelfGuard.Domain/Entities/UserPermissionGrant.cs`)

Columns (PascalCase in DB, EF Core default): `Id`, `TenantId`, `UserId` (recipient),
`PermissionKey` (varchar 100), `ExpiresAt` (timestamptz, NOT NULL — always set),
`GrantedByUserId` (NOT NULL), `GrantedAt` (default NOW()), `RevokedAt` (nullable),
`RevokedByUserId` (nullable), `NotifiedExpiringAt` (nullable), `NotifiedExpiredAt`
(nullable).

Entity methods (private setters, mutate only through these):
- `UserPermissionGrant.Create(tenantId, userId, permissionKey, expiresAt, grantedByUserId)`
- `grant.Revoke(revokedByUserId)`
- `grant.MarkNotifiedExpiring()` / `grant.MarkNotifiedExpired()`
- `grant.IsActive` — computed: `RevokedAt is null && ExpiresAt > DateTime.UtcNow`

## Indexes
- `idx_user_permission_grants_tenant_user` on `(TenantId, UserId)` — the JWT-mint merge
  lookup.
- `idx_user_permission_grants_expires_active` — **partial** index on `ExpiresAt WHERE
  "RevokedAt" IS NULL` — for the worker's expiry scan.

## RLS
Standard `tenant_isolation` + `provider_bypass` (NULLIF guard), same as every table since
ADR-016. A DB session with `app.role = 'provider'` sees rows across every tenant — needed
if you query cross-tenant with `tenantId = null` (see below).

## Repository — `IUserPermissionGrantRepository` (`Domain/Interfaces/`),
implementation `UserPermissionGrantRepository` (`Infrastructure/Data/Repositories/`),
registered in `DependencyInjection.cs`:

```csharp
Task<IReadOnlyList<UserPermissionGrant>> GetActiveGrantsForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
Task<UserPermissionGrant?> GetByIdAsync(Guid tenantId, Guid grantId, CancellationToken ct = default);
Task AddAsync(UserPermissionGrant grant, CancellationToken ct = default);
Task<bool> RevokeAsync(Guid tenantId, Guid grantId, Guid revokedByUserId, CancellationToken ct = default); // false if not found/already revoked
Task<IReadOnlyList<UserPermissionGrant>> GetExpiringSoonAsync(Guid? tenantId, TimeSpan window, CancellationToken ct = default); // ExpiresAt in (now, now+window], RevokedAt null, NotifiedExpiringAt null
Task<IReadOnlyList<UserPermissionGrant>> GetJustExpiredAsync(Guid? tenantId, CancellationToken ct = default); // ExpiresAt <= now, RevokedAt null, NotifiedExpiredAt null
Task MarkNotifiedExpiringAsync(Guid grantId, CancellationToken ct = default);
Task MarkNotifiedExpiredAsync(Guid grantId, CancellationToken ct = default);
Task SaveChangesAsync(CancellationToken ct = default);
```

`GetExpiringSoonAsync`/`GetJustExpiredAsync` accept `tenantId = null` for a cross-tenant
scan — none of the *_grants-specific mutation methods (`RevokeAsync`) call `SaveChangesAsync`
for you; call it explicitly after mutating, same as `RefreshTokenRepository`.

## Notes for your implementation
- Per ADR-019 §4, the actual worker cron (`permission-grant-expiry.job.ts`) runs in Node
  and most likely queries Postgres directly (existing worker convention — see
  `fiscalization-retry.job.ts`), **not** through this C# repository. The
  `GetExpiringSoonAsync`/`GetJustExpiredAsync`/`MarkNotified*Async` methods exist per the
  original brief in case a C#-side consumer needs them (e.g. an admin preview endpoint) —
  confirm with the actual worker design before assuming they're the delivery path.
- Delete-behavior judgment calls made at the schema layer (documented in the task log,
  `.claude/logs/tasks/341_2026-07-12_user-permission-grants-schema_database-engineer.md`):
  `UserId` FK → Cascade, `GrantedByUserId` → Restrict, `RevokedByUserId` → SetNull.
- `User.Permissions` (`UserService.cs`) is completely untouched — this table is additive
  only, per ADR-019.
