# TASK-019 — Analytics API
**Date:** 2026-06-04  
**Agent:** backend-developer  
**Status:** done

## What was implemented

All 6 analytics endpoints from v1-spec.md §5 Analytics:

| Endpoint | Description |
|---|---|
| `GET /api/analytics/expiry-summary` | Stock status counts (safe/warning/critical/expired) per store or network-wide |
| `GET /api/analytics/write-offs` | Write-off stats grouped by reason and date |
| `GET /api/analytics/movements` | Stock movement stats grouped by type |
| `GET /api/analytics/by-zone` | Per-zone batch status breakdown |
| `GET /api/analytics/by-category` | Per-category batch status and quantity breakdown |
| `GET /api/analytics/losses` | Total write-off losses with per-store breakdown |

## Files created

- `ShelfGuard.Application/Features/Analytics/Dtos/AnalyticsDtos.cs` — 6 response DTOs
- `ShelfGuard.Application/Features/Analytics/IAnalyticsService.cs`
- `ShelfGuard.Application/Features/Analytics/IAnalyticsRepository.cs` — in Application (not Domain) because it returns DTOs, not entities
- `ShelfGuard.Application/Features/Analytics/AnalyticsService.cs` — thin delegate
- `ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs` — all SQL aggregation logic
- `ShelfGuard.Api/Controllers/AnalyticsController.cs`

## Files modified

- `AppPolicies.cs` — added `CanViewAnalytics` policy (store_manager and above, per spec §3.2)
- `ShelfGuard.Application/DependencyInjection.cs` — registered `IAnalyticsService`
- `ShelfGuard.Infrastructure/DependencyInjection.cs` — registered `IAnalyticsRepository`

## Architecture note

`IAnalyticsRepository` lives in the Application layer (not Domain) because analytics queries return DTO aggregates, not domain entities. This avoids a circular dependency (Domain → Application is not allowed).
