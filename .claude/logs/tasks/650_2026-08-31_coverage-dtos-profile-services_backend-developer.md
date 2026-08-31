# TASK-650 (T3) — coverage DTOs + `DeliveryCoverageJson` + profile read/write + order region snapshot

**Agent:** backend-developer · **Date:** 2026-08-31 · **Status:** done
**Branch:** `main` (main working tree) · Plan: `eventual-whistling-rabbit.md` §Backend API, T3 of T1–T16
**Depends on:** T1 (`UkraineRegions`), T2 (`20260831090731_AddSupplierPerformanceData`) — both on `main`.

## Scope delivered

### DTOs — `Features/Marketplace/Dtos/MarketplaceDtos.cs`
- New: `DeliveryCoverageEntryDto(RegionCode, Terms?)`, `DeliveryCoverageDto(Served[], NotServed[], Note?)`,
  `RegionDeliveryStatDto(RegionCode, AvgDeliveryDays, SampleSize)`,
  `SupplierCoverageForBuyerDto(Coverage, BuyerRegionCode?, BuyerRegionStatus, BuyerRegionTerms?,
  MeasuredAvgDeliveryDaysToBuyerRegion?, MeasuredSampleSize?)` — `BuyerRegionStatus` ∈ `"served"|"not_served"|"unknown"`.
- `SupplierProfileDto` += `DeliveryCoverageDto? DeliveryCoverage` (last param, optional). **Not premium-gated** —
  populated for every caller. Legacy `DeliveryRegions` kept (deprecated, fed from the obsolete column).
- `SupplierMetricsDto` += `IReadOnlyList<RegionDeliveryStatDto>? DeliveryByRegion`, `int? DeliverySampleSize`,
  `int? ResponseSampleSize`, `DateTimeOffset? AggregatesComputedAt` (all appended optional — existing 7-arg
  positional constructions in `MarketplaceController` / `SupplierCabinetService` still compile).
- `SupplierProfileUpdateDto` + `CabinetProfileUpdateDto` += `DeliveryCoverageDto? DeliveryCoverage` (appended,
  optional). `DeliveryRegions` field retained on the wire but **ignored** by both services.

### `DeliveryCoverageJson.cs` (new — `Features/Marketplace/`)
- `Parse(string?) → DeliveryCoverageDto?` — null/blank/malformed → null; tolerates missing keys; result normalized.
- `Serialize(DeliveryCoverageDto) → string` — canonical **camelCase** JSON (matches `frontend/features/geo`
  `DeliveryCoverage` type + plan shape; worker writes `DeliveryByRegion` camelCase too). `System.Text.Json`,
  `JsonNamingPolicy.CamelCase`, `PropertyNameCaseInsensitive`, `WhenWritingNull`.
- `Validate(DeliveryCoverageDto) → List<string>` — normalizes first, then: every `served[].regionCode` /
  `notServed` entry must pass `UkraineRegions.IsValid` (reuses `UkraineRegions.Validate`); no code in both lists.
- `Normalize` (internal) — trims, drops blank codes, dedupes both lists case-insensitively (first `served`
  wins, keeps its terms). Idempotent. **Decision:** blank/whitespace entries are silently dropped by
  normalization rather than raising an error — "trim/normalize; dedupe" per the plan; only genuinely-unknown
  codes and served/notServed overlap produce validation errors.

### `MarketplaceService.cs`
- `ToFullProfileDto` — `DeliveryCoverage` populated **unconditionally** via `DeliveryCoverageJson.Parse`
  (outside the `showPremium` branch). `DeliveryRegions` read wrapped in `#pragma warning disable/restore CS0618`.
- `ToMetricsDto` — now `internal static` (shared with cabinet); maps the 4 new columns; `DeliveryByRegion`
  string → `List<RegionDeliveryStatDto>` via `System.Text.Json` (case-insensitive, null-safe, `JsonException`→null).
- `UpdateOwnProfileAsync` — `dto.DeliveryCoverage` non-null → `Validate` (→ `400` joined errors) then
  `Serialize` into `profile.DeliveryCoverage`. **Stopped writing `profile.DeliveryRegions`** (deleted the line).

### `SupplierCabinetService.cs`
- `UpdateProfileAsync` — same validate+serialize; stopped writing `DeliveryRegions`.
- `GetMetricsAsync` + `ToProfileDto` — now call `MarketplaceService.ToMetricsDto` (cabinet metrics gain the
  new fields too); `ToProfileDto` feeds `DeliveryCoverage` (parse) and keeps `DeliveryRegions` under the pragma.

### `MarketplaceOrderService.CreateOrderAsync`
- Injected `ILocationRepository` (registered in Infrastructure DI; 3 test ctor sites updated). After the
  `DestinationStoreId` null-check, loads the destination `Location` under the caller's (client) RLS context and
  sets `order.DestinationRegionCode = destination?.RegionCode` (null-safe — foreign/unknown id → null).

### `SupplierCabinetController.UpdateProfile` (minimal, in-scope)
- Was mapping **every** error to `404`. Now: `"not available"` → `404`, anything else (coverage validation) →
  `400` — mirrors the existing `SupplierProfileSettingsController` pattern. Added `[ProducesResponseType(400)]`.
  Judgment call (standard error handling), noted here per CLAUDE.md.

## Final DTO shapes (for T4/T8/T9/T10/T13)

```
DeliveryCoverageEntryDto(string RegionCode, string? Terms)
DeliveryCoverageDto(IReadOnlyList<DeliveryCoverageEntryDto> Served,
                    IReadOnlyList<string> NotServed, string? Note)
RegionDeliveryStatDto(string RegionCode, decimal AvgDeliveryDays, int SampleSize)
SupplierCoverageForBuyerDto(DeliveryCoverageDto Coverage, string? BuyerRegionCode,
                            string BuyerRegionStatus /* served|not_served|unknown */,
                            string? BuyerRegionTerms,
                            decimal? MeasuredAvgDeliveryDaysToBuyerRegion, int? MeasuredSampleSize)
SupplierMetricsDto(decimal? Rating, decimal? AvgDeliveryDays, decimal? OrderAccuracy,
                   decimal? QualityScore, decimal? CancellationRate, decimal? ResponseTimeHours,
                   DateTimeOffset UpdatedAt,
                   IReadOnlyList<RegionDeliveryStatDto>? DeliveryByRegion = null,
                   int? DeliverySampleSize = null, int? ResponseSampleSize = null,
                   DateTimeOffset? AggregatesComputedAt = null)
```
Stored `supplier_profiles.DeliveryCoverage` JSON (camelCase):
`{"served":[{"regionCode":"UA-32","terms":"..."}],"notServed":["UA-43"],"note":"..."}`

## Verification
- `dotnet build ShelfGuard.sln` — **0 errors**; the 4 `CS0618` `DeliveryRegions` warnings are **gone**
  (2 remaining reads are deliberate, `#pragma`-suppressed single sites). 1 pre-existing unrelated
  `CS8602` in `MarketplaceServiceTests.cs` (`AdminAddSupplierItemAsync_ImageUrls…` test, not touched).
- `dotnet test --filter "…Marketplace|…SupplierCabinet|…MarketplaceOrder|…DeliveryCoverage"` — **268/268 green**
  (was 249; +19 new: `DeliveryCoverageJsonTests` ×15, profile round-trip / metrics-aggregate / region-snapshot ×4).
- RLS-audit test — green (no schema change).
- Had to `Stop-Process` a stale local dev `ShelfGuard.Api.exe` (PID 37612, listening `localhost:5000`, started
  09:29) that was locking `bin/Debug` DLLs — standard per backend-structure.md. **User must restart their dev API.**

## Not done / follow-ups
- `backend/openapi.json` not regenerated (needs a live API+DB run; not in the T3 checklist) — fold into T15 docs.
- `GET /api/marketplace/suppliers/{id}/coverage` endpoint + repo query filter → T4.
- `SupplierAgreementService` / `ContractPdfGenerator` coverage section → T5. Worker aggregates → T6.
- `DeliveryRegions` → codes one-shot backfill → T14 (then the pragma-suppressed reads can go).
