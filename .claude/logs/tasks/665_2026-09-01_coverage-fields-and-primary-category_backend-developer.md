# TASK-665 — structured per-region delivery fields + single primary supplier category

**Agent:** backend-developer · **Date:** 2026-09-01 · **Status:** done
**Branch:** `main` (main working tree) · Modifies the shipped-but-not-deployed delivery-coverage
feature (TASK-648..664, plan `eventual-whistling-rabbit.md`, ADR-036). Backend only —
frontend/mobile follow in later tasks. **No DB migration** (both changes are app-level JSON shape
over existing `jsonb` columns; prod `supplier_profiles.DeliveryCoverage` is empty).

## CHANGE A — structured per-region delivery fields (replaced the single `terms` string)

### `Features/Marketplace/Dtos/MarketplaceDtos.cs`
- `DeliveryCoverageEntryDto(string RegionCode, int? DeliveryDaysMin, int? DeliveryDaysMax,
  decimal? MinOrderAmount, string? Note)` — replaces `(RegionCode, string? Terms)`.
- `SupplierCoverageForBuyerDto` — `string? BuyerRegionTerms` → `DeliveryCoverageEntryDto? BuyerRegionEntry`
  (the served entry matching the buyer's region, or null). `BuyerRegionStatus` logic unchanged.
- `DeliveryCoverageDto` keeps its global `Note`. Stored JSON stays camelCase:
  `{"served":[{"regionCode":"UA-32","deliveryDaysMin":1,"deliveryDaysMax":3,"minOrderAmount":5000,"note":"…"}],"notServed":["UA-43"],"note":"…"}`

### `Features/Marketplace/DeliveryCoverageJson.cs`
- `RawEntry` gained the 4 structured fields; keeps `Terms` (nullable) for **read-only** back-compat.
- `Parse` — when an entry has non-empty `Terms` and no `Note`, `Terms` → `Note` (dev-DB rows in the old
  shape self-heal). `Terms` is never written back.
- `Serialize` — new shape only, `Terms` omitted; null structured fields omitted (`WhenWritingNull`).
- `Normalize` — trims `Note`; drops blank `RegionCode`; **swaps** a reversed day pair (min > max);
  dedupes served by code (first wins, keeps its fields). Does **not** clamp negatives — Validate rejects them.
- `Validate` — existing region-code + served/notServed-overlap checks kept; added: `DeliveryDaysMin`/`Max`
  must be `0..365` when present; `MinOrderAmount` must be `>= 0` when present. Ukrainian messages.

### `Features/Marketplace/MarketplaceService.cs`
- `GetSupplierCoverageForBuyerAsync` — populates `BuyerRegionEntry` from the parsed served list.
- `UpdateOwnProfileAsync` — no longer writes `profile.Categories` (see Change B); still writes coverage.

### `Features/Marketplace/SupplierAgreementService.cs`
- `ContractDeliveryRegion` / `IContractPdfGenerator` / `ContractPdfGenerator` / its tests — **untouched**.
- New private `FormatDeliveryTerms(DeliveryCoverageEntryDto)` — flattens the structured fields into the
  single free-text line the PDF still renders: e.g. `«1–3 дні, від 5000 грн, спецумови»` / `«до 2 днів»` /
  `«від 5000 грн»`; per-region note appended as the last comma-segment; returns `null` when empty.
  `BuildDeliveryCoverageAsync` now calls it instead of reading the removed `.Terms`.

### `Features/Marketplace/DeliveryRegionsBackfill.cs`
- `Build` — served entries now constructed with 4 null structured fields. Unmatched free text still → global `note`.

### `MarketplaceRepository.ApplyRegionCoverageFilter` — **no change** (verified)
The jsonb `@>` predicate `DeliveryCoverage @> {"served":[{"regionCode":"X"}]}` is a subset match —
extra keys on served entries do not affect it. Added a structured-shape seed row to
`MarketplaceRepositoryCoverageFilterIntegrationTests` asserting it still matches.

## CHANGE B — one primary supplier category, set at tenant creation, read-only afterward

`SupplierProfile.Categories` (jsonb string array) unchanged; a supplier profile now holds 0 or 1 entry.

### `Features/Marketplace/SupplierOnboarding.cs`
- `CreateOwnerManaged(Guid tenantId, string tenantName, string? primaryCategory = null)` — when
  `primaryCategory` is a valid `SupplierItemCategories` key → `profile.Categories = ["<key>"]`;
  invalid/null → `Categories` stays null (unchanged behaviour).

### `ProviderService.cs` + `TenantAdminService.cs` — tenant-create paths
- **`Provider/Dtos/ProviderDtos.cs` `CreateTenantRequest`** += `string? SupplierCategory = null`.
- **`Admin/Dtos/AdminDtos.cs` `CreateTenantRequest`** += `string? SupplierCategory = null`.
- Both `CreateTenantAsync` validate `SupplierCategory` against `SupplierItemCategories.Find` **only when
  `businessType == "supplier"`** (unknown key → `"Unknown supplier category: '<x>'."`, nothing persisted);
  non-supplier tenants ignore the field. Value threaded into `CreateOwnerManaged`.
- Controllers (`ProviderController`, `AdminController`) already pass the request record straight through — no change.

### `SupplierCabinetService.cs` (lazy backfill ~:395) — unchanged, passes no category (null).

### profile-update endpoints stop writing `Categories`
- `MarketplaceService.UpdateOwnProfileAsync` + `SupplierCabinetService.UpdateProfileAsync` — removed the
  `profile.Categories = …` write. `Categories` **kept on the wire** in `SupplierProfileUpdateDto` /
  `CabinetProfileUpdateDto` (legacy clients/tests still send it) but now **ignored** — same treatment
  `DeliveryRegions` already gets. Read side (`ToFullProfileDto` / cabinet mapper) still returns `Categories`.

### Provider "fix an existing supplier's category" path — **added** (no such path existed)
- `IProviderService.SetSupplierCategoryAsync(Guid tenantId, string? category, ct)` + impl:
  loads tenant → must be `business_type == supplier`; validates the key (blank → clears `Categories`);
  loads the owner-managed profile via new `ITenantRepository.GetOwnerManagedSupplierProfileAsync`;
  sets `Categories` + `UpdatedAt`; saves.
- `ProviderController` — `PUT /api/provider/tenants/{id}/supplier-category`, body
  `{ "category": "food" | null }`, `ProviderOnly` policy (class-level). 204 / 400 / 404.

### One-shot dev-DB cleanup — `ShelfGuard.Tools.DeliveryCoverageBackfill`
- `BackfillRunner` gained a step after the DeliveryRegions loop (same transaction / dry-run semantics):
  every `supplier_profiles.Categories` row with `> 1` element is reduced to its first. Idempotent.
- **Ran against dev DB (`--apply`, 2026-09-01):** 1 profile
  `b6598054-…` `[auto_parts, medical, food]` → `[auto_parts]`. Re-run → 0 (idempotent confirmed).

## Verification
- `dotnet build ShelfGuard.sln` — 0 errors, 1 warning (pre-existing CS8602 in `MarketplaceServiceTests.cs:895`, unrelated).
- `dotnet test ShelfGuard.sln` — **2158/2158 passed** (baseline 2134 + 24 net-new tests, 0 failures).
- New/updated tests: `DeliveryCoverageJsonTests` (new-shape round-trip, `terms`→`note` back-compat +
  never-written-back, day-range swap, out-of-range/negative rejection), `MarketplaceServiceTests`
  (`BuyerRegionEntry`, legacy-terms heal), `SupplierAgreementServiceTests` (`FormatDeliveryTerms` structured +
  legacy), `SupplierCabinetServiceTests` (`Categories` no longer written), `ProviderServiceTests` +
  `TenantAdminServiceTests` (create-with-category valid/invalid/non-supplier; `SetSupplierCategoryAsync`),
  `MarketplaceRepositoryCoverageFilterIntegrationTests` (structured entry still matches the region filter).

## Not done (out of scope / pending)
- `backend/openapi.json` not regenerated (KI-040, already a pending prod step for this feature).
- Frontend / mobile — later tasks.
