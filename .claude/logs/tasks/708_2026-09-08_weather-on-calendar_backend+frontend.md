# TASK-708 — Weather on the events calendar

**Status:** review · main session (orchestrator) + backend-developer & frontend-developer agents ·
verified in browser · not committed/pushed

Per-day temperature on the `/events` calendar cells + detailed weather in the day-detail drawer +
address→coordinates geocoding for locations. No DB migration (`weather_data` and
`Location.Latitude/Longitude` already exist), no change to backend authorization.

User decisions: geocode from address (Nominatim) with manual override; calendar weather follows
the global store selector (shown only when exactly one store selected); full archive for the
viewed month + forecast, on-demand fetch cached in `weather_data`.

---

## Backend (agent + orchestrator follow-up)

### Capability A — address → coordinates
- `POST /api/locations/geocode` `{ "query": "<address>" }` →
  `200 { latitude, longitude, displayName }` | `404 { "error": "Не вдалося визначити координати за адресою." }`.
  Policy `AtLeastStoreManager`.
- New `Domain/Interfaces/IGeocodingClient.cs` (`GeocodeAsync` + `record GeocodeResult`).
- New `Infrastructure/Integrations/NominatimGeocodingClient.cs` — OSM Nominatim
  `search?q=&format=jsonv2&limit=1&accept-language=uk&countrycodes=ua`, per-request
  `User-Agent: ShelfGuard/1.0 (+https://agrusystems.pp.ua)`; `null` on empty / non-2xx /
  unparseable. `countrycodes=ua` added by orchestrator after a live test once resolved a
  loosely-phrased Cyrillic address to Brittany, FR.
- DI next to Open-Meteo; `LocationService.GeocodeAddressAsync` (+ iface, DTOs, controller) — no
  persistence, the form PUTs lat/lon back normally. `LocationService` ctor += `IGeocodingClient`.

### Capability B — weather for a calendar month
- `GET /api/weather/{locationId}/month?year=&month=` → `200 List<WeatherDayDto>`
  (`date` yyyy-MM-dd, tempMin/tempMax/tempAvg, precipitation, weatherCode, isForecast).
  Class policy `AtLeastStoreManager`, no `[RequireModule]` (KI-019).
- `WeatherService.GetMonthAsync`: guard month 1..12 / year 2000..2100 → `[]`; no coords → `[]`;
  read stored `weather_data` for the month (`IWeatherRepository.GetRangeAsync` — new, ignores
  `IsForecast`); refetch a day if missing or (for `date >= today-1`) `FetchedAt` > 6h; serviceable
  window `[today-92, today+16]` — one forecast call (`past_days`+`forecast_days`) or archive API
  for months entirely older; upsert only needed dates (`IsForecast = date >= today`,
  `TempAvg = round((min+max)/2, 1)`); re-read + return sorted. Open-Meteo failure → log + return
  stored, never 500.
- `IOpenMeteoClient` += `GetRangeAsync` / `GetArchiveAsync` (shared `FetchDailyAsync` parser,
  invariant-culture coords); `GetForecastAsync` (worker path) unchanged.
  `IWeatherRepository` += `GetRangeAsync` / `GetLocationAsync`. `WeatherService` ctor += `ILogger`.
- RLS: `weather_data` `tenant_isolation` already allows user-context read+write for own
  locations — upsert runs on the normal session, no bypass primitive.

New/changed: `IGeocodingClient.cs`, `NominatimGeocodingClient.cs`, `WeatherServiceGetMonthTests.cs`
(new); `LocationsController`, `WeatherController`, `LocationDtos`, `I/LocationService`,
`I/WeatherService`, `IOpenMeteoClient`, `OpenMeteoClient`, `IWeatherRepository`, `WeatherRepository`,
`DependencyInjection`, `LocationService{,GetAllScope}Tests` (changed).

---

## Frontend (agent)

- New `frontend/features/weather/` — `types.ts` (`WeatherDay`), `api/weather.ts`
  (`getMonth(locationId, year, month)`), `hooks/useWeatherMonth.ts` (RQ key
  `["weather","month",locationId,year,month]`, `enabled: !!locationId`, `staleTime 30m`),
  `weatherCodes.ts` (WMO code → bucket; `weatherCodeBucket`, `weatherBucketEmoji`,
  `weatherBucketLabelKey`, `weatherCodeInfo(code, locale?)` fallback).
- New `frontend/features/events/components/WeatherDayCard.tsx` — dark card at the top of the
  day-detail drawer list view; header `emoji + label` + amber «прогноз» / grey «факт» badge;
  rows Max/Min/Avg °C + Precipitation mm (rounded to 1 dp by orchestrator), each omitted when null.
- `EventCalendar.tsx` — new `weatherByDate?: Map<string, WeatherDay>`; day-number row now flex
  `space-between` with a compact `{emoji} {round(max)}° / {round(min)}°` opposite the number
  (muted, 10px); forecast days `opacity 0.7` + dot. Empty-cell branch untouched.
- `EventDayDetailDrawer.tsx` — new `weather?: WeatherDay` → `<WeatherDayCard>` in the list view.
- `app/(dashboard)/events/page.tsx` — `weatherLocationId = selectedStoreIds.length === 1 ?
  selectedStoreIds[0] : undefined`; `useWeatherMonth`; `weatherByDate` memo → calendar + drawer;
  hint `hintPickOneStore` when selection ≠ 1, `hintNoCoords` when a store is selected but the
  settled query returned `[]`.
- `features/locations/api/locations.ts` — `latitude?`/`longitude?` on **both** `CreateLocationDto`
  and `UpdateLocationDto` (**latent-bug fix**: `UpdateLocationDto` omitted them, so every location
  edit nulled the coords server-side), new `geocode(query)`.
- `features/locations/components/LocationFormDialog.tsx` — lat/lon in zod schema / defaultValues /
  edit `reset` / payload / `Props.onSubmit`; «Широта»/«Довгота» number inputs + «Визначити
  координати» button → `locationsApi.geocode`, `setValue` + `displayName` caption + error toast;
  disabled while pending / address blank.
- `app/(dashboard)/locations/page.tsx` — `handleSubmit` values type + `create` payload forward
  lat/lon (update path already forwards the whole values object).
- `messages/{uk,en}.json` — `Dashboard.events.weather.*` (`hint*`, `*Badge`, `temp*`,
  `precipitation`, `precipitationValue`, `codes.*` ×11) + `Dashboard.locations.form.{latitude*,
  longitude*,geocode*}`. Parity kept.

---

## Verification

- Backend: `dotnet build ShelfGuard.sln` clean (0 warn / 0 err after stopping the dev server that
  held a DLL lock); `dotnet test --filter ~Weather|~Location` **86/86**; `~AiOrder|~OrderCalc` 22/22.
- Frontend: `npx tsc --noEmit` clean, `npm run lint` clean, `npx vitest run` 59/59, `npx next build` ok.
- Browser (local stack, tenant «Свіжий Кут», location given Kyiv coords 50.4501/30.5234):
  - `POST /api/locations/geocode {"query":"Хрещатик 22, Київ"}` → 200 with correct Kyiv coords + displayName.
  - `PUT /api/locations/{id}` with lat/lon → persisted (confirms the latent-bug fix).
  - `GET /api/weather/{id}/month?year=2026&month=9` → 23 days: actual Sep 1-7, forecast Sep 8+,
    nothing Sep 25-30 (beyond +16 forecast — expected).
  - `/events` calendar cells show `{emoji} {max}°/{min}°`; click a day → `WeatherDayCard` with the
    right condition (⛈️ Thunderstorm / 🌦️ Drizzle), `actual`/`forecast` badge, all temp + precip rows.
  - No single store selected → hint «Select a single store in the switcher above to see weather», no temps.
  - No weather-related console errors (pre-existing `Dashboard.locations.types.shop` MISSING_MESSAGE
    is a stale QA location with `Type:"shop"`, unrelated).

## Pending

- openapi.json regen — repo-wide chore (KI-040).
- Location form's coord fields + geocode button not visually confirmed in-browser (needs
  `enterprise_admin`; the session stayed logged in as `store_manager`) — code reviewed, endpoint
  verified.
