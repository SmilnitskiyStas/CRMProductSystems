# CF-002, CF-003, CF-005 — QA Findings Fix

**Date:** 2026-06-19  
**Agent:** backend-developer  
**Status:** done

## Summary

Fixed three QA code findings in backend only. Build: 0 errors, 0 warnings.

---

## CF-002 — WeekStart must be Monday

**File:** `backend/ShelfGuard.Application/Features/Schedules/ScheduleService.cs`

Added validation in `CreateScheduleAsync` immediately after the name check:

```csharp
if (dto.WeekStart.DayOfWeek != DayOfWeek.Monday)
    return (null, "WeekStart must be a Monday.");
```

---

## CF-003 — Category and Priority validation

**File:** `backend/ShelfGuard.Application/Features/ServiceDesk/TicketService.cs`

Added validation in `CreateAsync` after description check. Values taken from constants already defined in `SupportTicket.cs`:

- **Category** — `SupportTicketCategory`: general, technical, billing, feature_request, bug
- **Priority** — `SupportTicketPriority`: low, medium, high, critical

Validation is permissive of null/empty (uses defaults from entity), but rejects invalid non-empty values.

---

## CF-005 — UNIQUE constraint on (TenantId, LocationId, WeekStart)

### 1. AppDbContext — partial unique index

**File:** `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`

Replaced non-unique browse index `idx_work_schedules_tenant_location` with a partial unique index:

```csharp
e.HasIndex(s => new { s.TenantId, s.LocationId, s.WeekStart })
 .IsUnique()
 .HasFilter("\"Status\" <> 'archived'")
 .HasDatabaseName("uq_work_schedules_tenant_location_week");
```

Archived schedules are excluded so historical data can be kept without blocking new schedules for the same week.

### 2. IScheduleRepository — new method

**File:** `backend/ShelfGuard.Domain/Interfaces/IScheduleRepository.cs`

```csharp
Task<bool> ScheduleExistsForWeekAsync(Guid tenantId, Guid locationId, DateOnly weekStart, CancellationToken ct);
```

### 3. ScheduleRepository — implementation

**File:** `backend/ShelfGuard.Infrastructure/Data/Repositories/ScheduleRepository.cs`

Filters `Status != "archived"` to mirror the partial index condition.

### 4. ScheduleService — pre-insert duplicate check

**File:** `backend/ShelfGuard.Application/Features/Schedules/ScheduleService.cs`

Added after location-exists check in `CreateScheduleAsync`:

```csharp
if (await _repo.ScheduleExistsForWeekAsync(tenantId, dto.LocationId, dto.WeekStart, ct))
    return (null, "A schedule for this location and week already exists.");
```

Returns a 409-compatible error string before hitting the DB UNIQUE constraint.

### 5. Migration generated

`backend/ShelfGuard.Infrastructure/Migrations/20260619155730_AddWorkScheduleUniqueConstraint.cs`

Drops `idx_work_schedules_tenant_location` and creates `uq_work_schedules_tenant_location_week` (partial unique).

---

## Files Changed

| File | Change |
|------|--------|
| `ShelfGuard.Application/Features/Schedules/ScheduleService.cs` | CF-002: Monday check; CF-005: duplicate check |
| `ShelfGuard.Application/Features/ServiceDesk/TicketService.cs` | CF-003: category + priority validation |
| `ShelfGuard.Domain/Interfaces/IScheduleRepository.cs` | CF-005: new `ScheduleExistsForWeekAsync` method |
| `ShelfGuard.Infrastructure/Data/Repositories/ScheduleRepository.cs` | CF-005: implementation |
| `ShelfGuard.Infrastructure/Data/AppDbContext.cs` | CF-005: partial unique index |
| `ShelfGuard.Infrastructure/Migrations/20260619155730_AddWorkScheduleUniqueConstraint.cs` | CF-005: migration |
