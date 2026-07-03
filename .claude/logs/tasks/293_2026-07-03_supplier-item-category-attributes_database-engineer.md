# TASK-293 — DB: SupplierItem.Category + Attributes (JSONB)

**Status:** done · **Agent:** database-engineer

## Changes
- `SupplierItem` entity: added `string? Category`, `Dictionary<string, object?>? Attributes` (both nullable, no defaults).
- EF config (`AppDbContext.cs`): `Category` → `text`; `Attributes` → `jsonb` via a `HasConversion` (System.Text.Json serialize/deserialize) + `ValueComparer`, not Npgsql dynamic-json — needed so the model also validates under EF Core InMemory (used by `ShelfGuard.Tests`), which cannot map `Dictionary<string,object?>` directly even with `EnableDynamicJson()`.
- Migration: `20260703162241_AddSupplierItemCategoryAttributes` — adds `Attributes` (jsonb, nullable) and `Category` (text, nullable) to `supplier_items`. No RLS changes (existing `tenant_isolation` + `provider_bypass` on `supplier_items` untouched; NULLIF guard already present from prior sprint).

## Verify
- `dotnet build`: green, 0 warnings/errors.
- `dotnet test`: 515/515 green (first attempt with Npgsql-only `jsonb` mapping broke 5 Marketplace tests under InMemory provider — fixed via value converter above, re-verified clean).

## Notes for TASK-294 (backend-developer)
- Read attributes back as `Dictionary<string, object?>` normally through EF; no special handling needed at the service layer.
