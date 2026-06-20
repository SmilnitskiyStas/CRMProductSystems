# TASK-274 — Provider Schedule (розклад команди)

**Date:** 2026-06-20
**Agent:** backend-developer + frontend-developer
**Status:** done

## Scope

Тижневий розклад доступності для агентів провайдерської команди (recurring slots, не конкретні дати).

## Backend

### New files
- `ProviderScheduleSlot.cs` (Domain entity) — UserId, DayOfWeek (0=Mon..6=Sun), StartTime, EndTime, Notes, IsActive
- `IProviderScheduleRepository.cs` (Domain) — GetAll (filter by userId), GetById, Add, Remove, Save
- `ProviderScheduleDtos.cs` (Application) — `ProviderScheduleSlotDto`, `CreateProviderScheduleSlotRequest`
- `IProviderScheduleService.cs` (Application)
- `ProviderScheduleService.cs` (Application) — валідує DayOfWeek, time format, end > start, перевіряє userId провайдерський
- `ProviderScheduleRepository.cs` (Infra) — Include(User), order by UserId → DayOfWeek → StartTime
- `ProviderScheduleController.cs` (Api) — GET (optionally ?userId=), POST [ProviderCanInvite], DELETE [ProviderCanInvite]
- Migration `AddProviderScheduleSlots` — table `provider_schedule_slots`, FK→users CASCADE

### Modified files
- `AppDbContext.cs` — DbSet + model config
- `DependencyInjection.cs` × 2

## Frontend

- `providerScheduleApi.ts` — 3 функції
- `useProviderSchedule.ts` — 3 hooks (get/create/delete)
- `ScheduleTab.tsx` — 7-колонковий grid (Пн-Нд), SlotPill з кольором по ролі, AddSlotModal (select member + day + time)
- `provider/page.tsx` — нова вкладка "Розклад" з `Calendar` іконкою

## Business rules
- DayOfWeek 0-6 (0=Monday)
- StartTime/EndTime в форматі "HH:mm"
- EndTime > StartTime
- Тільки provider team members можуть отримати slot

## Verification
- dotnet build green
- EF Core migration generated: `AddProviderScheduleSlots`
- tsc green
