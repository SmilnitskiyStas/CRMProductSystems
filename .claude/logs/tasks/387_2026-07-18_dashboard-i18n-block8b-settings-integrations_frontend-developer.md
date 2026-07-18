# TASK-387: Dashboard i18n (uk/en) — Block 8b: Settings, Integrations, Tenant Roles, Legal Entities, Modules

**Agent:** frontend-developer
**Date:** 2026-07-18
**Status:** done

## Зроблено

Переклав через існуючий `useTranslations`/`DashboardIntlProvider` (Block 1, TASK-376) —
жодного нового provider-wiring. П'ять нових top-level розділів у `Dashboard.*`
(`settings`, `integrations`, `tenantRoles`, `legalEntities`, `modules`) у
`frontend/messages/{uk,en}.json`, додано одразу після `Dashboard.admin` (останній ключ
Block 8a). 244 leaf-ключі, uk/en структурно ідентичні (перевірено скриптом — 0 розбіжностей).

**27 файлів фіча-скоупу** (5 `features/settings/*`, 8 `features/integrations/*`,
6 `features/tenant-roles/*`, 5 `features/legal-entities/*`, 3 `features/modules/*`) +
3 сторінки (`settings/page.tsx`, `settings-user/page.tsx`, `settings/legal-entities/page.tsx`).

- **Перевірка на shared-lib-файл для tenant-roles (як `providerPermissions.ts` у Block
  8a)**: такого файлу **немає**. `TenantRoleCapabilityDto.labelUa` та
  `TenantRoleCapabilityGroup.specialty` — бекенд-сорсний контент (`GET
  /api/tenant-roles/capabilities`), явно закоментовано в `types.ts`: "label is
  backend-sourced Ukrainian text, never hardcode it here" (ADR-020 п.9). Це динамічний
  контент, не статичний UI-текст — поза скоупом i18n (аналогічно backend error-рядкам,
  Block 11, deferred). Не займав.
- **Натомість знайшов і застосував той самий provider Permissions-патерн до ДВОХ інших
  файлів у власному скоупі**, які мали саме той різновид хардкоду:
  - `features/modules/types.ts`: `ALL_MODULES: ModuleMeta[]` (label+description) та
    `BUSINESS_TYPE_LABELS: Record<string,string>` видалено → `ALL_MODULE_KEYS: ModuleKey[]`
    (тільки ключі) + `BUSINESS_TYPE_KEYS: string[]`. Лейбли тепер у
    `Dashboard.modules.catalog.*`/`Dashboard.modules.businessTypes.*`. Єдиний
    call site — `ModulesTab.tsx` (перевірено grep по всьому frontend).
  - `features/integrations/types.ts`: `ServiceMeta.label/description` та
    `ConfigField.label/placeholder/hint` видалено — лишились тільки структурні поля
    (`key`, `type`, `required`, `icon`). Лейбли тепер у
    `Dashboard.integrations.services.<service>.*`/`.fields.<key>.*`. Call sites —
    `IntegrationCard.tsx`, `IntegrationConfigModal.tsx`, `IntegrationsTab.tsx`
    (settings feature) — усі оновлені.
- **`TenantRolesTab.tsx`**: ICU plural замінив ручні `pluralizeCapability(n)`/
  `pluralizeUser(n)` (укр.-only mod10/mod100 логіка, не працювала б для en) —
  `t("capabilityCount", {count})`/`t("userCount", {count})` з
  `{count, plural, one {…} few {…} many {…} other {…}}` (uk) /
  `{one {…} other {…}}` (en). Обидві функції видалено як мертвий код.
- **`LegalEntityFormDialog.tsx`**: zod-схема була module-level константою з
  укр. `.min()/.refine()` message — перенесено у `buildSchema(t)` factory +
  `useMemo(() => buildSchema(t), [t])`, той самий патерн що
  `LocationFormDialog.tsx` (Block 2) уже використовує для тієї ж проблеми.
- **`settings/page.tsx`**: `ALL_TABS`/`SECTIONS`-масиви з лейблами були
  module-level константами — перенесені всередину компонента (потрібен `t` з
  `useTranslations`). Перейменував внутрішні loop-змінні `t` (searchParams tab-param,
  filter callback) на `tabParam`/`tab`, щоб не затінювати `const t = useTranslations(...)`
  (той самий клас бага, що зловив у Block 8a — тут запобіг проактивно).
- **`settings-user/page.tsx`**: `SECTIONS`-масив аналогічно перенесено в компонент.
  `ROLE_LABELS` (з `features/profile/types.ts`, Block 9, ще не перекладено) — імпорт
  залишив без змін, роль-бейдж лишається укр.-only до Block 9 (той самий підхід, що
  8a лишив `ProviderSupportTab.tsx`'s ticket-labels недоторканими).
- Locale-aware дата: `IntegrationCard.tsx`/`IntegrationsTab.tsx`'s inline PrroCard —
  `toLocaleDateString("uk-UA")` → `intlLocale` (`locale === "en" ? "en-US" : "uk-UA"`
  через `useLocale()`), той самий патерн що і Block 8a.

## Верифікація

- `npx tsc --noEmit` — exit 0, без помилок.
- `npm run lint` — exit 0, "No ESLint warnings or errors".
- `npm run build` — exit 0, усі 52 сторінки згенеровано, включно з `/settings` (16.3 kB),
  `/settings-user` (16.1 kB), `/settings/legal-entities` (7.69 kB). Повторювані
  `ENVIRONMENT_FALLBACK`-помилки під час `Generating static pages` — пре-існуючий шум
  (не з моїх файлів, самі трейси вказують у `next-server`/compiled chunks), білд
  завершився `Compiled successfully` + exit 0.
- `docker build -f frontend/Dockerfile frontend` (з кореня репо) — виконано СИНХРОННО
  (вивід у файл + `echo "EXIT: $?"` в тому самому виклику, без background/сну);
  останній рядок логу: `EXIT: 0`. Усі стадії (`npm ci`, `npm run build`,
  `COPY --from=build`, `exporting to image`) завершились без помилок.
- Key-resolution скрипт (scratchpad, position-aware: прив'язує кожен `X("key")`/
  `X.has("key")` до найближчого **попереднього по порядку рядків у файлі**
  `const X = useTranslations(ns)`) — **192 статичних виклики з 16 файлів, 0
  непрорезольваних ключів** в обох `messages/{uk,en}.json`. Плюс 11 динамічних
  template-literal викликів (`${key}.label`, `${meta.service}.description`,
  `${fieldPrefix}.hint` тощо) перевірено окремим скриптом підстановкою всіх реальних
  значень (`ALL_MODULE_KEYS` ×2 атрибути, `ALL_SERVICES` ×2, кожне
  `service.fields.*` ×2 + hint-presence-symmetry) — **48/48 резолвляться**, hint
  presence симетрична між uk/en у всіх 12 полів (жодного "hint показується тільки в
  одній locale"). Окрема структурна перевірка дерева — 244/244 leaf-ключів
  збігаються uk↔en.

## Файли

`frontend/features/settings/components/{NotificationsTab,IntegrationsTab,ModulesTab,
ProfileTab,MarketplaceProfileTab}.tsx` (останній — без змін, немає хардкоду),
`frontend/features/integrations/{types.ts,components/IntegrationCard.tsx,
components/IntegrationConfigModal.tsx,components/PrroConfigModal.tsx}` (api/*.ts,
hooks/*.ts — без змін, немає UI-тексту),
`frontend/features/tenant-roles/components/{TenantRolesTab,TenantRoleSelector,
TenantRoleBadge}.tsx` (types.ts/api/hooks — без змін),
`frontend/features/legal-entities/components/{LegalEntityFormDialog,
LegalEntitiesList}.tsx` (types.ts/api/hooks — без змін),
`frontend/features/modules/types.ts` (api/hooks — без змін),
`frontend/app/(dashboard)/{settings/page,settings-user/page,
settings/legal-entities/page}.tsx`,
`frontend/messages/{uk,en}.json` (нові `Dashboard.{settings,integrations,tenantRoles,
legalEntities,modules}.*`).

## Не в скоупі (свідомо)

- `features/provider/*`, `features/admin/*` — Block 8a, готово, не займав.
- Усе перекладене в Block 1-7.
- `features/profile/types.ts`'s `ROLE_LABELS` — Block 9, `settings-user/page.tsx`
  споживає його як є (роль-бейдж лишається укр.-only до Block 9).
- `TenantRoleCapabilityDto.labelUa`/`TenantRoleCapabilityGroup.specialty` —
  бекенд-сорсний динамічний контент (ADR-020), не UI-текст для i18n.
- Git commit/push — за інструкцією, користувач сам комітить і стежить за CI/деплоєм.
