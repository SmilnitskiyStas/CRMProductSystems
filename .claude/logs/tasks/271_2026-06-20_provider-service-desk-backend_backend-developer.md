# TASK-271 — Backend: Provider cross-tenant Service Desk

**Agent:** backend-developer
**Date:** 2026-06-20
**Status:** done

## Summary

Provider (власник системи) + його агенти повинні мати змогу:
1. Бачити тікети з усіх клієнтських тенантів в одному списку
2. Створювати тікет на конкретного клієнта — тікет відображається як у Провайдера, так і у клієнта

## Root cause (чому не працювало раніше)

`ServiceDeskController.GetAll` → `GetTenantId()` читав `tenant_id` з JWT.
Provider JWT не містить `tenant_id` → returns `null` → `Forbid()` в кожному ендпоінті.

## Рішення

Окремий контролер `AdminServiceDeskController` з `[Authorize(Policy = AppPolicies.ProviderOnly)]`
+ новий стек репозиторій/сервіс що обходить RLS (паттерн `TenantAdminRepository`).

Тікет зберігається з:
- `TenantId = dto.TargetTenantId` (клієнтський тенант) → клієнт бачить у своєму `/api/service-desk`
- `CreatedByProvider = true` → ознака що тікет ініційований Провайдером

## Нові файли

### Domain
- `ShelfGuard.Domain/Interfaces/IProviderTicketRepository.cs`

### Application
- `ShelfGuard.Application/Features/ServiceDesk/ProviderServiceDeskDtos.cs`
  - `ProviderTicketListItemDto` (includes `TenantId`, `TenantName`, `CreatedByProvider`)
  - `CreateProviderTicketDto` (includes `TargetTenantId`)
- `ShelfGuard.Application/Features/ServiceDesk/IProviderTicketService.cs`
- `ShelfGuard.Application/Features/ServiceDesk/ProviderTicketService.cs`

### Infrastructure
- `ShelfGuard.Infrastructure/Data/Repositories/ProviderTicketRepository.cs`
  — cross-tenant queries via JOIN з `tenants` таблицею, без RLS фільтру

### API
- `ShelfGuard.Api/Controllers/AdminServiceDeskController.cs`
  - `GET  /api/admin/service-desk?status=&tenantId=`
  - `POST /api/admin/service-desk`

### Migration
- `20260620114403_AddTicketCreatedByProvider`
  — `CreatedByProvider boolean NOT NULL DEFAULT false` на `support_tickets`

## Оновлені файли

- `SupportTicket.cs` — `CreatedByProvider bool { get; init; } = false`
- `AppDbContext.cs` — `e.Property(t => t.CreatedByProvider).HasDefaultValue(false)`
- `Application/DependencyInjection.cs` — `IProviderTicketService → ProviderTicketService`
- `Infrastructure/DependencyInjection.cs` — `IProviderTicketRepository → ProviderTicketRepository`

## API endpoints

### GET /api/admin/service-desk
```
Query: status? (string), tenantId? (Guid)
Response: List<ProviderTicketListItemDto>
Auth: ProviderOnly
```

### POST /api/admin/service-desk
```
Body: { targetTenantId, title, description, category?, priority? }
Response: 201 ProviderTicketListItemDto
Auth: ProviderOnly
```

## Acceptance criteria
- [x] dotnet build — 0 errors, 0 warnings
- [x] 459/459 тестів green
- [x] EF migration generated correctly
- [x] Provider JWT (no tenant_id) → 200 on GET /api/admin/service-desk
- [x] Created ticket visible to client in /api/service-desk (same TenantId)
- [x] Created ticket has CreatedByProvider = true
