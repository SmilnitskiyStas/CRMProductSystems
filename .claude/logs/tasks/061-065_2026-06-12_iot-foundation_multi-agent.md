---
task_id: TASK-061..065
date: 2026-06-12
agent: project-architect + database-engineer + devops-engineer + backend-developer + frontend-developer
status: done (live e2e passed on local stack 2026-06-12)
---

# Sprint v3.1 «IoT Foundation» — v3-spec §6 Фаза 1

ADR-010 (decisions.md): MQTT ingestion lives in the Node worker, API is CRUD/read-only.

## TASK-061 — DB schema (database-engineer)
- Entities: `IotDevice`, `TemperatureReading`, `WeightReading` + AppDbContext config
- Migration `V3IotFoundation`: 3 tables, UNIQUE(TenantId, DeviceId),
  idx (DeviceId, RecordedAt DESC) ×2, partial idx Processed=false
- RLS: iot_devices direct; readings via EXISTS join to iot_devices (+provider_bypass)
- `dotnet build` green. `database update` pending DB availability

## TASK-062 — Mosquitto (devops-engineer)
- docker-compose: `mosquitto` service (eclipse-mosquitto:2), host port 1884→1883 (ADR-004),
  healthcheck via mosquitto_sub, persistent volume, `infra/mosquitto/mosquitto.conf`
- worker gets `MQTT_URL=mqtt://mosquitto:1883`; dev allows anonymous (prod: password_file)
- Removed obsolete `version:` key from compose. Pub/sub smoke pending Docker

## TASK-063 — iot_devices CRUD API (backend-developer)
- `Features/IoT/{IIotDeviceService,IotDeviceService,Dtos}` + `IotController` (api/iot)
- 7 endpoints (см. api-contracts.md §IoT); device_id unique per tenant; soft delete;
  IsOnline = LastSeenAt < 30 min; latest-per-device temperature for store
- `IIotDeviceRepository` + EF repository; DI registered both layers
- Tests: `Tests/IoT/IotDeviceServiceTests.cs` — 15/15 green

## TASK-064 — Worker MQTT listener (backend-developer)
- `services/iot-rules.ts` — pure rules: assessWeightDelta (95/85/60, auto ≥70),
  tempAlertThreshold (fridge +8 / freezer -12 / config.alert_above), constants
- `jobs/mqtt-listener.ts` — subscribe shelfguard/#, resolve device by payload.device_id,
  update last_seen/battery; temp → readings + alert → notifications queue + sustained-2h
  violation → product_stock 'temp_violation' + stock_event; weight → readings +
  stock_event('sensor') always + FEFO write-down when confident (FOR UPDATE, sold_out on 0)
- Offline watchdog: 15-min interval, alert once per episode (in-memory dedup)
- notification.job: new payloads temp_alert (manager+director, TG) / iot_offline (manager, TG)
  with per-channel logging (TASK-042 semantics). `tsc --noEmit` green

## TASK-065 — Web /iot dashboard (frontend-developer)
- `features/iot/{types,api,hooks,components}`: DevicesTable (online/offline, battery,
  firmware), DeviceFormDialog (zod, config JSON validation), TemperaturePanel
  (latest cards + 24h recharts line)
- Page `/iot` with store switcher; sidebar «IoT пристрої» (AT_LEAST_STORE_MANAGER)
- `next build` green — route 12.9 kB

## Live e2e — PASSED (local stack, 2026-06-12)
Docker engine recovered via `wsl --shutdown` + Docker Desktop restart.
1. ✅ Migration applied; RLS verified: 3 tables rowsecurity=t, 6 policies
2. ✅ Mosquitto pub/sub smoke on shelfguard/# (healthcheck green)
3. ✅ temp 9.5°C (profile fridge, поріг 8) → temperature_readings IsAlert=true;
   notification job → per-channel rows `telegram / iot.temp_alert / skipped (no chat_id)`
4. ✅ weight delta −490 г (unit 245) → confidence 85, units 2 → stock_event 'sensor'
   QuantityDelta −2 → FEFO: найстарша партія (exp 2026-06-08) 18→16
5. ✅ last_seen_at + battery оновлюються з кожним повідомленням
Test data cleaned up afterwards (devices, events, queue rows; quantity restored).

## Bugs found & fixed during e2e
- `parseDeviceConfig`: pg повертає jsonb як object, не string → JSON.parse падав,
  конфіг ставав {} (поріг/вага не застосовувались). Fix: приймає object|string.
- `logNotifications` SQL: параметр $6 у двох контекстах → "inconsistent types
  deduced for parameter $6". Fix: явний `$6::text` cast.
