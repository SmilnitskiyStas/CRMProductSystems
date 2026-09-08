# TASK-706 — Календар подій: власна секція меню замість POS-gated групи

**Дата:** 2026-09-08
**Тип:** bugfix (навігація / module-gating), frontend + backend-константа
**Status:** review · main session · не запушено

## Проблема

Користувач: «зник бізнес-календар (свята + позначки на дні) з веб-застосунку».

Розслідування (2 Explore-агенти + перевірка): фічу ніхто не видаляв. Сторінка
`frontend/app/(dashboard)/events/page.tsx`, `frontend/features/events/**`, бекенд
`EventsController` / `EventService` / `DemandEvent*` — усе ціле, останні зміни TASK-588..594.

Причина: пункт `/events` («Події») з TASK-210 (`b987171c`) лежав у групі Sidebar «Продажі»
з `moduleKey: "pos"`. Тенанти без модуля `pos` (бізнес-типи `auto_service` / `warehouse` /
`production` / `distribution` за `Tenant.DefaultModulesForBusinessType`, або провайдер зняв
`pos`) втрачали всю групу разом із календарем. `00e38bc9` прибрав bypass для enterprise_admin —
після цього навіть власник тенанта без `pos` календар не бачив. Пов'язано з KI-019.

## Рішення

Календар — універсальний інструмент планування, не привʼязаний до POS. Винесено в окрему
Sidebar-групу «Календар» **без `moduleKey`**. Видимість — роль `AT_LEAST_STORE_MANAGER` за
замовчуванням + per-role керування через наявний механізм TenantRole «Видимі розділи».
Бекенд-авторизація не змінена, міграцій немає.

## Зміни

- `frontend/components/layout/Sidebar.tsx` — прибрано `/events` з групи `sales`; додано нову
  групу `key: "calendar"` (без `moduleKey`) одразу після `sales`.
- `frontend/messages/uk.json`, `frontend/messages/en.json` — `Dashboard.sidebar.groups`:
  прибрано `sales.events`; додано `calendar.label` / `calendar.events`
  («Календар» / «Календар подій», en: «Calendar» / «Event Calendar»).
- `backend/ShelfGuard.Domain/Constants/TenantRoleTabs.cs` — новий group-key `Calendar =
  "calendar"` у `GroupKeys`; `ItemEvents` ("/events") перенесено з секції `Sales` каталогу в
  нову секцію `Calendar`. Каталог «Видимі розділи» будується динамічно → секція «Календар»
  зʼявляється в редакторі ролей автоматично. `me.tabs` (`AuthService.BuildEffectiveTabsAsync`)
  — pass-through `TenantRole.AllowedTabs`, нічого міняти не треба.
- `frontend/app/(dashboard)/events/page.tsx` — додано
  `useRequireTab("/events", "calendar", hasRole(me?.role, AT_LEAST_STORE_MANAGER))`
  (за зразком `/users`, `/schedules`) — пряме відкриття URL поважає per-role налаштування.
- `.claude/docs/known-issues.md` — KI-019: додано Events sub-decision (календар не module-gated).

## Верифікація

- `frontend`: `npx tsc --noEmit` чисто · `next lint` чисто · `vitest run` 59/59 pass.
- `backend`: `dotnet build` success · `dotnet test --filter TenantRole` 43/43 pass
  (інваріантні `GetTabCatalog_*` тести покривають узгодженість `GroupKeys` ↔ `Catalog`).
- Браузер (локальний backend :5000 + dev DB, тенант «Свіжий Кут», роль store_manager):
  - з `pos` → у Sidebar окрема група CALENDAR поряд із SALES; `/events` відкривається,
    консоль чиста; модалка «New event» працює.
  - `pos` тимчасово прибрано з `tenants.Modules` → **SALES зникає, CALENDAR лишається**;
    `/events` відкривається; `useRequireTab` не редіректить. `pos` повернуто.

## Міграційний нюанс

TenantRole-рядки, що масово надавали всю групу `"sales"`, більше не даватимуть неявно
`/events` (він вийшов із цієї групи). Таких тенантів мало; календар усе одно видно за роллю.

## Поза скопом

- `/customers` теж у `pos`-gated групі «Продажі» — окреме рішення.
- `[RequireModule]` на решті v2/v3-контролерів (повний KI-019).
- openapi.json regen не потрібен (API-поверхня без змін).
