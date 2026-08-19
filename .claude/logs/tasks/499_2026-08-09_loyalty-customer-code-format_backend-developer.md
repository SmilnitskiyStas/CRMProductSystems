# TASK-499 — Backend: per-tenant customer code display format (QR vs. barcode)

**Status:** done · **Agent:** backend-developer · **Date:** 2026-08-09

## Problem

The consumer's universal checkout code (`SGCUS1.{consumerAccountId}.{totp}`, TASK-498/cross-tenant
redesign) had no notion of how it should be displayed. Product decision: a store network
(`Tenant`), never an individual store, chooses QR vs. Code 128 barcode for its customers — a
single per-tenant setting, default `barcode`. Before a consumer has joined any network, there's no
tenant context to look a preference up from, so they get the system default.

## What changed

### `backend/ShelfGuard.Domain/Entities/LoyaltyProgramSettings.cs`
Added `public string CustomerCodeFormat { get; set; } = "barcode";` — string-typed (round-trips
through JSON/SQL as a plain value), valid values exactly `"qr"`/`"barcode"`.

### Migration `20260809180100_AddLoyaltyCustomerCodeFormat`
`CustomerCodeFormat varchar(20) NOT NULL DEFAULT 'barcode'` on `loyalty_program_settings`, scaffolded
via `dotnet ef migrations add` (real tool, not hand-written) against `AppDbContext`'s fluent config
(`HasColumnType("varchar(20)").HasDefaultValue("barcode")`). `AppDbContextModelSnapshot.cs` updated
by the tool.

**Timestamp note:** the tool generated `20260809164721_...` from the real system UTC clock
(16:47), which sorts *before* the immediately-preceding, hand-authored
`20260809180000_AddConsumerLoyaltyCodeSecret` migration (timestamped 18:00 — ahead of the actual
clock at the time it was written). Renamed the new migration's file pair and its
`[Migration("...")]` attribute to `20260809180100` so it sorts after, confirmed via
`dotnet ef migrations list`. Applied cleanly to local dev Postgres
(`dotnet ef database update ... Host=localhost;Port=5435;Database=crm;Username=crm`) — only this
one migration ran (the DB was already current otherwise).

### DTOs (`backend/ShelfGuard.Application/Features/Loyalty/Dtos/LoyaltyDtos.cs`)
- `LoyaltyCodeDto` — new `DisplayFormat` (string), inserted as 2nd positional param (`Code,
  DisplayFormat, Balance, ExpiresInSeconds`) to match the wire order in the brief. `Balance`
  semantics unchanged (still hardcoded `0m`, a separate known simplification).
- `LoyaltyProgramSettingsDto` — new `CustomerCodeFormat` (string), added after `CodeTtlSeconds`,
  before `UpdatedAt`.
- `UpsertLoyaltyProgramSettingsRequest` — new `CustomerCodeFormat` (string), required positional
  param (this DTO has no partial-update semantics — every field is always fully overwritten, so
  null/empty is rejected the same as any other unrecognized value, not treated as "leave
  unchanged").

### `LoyaltyService.cs`
- `UpsertSettingsAsync`: `if (request.CustomerCodeFormat is not ("qr" or "barcode")) return (null,
  "CustomerCodeFormat must be 'qr' or 'barcode'.");` — catches null/empty/anything else in one
  check (400 via `LoyaltySettingsController`'s existing generic error mapping).
- `ApplyRequest` / `ToSettingsDto`: thread the field through, same pattern as the other settings.
- `GetConsumerCodeAsync(Guid consumerAccountId, Guid? tenantId = null, CancellationToken ct =
  default)` — signature gained the optional `tenantId`. New resolution logic (consumer-lookup /
  lazy TOTP secret / code generation left exactly as before):
  - `tenantId` given → `GetMembershipByTenantConsumerAsync`; no membership → 403 `"You are not a
    member of this network."`; else resolve that tenant's format.
  - `tenantId` omitted → `GetMembershipsForConsumerAsync`; 0 → `"barcode"`; 1 → that tenant's
    format; 2+ → 409, `Error = "network_selection_required"` (controller reflects `Error` verbatim
    into the JSON body, giving the machine-readable code the brief asked for).
- New private `ResolveCustomerCodeFormatAsync(Guid tenantId, ct)`: reads
  `LoyaltyProgramSettings.CustomerCodeFormat` via `_loyalty.GetSettingsAsync`, wrapped in
  `ITenantSessionOverride.ExecuteAsync` — **required** because `loyalty_program_settings` carries
  only the canonical `tenant_isolation`/`provider_bypass`/`worker_bypass` RLS triad, no
  `consumer_self_access` policy (confirmed in `20260726132332_AddLoyaltyProgram.cs`), so a
  consumer session's ambient null `app.tenant_id` would otherwise see zero rows. Both call sites
  pass a `tenantId` already proven (via a checked `LoyaltyMembership` row) to belong to this
  consumer, satisfying `ITenantSessionOverride`'s security contract. No saved settings row →
  `"barcode"`.
- `[Obsolete] GetCurrentCodeAsync` (unused elsewhere, kept for compat) now forwards its `tenantId`
  through instead of dropping it.

### `ILoyaltyService.cs`
Interface signature + XML doc updated for the new optional `tenantId` param and its 403/409 cases.

### `ConsumerLoyaltyController.cs`
`GetCode` action: added `[FromQuery] Guid? tenantId`, passed through to the service call.
Added `[ProducesResponseType]` for 200/403/404/409 (this controller had none before).

### `LoyaltySettingsController.cs`
No behavior change needed — it already passes the whole DTO/request through; doc comment updated
to mention the new default (`barcode`) alongside the existing 3%/50%/0/30s figures.

## Wire contract (for the parallel web-settings and mobile workstreams)

`GET /api/consumer/loyalty/code?tenantId={optional guid}` → 200:
```json
{ "code": "SGCUS1....", "displayFormat": "qr", "balance": 0, "expiresInSeconds": 30 }
```
403 (explicit tenantId, consumer not a member there):
```json
{ "error": "You are not a member of this network." }
```
409 (no tenantId, 2+ memberships):
```json
{ "error": "network_selection_required" }
```
404 (unknown/inactive consumer account, unchanged from before):
```json
{ "error": "Consumer account not found." }
```

`GET /api/settings/loyalty` → 200 (unchanged shape, new field added):
```json
{
  "isEnabled": true,
  "accrualRatePercent": 3.0,
  "redemptionCapPercent": 50.0,
  "minRedemptionBalance": 0,
  "codeTtlSeconds": 30,
  "customerCodeFormat": "barcode",
  "updatedAt": null
}
```
`PUT /api/settings/loyalty` request body — same shape as the GET response minus `updatedAt`,
`customerCodeFormat` required, exactly `"qr"` or `"barcode"` (400 otherwise, including
null/empty/wrong case):
```json
{
  "isEnabled": true,
  "accrualRatePercent": 3.0,
  "redemptionCapPercent": 50.0,
  "minRedemptionBalance": 0,
  "codeTtlSeconds": 30,
  "customerCodeFormat": "qr"
}
```

## Explicitly not touched (per brief)
`JoinAsync`, `ResolveOrCreateMembershipByPhoneAsync` (TASK-498), and `ResolveCodeAsync`'s legacy
`SGLOY1.` branch — confirmed unmodified and its 6 existing regression tests still pass. Nothing
under `mobile/` or `frontend/` touched.

## Tests

`backend/ShelfGuard.Tests/Auth/LoyaltyServiceTests.cs` — 12 new cases:
- Default `CustomerCodeFormat` is `"barcode"` on no saved settings row (extended existing test).
- Upsert round-trips `"qr"` and `"barcode"` (2 cases).
- Upsert rejects unrecognized format — null, empty, wrong case, garbage (4 cases).
- `GetConsumerCodeAsync`, no `tenantId`: 0 memberships → `"barcode"`; 1 membership → saved format
  (`"qr"` case and no-saved-settings-row → `"barcode"` case); 2+ → 409
  `network_selection_required`.
- `GetConsumerCodeAsync`, explicit `tenantId`: not a member → 403 (and never calls the
  memberships-list ambiguity path); is a member → correct format even with other memberships
  present (proves the explicit-tenantId path bypasses the 2+ ambiguity check).
- Also added a generic `ITenantSessionOverride.ExecuteAsync<LoyaltyProgramSettings?>` pass-through
  stub in the test constructor (same pattern as the existing `LoyaltyMembership` one — NSubstitute
  requires a separate setup per closed generic).

## Verification

- `dotnet build` (full solution): 0 errors, 0 new warnings (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test` (full suite, no filter): **1375/1375 pass** (1363 baseline + 12 new, 0
  regressions).
- Migration applied to local dev Postgres; all 12 Loyalty integration tests
  (`LoyaltyRepositoryIntegrationTests`, `LoyaltyJoinRlsIntegrationTests`,
  `LoyaltyConcurrencySalesIntegrationTests`, `LoyaltyRlsIntegrationTests`) pass live against it.
- `docker build -f backend/Dockerfile backend`: succeeds, matches the plain `dotnet build` output.
- Nothing staged or committed.
