---
task_id: TASK-054
date: 2026-06-11
agent: database-engineer + backend-developer
status: done
---

# TASK-054 — Demand Events Calendar (v2-spec §4)

## Files
| Layer | File |
|---|---|
| Domain | `Entities/{DemandEvent, DemandEventCoefficient, WeatherData, WeatherCoefficient}.cs`, `Interfaces/IEventRepository.cs` |
| Infrastructure | Migration `V2EventsWeather` (4 tables + RLS), `Data/Repositories/EventRepository.cs` |
| Application | `Features/Events/{IEventService, EventService, Dtos}.cs` (+ `DefaultHolidays`), `Features/Orders/OrderCalcService.cs` (+ `EventCoefficientResolver`) |
| Api | `Controllers/EventsController.cs` |
| Tests | `Tests/Events/EventCoefficientResolverTests.cs` — 6 tests (15/15 with formula suite) |

## Endpoints (spec §9, `AtLeastStoreManager`)
```
GET/POST           /api/events            (?from ?to ?store_id)
GET/PUT/DELETE     /api/events/{id}
GET/POST           /api/events/{id}/coefficients
PUT                /api/events/{id}/coefficients/{coefId}
POST               /api/events/seed-defaults    (idempotent)
```

## Key behaviors
- **Recurring events** match by month/day each year, incl. windows wrapping over
  New Year (25 Dec – 2 Jan) — `DemandEvent.IsActiveOn`.
- **Coefficient resolution:** within one event most specific scope wins
  (product > segment > category); across events multipliers multiply (§3).
- **Order formula integration:** `OrderFormula` takes `demandMultiplier`,
  applied to positive Raw before MOQ/USQ rounding; exposed as `eventCoefficient`
  in order lines.
- **Seeded holidays:** Новий рік, Великдень (movable — window per seed year),
  8 березня, Початок школи, День Незалежності — with spec §4 default coefficients.
  Seeded category coefficients have ScopeId=null until linked to real categories via UI.
- Weather tables (weather_data via store-join RLS, weather_coefficients) created
  in the same migration — ready for TASK-055.

## Production e2e
- seed-defaults → 5 events / 13 coefficients; re-run → 0/0 (idempotent) ✓
- Local event today + product coefficient ×2 →
  Вода: raw 75.97 → **151.94 (k=2.0) → ORDER 152**; Гречка k=1 → 67 unchanged ✓
