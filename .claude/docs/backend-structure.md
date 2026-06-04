# Backend Structure

**Owner:** backend-developer
**Updated:** 2026-06-03

## Layer Responsibilities
ShelfGuard.Api:         HTTP routing, auth middleware, DI wiring — no business logic
ShelfGuard.Application: Use cases, business rules, DTOs, service interfaces
ShelfGuard.Domain:      Entities, value objects, repository interfaces, domain rules
ShelfGuard.Infrastructure: EF Core, repositories, Claude API, Telegram, email

## Dependency Direction
Api -> Application -> Domain
Infrastructure -> Application, Domain
(Infrastructure implements Domain interfaces)

## Service Pattern
Interface in Application, implementation in Application.
Repository interface in Domain, implementation in Infrastructure.

## Tenant Context
TenantInterceptor (middleware) reads JWT, sets app.tenant_id in PostgreSQL session.
All DB queries automatically filtered by RLS.

## Current Projects
Backend uses ShelfGuard.* naming. Solution: backend/ShelfGuard.sln
EF Core migrations reside in ShelfGuard.Infrastructure/Migrations/.
To add a migration: dotnet ef migrations add <Name> --project ShelfGuard.Infrastructure --startup-project ShelfGuard.Api
