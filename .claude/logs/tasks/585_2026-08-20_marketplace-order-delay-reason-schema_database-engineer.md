# TASK-585 — MarketplaceOrder.DelayReason (schema layer)

**Status:** done
**Agent:** database-engineer
**Scope:** schema only (part 1 of 3 — backend-developer/frontend-developer follow up separately)

## What changed

- `backend/ShelfGuard.Domain/Entities/MarketplaceOrder.cs` — added `public string? DelayReason { get; set; }` right after `DeliveredAt` (TASK-584 field). Mirrors `CancelReason`: nullable free-text, same style/doc-comment convention.
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` (~line 1928) — added `e.Property(x => x.DelayReason).HasMaxLength(2000).IsRequired(false);` next to the existing `CancelReason` config, so the column type matches exactly (`varchar(2000)`, not unbounded `text`).
- Migration: `20260820193144_AddMarketplaceOrderDelayReason` (`ShelfGuard.Infrastructure/Migrations/`). Single `AddColumn<string>` for `DelayReason` on `marketplace_orders`, type `character varying(2000)`, nullable. No unrelated model drift picked up — verified by reading the generated file.

## Verification

- `dotnet build` — clean (1 pre-existing warning unrelated to this change, `MarketplaceServiceTests.cs:534`).
- `dotnet ef database update` applied against local dev DB (docker `crmproductsystems-postgres-1`, port 5435) — confirmed via `\d marketplace_orders`: `DelayReason | character varying(2000)` present, nullable, no default.
- RLS confirmed unchanged and sufficient: `tenant_isolation` (`SupplierTenantId OR ClientTenantId = app.tenant_id`), `provider_bypass`, `worker_bypass` all still present on the table — no new policy added, none needed (row-level policies cover new columns automatically).
- No index added (free-text field, never filtered/sorted).
- `dotnet test` — full suite: **1755 passed, 0 failed**.

## Notes for next agent

See handoff: `.claude/logs/handoffs/585-to-backend_database-engineer.md`.
