# Backend Structure

**Owner:** backend-developer
**Updated:** 2026-06-04

## Layer Responsibilities
```
ShelfGuard.Api          — HTTP routing, auth middleware, DI wiring — no business logic
ShelfGuard.Application  — Use cases, business rules, DTOs, service interfaces
ShelfGuard.Domain       — Entities, value objects, repository interfaces, domain rules
ShelfGuard.Infrastructure — EF Core, repositories, Claude API, Telegram, BullMQ producers
```

## Dependency Direction
```
Api → Application → Domain
Infrastructure → Application, Domain
(Infrastructure implements Domain interfaces)
```

## Service Pattern
- Interface defined in `Application`, implementation in `Application`
- Repository interface in `Domain`, implementation in `Infrastructure`
- Example: `IAuthService` (Application) ← `AuthService` (Application); `IUserRepository` (Domain) ← `UserRepository` (Infrastructure)

## Tenant Context
`TenantConnectionInterceptor` (EF Core `DbConnectionInterceptor`) fires on every connection open.
Reads JWT claims, validates role whitelist, sets `app.tenant_id` and `app.role` PostgreSQL session variables.
All DB queries automatically filtered by RLS — application layer never filters by tenant manually.

## Startup Sequence (Program.cs)
1. `db.Database.MigrateAsync()` — auto-apply pending migrations (dev convenience)
2. `DbSeeder.SeedAsync(db)` — insert demo data if tenants table is empty
3. Swagger only in Development
4. Middleware: CORS → Authentication → Authorization → Controllers

> ⚠️ KI-006: Steps 1+2 run in all environments. Should be guarded with `IsDevelopment()` before production.

## Migration Commands
```bash
cd backend/
dotnet ef migrations add <Name> --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api
dotnet ef database update --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api
```
> Stop the running API process before running these — DLL locking will fail the build.

## Migration History
| Migration | Tables |
|---|---|
| InitialCreate | Products (POC) |
| AddAuth | tenants, users, refresh_tokens + RLS |
| FullSchema | 19 new v1 tables + RLS + FEFO indexes |

## Feature Implementation Status
| Feature | Controller | Service | Repository | Migration |
|---|---|---|---|---|
| Auth | ✅ AuthController | ✅ AuthService | ✅ UserRepository, RefreshTokenRepository | ✅ AddAuth |
| Products (POC) | ✅ ProductsController | ✅ ProductService | ✅ ProductRepository | ✅ InitialCreate |
| Catalog | ✅ CatalogController | ✅ CatalogProductService | ✅ CatalogProductRepository | ✅ FullSchema |
| Stock | ✅ StockController | ✅ StockService | ✅ StockRepository | ✅ FullSchema |
| Stock | 🕐 | 🕐 | 🕐 | ✅ FullSchema |
| Receipts | 🕐 | 🕐 | 🕐 | ✅ FullSchema |
| Transfers | 🕐 | 🕐 | 🕐 | ✅ FullSchema |
| Write-offs | 🕐 | 🕐 | 🕐 | ✅ FullSchema |
| Stores/Zones | 🕐 | 🕐 | 🕐 | ✅ FullSchema |
| Analytics | 🕐 | 🕐 | 🕐 | — |
| Notifications | 🕐 | 🕐 | 🕐 | ✅ FullSchema |
