# TASK-398: Per-item sidebar tab catalog (item-level granularity for AllowedTabs)

**Agent:** backend-developer
**Date:** 2026-07-20
**Status:** done

## Контекст

Product owner feedback on the already-shipped Feature 1 (ADR-021, TASK-391): whole-group grants
(e.g. "operations" = Inventory+Stock+Receipts+Transfers+WriteOffs+Locations+IoT, all-or-nothing)
are too coarse. Need to grant a single page (e.g. only Receipts) without unlocking the rest of
its group.

## Зроблено

1. **`TenantRoleTabs.cs`** (`ShelfGuard.Domain/Constants`) — verified the real href list directly
   against `frontend/components/layout/Sidebar.tsx`'s `buildNavGroups` per the task brief's
   instruction (it matched the brief's own list exactly, 27 hrefs across 9 groups — confirmed
   independently rather than copied blind) and added 27 new item-level key constants (`Item*`,
   value = literal `NavItem.href`), one per page across the 9 real groups. Kept all 10 original group-level
   keys unchanged (backward compat). New `GroupKeys` (the 9 group container keys) and `ItemKeys`
   (the 27 page keys) sets; `All = { Dashboard } ∪ GroupKeys ∪ ItemKeys` — 37 total. Added
   `TenantRoleTabGroupDefinition(GroupKey?, GroupLabelUa, Items)` and rebuilt `Catalog` as a
   hierarchy: standalone Dashboard section (`GroupKey: null`) + 9 group sections, each with its
   nested item definitions. Labels copied verbatim from `frontend/messages/uk.json`
   (`Dashboard.sidebar.groups.*`).

2. **`GET /api/tenant-roles/tabs`** — `ITenantRoleService.GetTabCatalog()` /
   `TenantRoleService.GetTabCatalog()` / `TenantRolesController.GetTabs()` return type changed
   from flat `TenantRoleTabDto[]` to `TenantRoleTabGroupDto[]` (new DTO:
   `{ groupKey: string|null, groupLabelUa: string, items: TenantRoleTabDto[] }` — `TenantRoleTabDto
   { key, labelUa }` reused unchanged as the leaf shape). `groupKey` doubles as an independently-
   grantable coarse key; `items[].key` are the fine-grained hrefs.

3. **`TenantRoleService.Validate`** — no logic change needed: it already checks
   `TenantRoleTabs.All.Contains(t)` generically, so expanding `All` to include the 27 item keys
   automatically makes both key flavours valid on the same `allowedTabs` list with zero branching.
   Added a comment explaining this instead of speculative new code.

4. **One judgment call, flagged rather than silently resolved:** `"/settings/legal-entities"` is a
   real Workforce `NavItem` (verified in Sidebar.tsx) so it's included in the catalog for
   completeness — but Sidebar.tsx's TASK-397 carve-out already excludes that one href from
   `tabsSet` entirely (`canManageLegalEntities`-only, security-sensitive, deliberate). Documented
   in `TenantRoleTabs.cs`'s class doc comment and in the ADR-021 addendum below so the future
   frontend item-level-enforcement task doesn't accidentally wire it up and create a bypass.

5. **Docs** (in scope — this task directly changes the shape they document, not pre-existing
   unrelated staleness): `.claude/docs/api-contracts.md` (`GET .../tabs` response shape, request
   validation note, new `TenantRoleTabGroupDto` example) and a dated addendum under ADR-021 in
   `.claude/docs/decisions.md`.

## Не в скоупі (за завданням)

- `Sidebar.tsx` / any frontend enforcement — still only reads `tabsSet.has(group.key)`. Granting
  an item-level key today has **zero client-side effect** until a follow-up frontend task wires
  it in. Backend catalog/validation is ready for that follow-up.
- Migrations — `AllowedTabs` is already `text[]` with no DB-level value constraint; only the
  in-app whitelist grew.

## Тести

`TenantRoleServiceTests.cs` — replaced the old flat-catalog test with 3 catalog-shape tests
(flattened group+item keys match `All` with labels; Dashboard is the sole `groupKey: null`
section with exactly 1 item; every real group's `groupKey` ∈ `GroupKeys` with ≥1 item) + 4
validation tests (item-level key alone accepted; group-level + item-level + Dashboard mixed in one
template accepted; unknown item-level key rejected; excluded `/provider` href rejected). Existing
`AuthServiceTabsTests.cs` needed no changes (already treats `AllowedTabs` as opaque strings,
pass-through only).

## Верифікація

- `dotnet build --no-incremental` (full solution) — 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, same one noted in TASK-395's log).
- `dotnet test` — **907/907 passed** (net +6 new tests in this task: 7 added, 1 rewritten in place).
- Git: local commit only, no push (per task brief).
