# TASK-661 (T14) — one-shot backfill `supplier_profiles.DeliveryRegions` → `DeliveryCoverage`

**Status:** done (committed to main) · **Agent:** backend-developer
Plan: `eventual-whistling-rabbit.md`, «Бекфіл / міграція даних» + risk 5. Depends on T1
(`UkraineRegions.TryMatchFreeText`) + T2 (`DeliveryCoverage` column), both on main.

## Mechanism chosen: standalone console tool (`ShelfGuard.Tools.*`)

The project already has a Tools/console pattern (`ShelfGuard.Tools.PchilkaImport`) and the plan
prefers this over an admin endpoint or a non-pure DDL migration. New project
`backend/ShelfGuard.Tools.DeliveryCoverageBackfill` — same layout/`FrameworkReference` rationale
as PchilkaImport (references Application/Infrastructure/Domain so it reuses `AppDbContext`,
`DeliveryRegionsBackfill`, `DeliveryCoverageJson`, the real `TenantConnectionInterceptor`).
Added to `ShelfGuard.sln`.

### How to run (prod)

```bash
cd backend
# dry run first — computes + prints every change, then rolls back:
ConnectionStrings__DefaultConnection="Host=…;Database=…;Username=shelfguard_app;Password=…" \
  dotnet run --project ShelfGuard.Tools.DeliveryCoverageBackfill
# then persist:
ConnectionStrings__DefaultConnection="…" \
  dotnet run --project ShelfGuard.Tools.DeliveryCoverageBackfill -- --apply
```

- Connection must be the **non-superuser app role** (`shelfguard_app`). The tool asserts
  `SET LOCAL app.role = 'provider'` itself inside one explicit transaction (identical mechanism
  to `ProviderRlsOverride`; `supplier_profiles` carries `provider_bypass` and **no** RESTRICTIVE
  `store_scope`, so `provider` alone is sufficient for the cross-tenant read+write). Issued
  directly, not via the DI'd `IProviderRlsOverride` — that service's contract +
  `ProviderRlsOverrideContainmentTests` restrict it to `MarketplaceRepository`.
- **Dry run by default**; `--apply` commits. `--help` prints usage.
- Idempotent: only rows with `DeliveryCoverage IS NULL` **and** non-null `DeliveryRegions` are
  touched, so a re-run after a partial/aborted run is safe. The whole run is one transaction
  (all-or-nothing).
- After it runs successfully in prod, the two `#pragma warning disable CS0618` reads of
  `DeliveryRegions` in `MarketplaceService`/`SupplierCabinetService` can be dropped — **separate
  follow-up, not this task** (left untouched).

## Logic (pure helper — `DeliveryRegionsBackfill.Build`)

New `backend/ShelfGuard.Application/Features/Marketplace/DeliveryRegionsBackfill.cs` — I/O-free,
unit-tested. For each parsed free-text string:
- `UkraineRegions.TryMatchFreeText(s)` match → `served` entry `{ regionCode, terms: null }`
  (deduped by code, first occurrence wins).
- no match → collected verbatim into `note` = `"Також: " + join(", ", unmatched)` (deduped
  case-insensitively) so nothing from the legacy column is lost.
- `notServed` always empty (legacy column only expressed positive coverage).
- **served empty + note present → still written** (note-only coverage is valid and visible; the
  less-surprising choice per the task). served empty + no note (e.g. `[]`) → `Coverage == null`,
  row left untouched.
- Tool: `profile.DeliveryCoverage = DeliveryCoverageJson.Serialize(dto)`; `DeliveryRegions`
  **not** cleared (audit trail; a later migration drops the column). Per-row + summary logging.

**Tradeoff noted (plan risk 5):** flipping a row to note-only coverage removes it from the
`DeliveryCoverage IS NULL` legacy `Region ILIKE` fallback in `MarketplaceRepository`. Only bites
a row whose `DeliveryRegions` is entirely unmappable *and* whose `Region` string would have
matched the code/name ILIKE — rare, and such a string wouldn't reliably match that fallback
anyway. Mappable rows get real `served` codes and match the jsonb `@>` filter properly.

## Dev-DB run (2026-08-31, `crmproductsystems-postgres-1`, applied)

```
Rows with DeliveryCoverage IS NULL and DeliveryRegions IS NOT NULL: 2
  [skip]   b6598054-…  DeliveryRegions=[]        -> nothing to map
  [update] ef3a82bb-…  matched=[]  unmatched=[Odesa]
           {"served":[],"notServed":[],"note":"Також: Odesa"}

Rows scanned: 2 · updated: 1 (committed) · skipped (nothing to map): 1
Total unmatched values: 1 (1 distinct): Odesa
```

A 3rd row with non-null `DeliveryRegions` (`f4cdddf4-…`, `["Kyiv","Lviv"]`) already had a
`DeliveryCoverage` written by a concurrent session → correctly skipped by the idempotency guard.
Re-run afterwards: 0 updated (idempotent confirmed). Verified in psql: `ef3a82bb` now
`{"note":"Також: Odesa","served":[],"notServed":[]}`, `DeliveryRegions` still `["Odesa"]`.

Dev data is Latin-script QA junk (`Kyiv`/`Lviv`/`Odesa`) so match rate is 0 — expected, and the
tool is effectively a near-no-op here. Real (Cyrillic) prod region names will map through
`TryMatchFreeText`.

## Tests

`backend/ShelfGuard.Tests/Marketplace/DeliveryRegionsBackfillTests.cs` (9 tests, all green):
the required case `["Київська область","по домовленості","Житомир"]` → served `[UA-32,
UA-18-ZHYTOMYR]` + note `"Також: по домовленості"`; all-match no-note; none-match note-only;
served dedupe by code; unmatched dedupe case-insensitive; empty array / blank-only / null →
`Coverage == null`; output round-trips through `DeliveryCoverageJson.Serialize`/`Parse` and
passes `DeliveryCoverageJson.Validate`.

## Build / tests

`dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing warning, `MarketplaceServiceTests.cs`
line 875, untouched). `dotnet test ShelfGuard.sln` — 2134/2134.

## Files

- `backend/ShelfGuard.Application/Features/Marketplace/DeliveryRegionsBackfill.cs` (new — pure helper)
- `backend/ShelfGuard.Tools.DeliveryCoverageBackfill/` (new project: `.csproj`, `appsettings.json`,
  `Program.cs`, `BackfillRunner.cs`)
- `backend/ShelfGuard.Tests/Marketplace/DeliveryRegionsBackfillTests.cs` (new)
- `backend/ShelfGuard.sln` (project entry)
