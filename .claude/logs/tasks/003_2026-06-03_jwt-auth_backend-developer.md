# TASK-003: JWT authentication with refresh tokens

**Date:** 2026-06-03
**Agent:** backend-developer
**Status:** done
**Duration:** 1 session

## What was done

Implemented full JWT auth stack: login, refresh (rotating token), logout, GET /auth/me.

## Files changed

**New — Domain:**
- `ShelfGuard.Domain/Entities/Tenant.cs`
- `ShelfGuard.Domain/Entities/User.cs`
- `ShelfGuard.Domain/Entities/RefreshToken.cs`
- `ShelfGuard.Domain/Interfaces/IUserRepository.cs`
- `ShelfGuard.Domain/Interfaces/IRefreshTokenRepository.cs`

**New — Application:**
- `ShelfGuard.Application/Services/IPasswordHasher.cs`
- `ShelfGuard.Application/Services/IJwtService.cs`
- `ShelfGuard.Application/Features/Auth/Dtos/AuthDtos.cs`
- `ShelfGuard.Application/Features/Auth/IAuthService.cs`
- `ShelfGuard.Application/Features/Auth/AuthService.cs`

**New — Infrastructure:**
- `ShelfGuard.Infrastructure/Services/JwtService.cs`
- `ShelfGuard.Infrastructure/Services/BcryptPasswordHasher.cs`
- `ShelfGuard.Infrastructure/Data/Repositories/UserRepository.cs`
- `ShelfGuard.Infrastructure/Data/Repositories/RefreshTokenRepository.cs`
- `ShelfGuard.Infrastructure/Migrations/20260603181341_AddAuth.cs` (+ Designer)

**New — Api:**
- `ShelfGuard.Api/Controllers/AuthController.cs`

**New — Tests:**
- `ShelfGuard.Tests/Auth/AuthServiceTests.cs`

**Modified:**
- `AppDbContext.cs` — added Tenant, User, RefreshToken DbSets + fluent config
- `ShelfGuard.Infrastructure/DependencyInjection.cs` — registered JwtService, BcryptPasswordHasher, UserRepository, RefreshTokenRepository
- `ShelfGuard.Application/DependencyInjection.cs` — registered AuthService
- `ShelfGuard.Api/Program.cs` — added JWT auth middleware, AddAuthentication, AddAuthorization, AllowCredentials
- `appsettings.json` — added Jwt section
- `appsettings.Development.json` — added dev JWT secret + connection string

**Packages added:**
- Api: `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.0
- Infrastructure: `BCrypt.Net-Next` 4.0.3, `System.IdentityModel.Tokens.Jwt` 7.5.2

## Decisions made

- Refresh token is a cryptographically random 64-byte base64 value; SHA256 hash stored in DB (raw token travels over wire only, never persisted)
- Refresh token rotation: every `/auth/refresh` call revokes old token and issues a new one
- HttpOnly cookie for refresh token; Secure=false on localhost, Secure=true elsewhere
- `current_setting('app.tenant_id', true)` with `true` = "missing OK" avoids errors when RLS context not set (e.g. provider)
- Kept POC Product entities intact — no existing code deleted

## Tests

- Unit tests written: yes — 7 new in `AuthServiceTests`
- Build passes: yes — 0 errors, 0 warnings
- `dotnet test`: 13/13 passed

## Notes for next agent

TASK-004 (backend-developer): TenantInterceptor middleware — reads JWT, sets `app.tenant_id` PostgreSQL session variable so RLS filters automatically.
- JWT claims: `sub` = userId, `tenant_id` = tenantId (custom claim), `role` = role
- ClaimTypes used in controller: `ClaimTypes.NameIdentifier` (maps to `sub`), `ClaimTypes.Role`
- Custom claim name for tenant: `"tenant_id"` (string in the JWT payload)
- Connection to AppDbContext: inject IDbConnection or use `_db.Database.ExecuteSqlRawAsync` to SET LOCAL
