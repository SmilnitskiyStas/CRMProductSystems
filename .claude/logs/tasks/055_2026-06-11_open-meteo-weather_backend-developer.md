---
task_id: TASK-055
date: 2026-06-11
agent: backend-developer + devops-engineer
status: done
---

# TASK-055 — Open-Meteo Weather Integration (v2-spec §6)

## Files
| Layer | File |
|---|---|
| Domain | `Interfaces/{IOpenMeteoClient, IWeatherRepository}.cs` (DailyForecast record) |
| Infrastructure | `Integrations/OpenMeteoClient.cs` (isolated per architecture rule), `Data/Repositories/WeatherRepository.cs`, DI via `AddHttpClient` |
| Application | `Features/Weather/{IWeatherService, WeatherService, Dtos}.cs` + public `WeatherCoefficientResolver` |
| Api | `Controllers/WeatherController.cs` |
| Worker | `jobs/weather-fetch.job.ts` + cron `0 6 * * *` (`weather-fetch-cron`) |
| Tests | `Tests/Weather/WeatherCoefficientResolverTests.cs` — 5 tests |

## Endpoints (spec §9)
```
GET  /api/weather/{storeId}          → 7-day forecast        [AtLeastStoreManager]
GET  /api/weather/{storeId}/history  → ?from&to              [AtLeastStoreManager]
POST /api/weather/fetch              → manual trigger        [AtLeastNetworkManager]
GET  /api/weather/coefficients                               [AtLeastStoreManager]
POST /api/weather/coefficients       → create rule           [AtLeastStoreManager]
PUT  /api/weather/coefficients/{id}  → change multiplier     [AtLeastStoreManager]
```

## Behavior
- **Fetch:** for every active store with coordinates → Open-Meteo 7-day daily
  (temp max/min, precipitation, weathercode), upsert by (StoreId, Date);
  TempAvg computed; IsForecast = date ≥ today. Worker job duplicates this in
  Node for the 06:00 cron (worker owns crons; no service-token infra to call the API).
- **Rule matching:** condition = TempMax > TempAbove OR TempMax < TempBelow OR
  WeatherCode equal; scope = segment / category / global (both null).
  Matching rules multiply. Resolver is pure + unit-tested.
- **Order formula:** demandMultiplier = eventCoef × weatherCoef;
  both exposed per line (`eventCoefficient`, `weatherCoefficient`).

## Production e2e (real Kyiv weather, stores got coordinates 50.4501/30.5234)
- fetch → `{storesProcessed: 2, daysUpserted: 14, errors: []}`
- forecast: 2026-06-11 17.7..28.2°C, рекордна спека ✓
- hot rule (tempAbove 15 → ×1.5 global, demo):
  Вода k_event 2.0 × k_weather 1.5 → raw 227.91 → ORDER 228
  Гречка k_weather 1.5 → 100 ✓ (rule neutralized to 1.0 after test)

## Notes
- Transient DNS failure inside the api container on first fetch
  ("Name or service not known") — second attempt succeeded. If it recurs,
  add `dns: [8.8.8.8]` to the api service or HttpClient retry (Polly).
- 403 trap: /weather/fetch requires network_manager+ (store manager gets Forbid).
