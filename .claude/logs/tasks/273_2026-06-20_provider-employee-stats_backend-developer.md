# TASK-273 — Provider Employee Statistics

**Date:** 2026-06-20
**Agent:** backend-developer + frontend-developer
**Status:** done

## Scope

Статистика продуктивності для кожного учасника провайдерської команди без нових таблиць.

## Backend

### New files
- `IProviderStatsRepository.cs` (Domain) — 4 методи: team members, assigned tickets, created-by-provider tickets, comment authors
- `ProviderStatsRepository.cs` (Infra) — cross-tenant queries via same null-UUID RLS pattern
- `IProviderStatsService.cs` (Application)
- `ProviderStatsService.cs` (Application) — агрегує в пам'яті після 3 SQL запитів

### Modified files
- `ProviderTeamDtos.cs` — `ProviderMemberStatsDto` (9 полів)
- `ProviderTeamController.cs` — `GET /api/provider/team/stats` (ProviderTeamMember policy)
- `DependencyInjection.cs` × 2 — реєстрація

### Metrics per member
- `TicketsAssigned` — призначені тікети
- `TicketsResolved` — вирішені з них
- `TicketsCreatedByProvider` — тікети, створені агентом від імені клієнта
- `CommentsWritten` — коментарі у тікетах
- `AvgResolutionHours` — середній час вирішення (nullable)

## Frontend

- `providerStatsApi.ts` — `getTeamStats()` → GET /api/provider/team/stats
- `useProviderStats.ts` — React Query hook
- `StatsTab.tsx` — таблиця з колонками, прогрес-бар resolve rate, кольорові статуси
- `provider/page.tsx` — нова вкладка "Статистика" з `BarChart2` іконкою

## Verification
- dotnet build green
- tsc green
