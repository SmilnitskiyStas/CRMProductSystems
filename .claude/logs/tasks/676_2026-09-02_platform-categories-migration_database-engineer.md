# TASK-676 — B1: global PlatformCategory table + migration

**Status:** done · **Agent:** database-engineer · Plan `.claude/plans/1-giggly-catmull.md` (Частина B / B1)

## What changed

Per-tenant `Category` entity replaced with a single global, provider-curated
`PlatformCategory` (`platform_categories` table) — **no `TenantId`, no RLS**.

- **Domain:** deleted `Category.cs`; `PlatformCategory.cs` kept as delivered (reviewed, no
  change). Nav `Item.Category` / `ProductSegment.Category` / `WeatherCoefficient.Category`
  already retyped to `PlatformCategory?` in the working tree.
- **AppDbContext:** `DbSet<Category> Categories` → `DbSet<PlatformCategory> PlatformCategories`;
  `builder.Entity<Category>` block → `builder.Entity<PlatformCategory>`
  (`platform_categories`, jsonb `BusinessTypes` default `'[]'`, `SortOrder`/`IsActive`/
  `CreatedAt` defaults, self-FK `ParentId` RESTRICT, indexes
  `idx_platform_categories_parent_active` / `idx_platform_categories_active_sort`, **no
  tenant FK/index, no RLS**). `WeatherCoefficient.CategoryId` FK `Cascade` → **`SetNull`**.
- **Repos/services:** `ICategoryRepository` / `CategoryRepository` / `CategoryService` /
  `AnalyticsRepository` (`_db.Categories` → `_db.PlatformCategories`).
- **`AudienceBuilderRepository.SearchCategoriesAsync`** (not in brief, but its raw SQL read
  `FROM categories WHERE "TenantId" = …`): now `FROM platform_categories`, tenant filter
  dropped, `ItemCount` subquery keeps an explicit `i."TenantId"` filter.
- **Seeder / import tool:** `DbSeeder` 6 demo categories → `PlatformCategory` with
  `BusinessTypes = ["retail"]` + `SortOrder`; `PchilkaImport.ImportRunner` matches/creates
  global `PlatformCategory` by trimmed case-insensitive name (no `TenantId` filter).
- **Tests:** 8 integration test files — `new Category { TenantId … }` → `new PlatformCategory`,
  `.Categories` → `.PlatformCategories`, and raw-SQL cleanup `DELETE FROM categories WHERE
  "TenantId" …` → `DELETE FROM platform_categories WHERE "Name" LIKE …` / `"Id" = …`.
  `AudienceBuilderRepositoryIntegrationTests` typeahead test adapted for the now-global list
  (unique run-token category name; empty-search assertion uses a generous limit).

## Migration `20260902114742_AddPlatformCategories`

Hand-edited after scaffold. `Up()` in one transaction:
1. `CREATE TABLE platform_categories` (+ self-FK + 2 indexes, **no ENABLE RLS**).
2. `ALTER TABLE categories/items/product_segments/weather_coefficients NO FORCE ROW LEVEL
   SECURITY` — migrations run as the table-owning NOBYPASSRLS app role with no
   `app.tenant_id`; without this the data steps see zero rows (learned the hard way — first
   apply silently no-op'd the seed). Restored in step 8.
3. Seed = `INSERT … SELECT gen_random_uuid(), c."Name", jsonb_agg(DISTINCT t."BusinessType") …
   FROM categories c JOIN tenants t … GROUP BY lower(btrim(c."Name")), c."Name"` (flat,
   case/whitespace-collapsed).
4. Drop old FKs → `categories` (**before** the repoint — the repoint writes ids the old FK
   rejects; the brief's stated order was repoint-then-drop and fails with FK 23503).
5. Repoint `items` / `product_segments` / `weather_coefficients` `.CategoryId` by trimmed
   case-insensitive name.
6. Add new FKs → `platform_categories`, all `ON DELETE SET NULL`.
7. `DROP TABLE categories` (RLS policies + `idx_categories_tenant_parent_active` drop with it).
8. Restore `FORCE ROW LEVEL SECURITY` on the 3 surviving tables.

`Down()`: recreates `categories` + RLS triad (tenant_isolation NULLIF / provider_bypass
`IN ('provider','provider_admin')` / worker_bypass) + FK swap-back + `DROP platform_categories`.
Row data **not** restored — documented irreversible, matching `MigrateOrphanSuppliersToTenants`.

SQL script: `scratchpad/add-platform-categories.sql` (reviewed end to end — ordering correct,
no `items."CategoryId"` nulled by the FK swap).

## Verification (dev DB `crm` @ localhost:5435)

- `dotnet build ShelfGuard.sln` — 0 errors, 0 warnings.
- `dotnet ef database update` applied clean (after fixing ordering; one earlier bad apply +
  full recovery from `categories` data backup, dev DB now correct).
- Non-null `items."CategoryId"`: **199 before → 199 after** (identical); 0 orphaned.
- `platform_categories`: **86 rows** = distinct-name count of old `categories` (86).
  Item→category name mapping verified row-for-row against pre-migration backup: 0 mismatches,
  0 lost.
- `platform_categories` has no RLS; `items`/`product_segments`/`weather_coefficients` FORCE
  RLS restored.
- `dotnet test` — filtered set (Catalog|Rls|ItemRepository) 133/133; wider affected set
  801/801; **full suite 2174/2174 green**. RLS audit
  `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` passes
  (no-RLS `platform_categories` not flagged).

## Notes for the main session / B2 / prod deploy

- Backend on :5000 was stopped to release DLL locks for the build/migration — **needs
  restarting**.
- **Prod deploy:** the seed's `JOIN tenants` drops any `categories` row whose tenant is
  missing; on dev there were 0 such. Verify on prod before deploy or those items' categories
  would be left dangling and the new FK add would abort (safe — transaction rollback).
- `AppDbContextModelSnapshot` regenerated. openapi.json regen still pending (no contract
  change here, `CategoryDto` unchanged).
