# TASK-386: Dashboard i18n (uk/en) — Block 8a: Provider & Platform Admin

**Agent:** frontend-developer
**Date:** 2026-07-18
**Status:** done

## Зроблено

Переклав через існуючий `useTranslations`/`DashboardIntlProvider` (Block 1, TASK-376) —
жодного нового provider-wiring. Два нових top-level розділи `Dashboard.provider` (23
під-неймспейси) та `Dashboard.admin` (7 під-неймспейсів) у `frontend/messages/{uk,en}.json`
(додано одразу після `Dashboard.supplierCabinet`, останній ключ Block 7). 412 leaf-ключів,
uk/en структурно ідентичні (перевірено скриптом).

**22 файли скоупу** (14 `features/provider/components/*.tsx` + `features/provider/types.ts`
+ 3 `features/admin/components/*.tsx` + `features/admin/types.ts` + 3 сторінки), плюс 1 файл
поза формальним скоупом, зачеплений свідомо:

- **`provider/types.ts` / `admin/types.ts`**: видалив україномовні `*_LABELS` Record-и
  (`BUSINESS_TYPE_LABELS`, `MODULE_LABELS`, `MODULE_DESCRIPTIONS`, `PLAN_LABELS`) —
  замінені на `t(key)`/`t.has(key)` у місцях використання. Кольори (`PLAN_COLORS`),
  іконки (`BUSINESS_TYPE_ICONS`), список ключів (`ALL_*`), presets — лишились без змін
  (не текст). Перевірив grep — жоден інший файл (окрім `features/modules/*`,
  `features/settings/*`, Block 8b, у яких власні незалежні копії) ці константи не імпортує.
- **`lib/providerPermissions.ts`** (поза `features/provider/`, але напряму споживається
  трьома компонентами скоупу): `PROVIDER_PERMISSIONS` (укр. лейбл-мапа) видалено, замінено
  на `t(key)` у RolesSection/EditMemberModal/InviteProviderMemberModal через спільний
  `Dashboard.provider.permissions`; `ALL_PERMISSIONS` лишився масивом ключів (тепер `string[]`
  напряму, без `Object.keys(...)`). Перевірив: `Sidebar.tsx` (Block 1, вже задеплоєний)
  імпортує з цього файлу лише `SYSTEM_ROLE_PERMISSIONS`/`resolvePermissions` — не зачеплено.
- **Спільні enum-неймспейси в межах `provider`**: `roleLabels` (Власник/Адмін/Агент —
  раніше дубльований `ROLE_LABELS`-const в TeamTab.tsx і StatsTab.tsx), `roleSelector`
  (`baseRoleLabels`+`systemRolesGroup`+`customRolesGroup` — раніше дубльовані
  `BASE_ROLE_LABELS`-const/hardcoded optgroup-лейбли в RolesSection/EditMemberModal/
  InviteProviderMemberModal), `permissions`, `businessTypes`, `modules`,
  `moduleDescriptions`, `plans` — усі дубльовані Record-и видалені, кожен споживач
  отримав власний `useTranslations` на спільний неймспейс.
- **`ProviderSupportTab.tsx`**: `TICKET_STATUS_LABELS`/`TICKET_PRIORITY_LABELS`/
  `TICKET_CATEGORY_LABELS` та `TicketStatusBadge`/`PriorityBadge` — імпорт з
  `features/service-desk` (Block 10, ще не перекладений) — залишив без змін, коментар у
  коді пояснює чому. Переклав лише власний текст файлу (~35 ключів).
- **Locale-aware formatting**: усі `toLocaleString/toLocaleDateString/toLocaleTimeString
  ("uk-UA", ...)` → `intlLocale` (`locale === "en" ? "en-US" : "uk-UA"` через `useLocale()`)
  в TenantDetailPanel/ProviderLogsPanel/ChatSupportTab/ProviderSupportTab (provider) та
  TenantDetailDrawer (admin) — module-level `formatDate`/`formatTime`/`formatDateTime`
  helpers отримали `locale` параметром.
- **`ScheduleTab.tsx`**: `DAY_LABELS`/`DAY_FULL` масиви (Пн/Вт/…, Понеділок/Вівторок/…) →
  `t(\`dayShort.${key}\`)`/`t(\`dayFull.${key}\`)` з ключовим масивом `DAY_KEYS` замість
  тексту (index-based day-of-week логіка не змінена).
- **`ProviderLogsPanel.tsx`**: `actionLabel()` (module-level функція з укр. Record) →
  приймає `t` параметром, `t(\`actions.${action}\`)` з fallback на raw action через
  `KNOWN_ACTIONS.includes()`-guard (та сама defensive-логіка, що й у оригіналі).
- **Знайдено і виправлено 3 баги в процесі роботи** (спіймано до tsc/build, не потрапило
  в раннер):
  1. `ALL_PERMISSIONS` з `as const` зламав `Record<string,string[]>`-присвоєння в
     `SYSTEM_ROLE_PERMISSIONS` (2 tsc-помилки) — повернув до звичайного `string[]`.
  2. Дублікат `"use client";` (моя ж помилка редагування) в `TenantTable.tsx` — виправлено.
  3. `.map((t) => ...)` затінив зовнішній `const t = useTranslations(...)` в
     `TenantTable.tsx` (два виклики `t("detailsButton")`/`t("deactivateShort")` всередині
     map зламались би — компілятор зловив би тип-помилку). Перейменував loop-змінну на
     `tenant`. Проактивно перейменував аналогічні loop-змінні (`t`→`tn`/`tenant`/
     `tenantOpt`) ще в 4 файлах (provider/page.tsx, admin/page.tsx, ProviderLogsPanel.tsx,
     ProviderSupportTab.tsx) до того, як стало проблемою.

## Верифікація

- `npx tsc --noEmit` — exit 0 (після виправлення #1 вище; перший прогін дав 2 помилки).
- `npm run lint` — exit 0, "No ESLint warnings or errors".
- `npm run build` — exit 0, усі сторінки згенеровано, включно з `/provider` (10.3 kB),
  `/provider/team` (6.66 kB), `/admin` (8.9 kB).
- `docker build -f frontend/Dockerfile frontend` (з кореня репо) — виконано СИНХРОННО з
  виводом у файл і `echo "EXIT: $?"` одразу після; останній рядок логу: `EXIT: 0`. Усі
  17 build-стадій (`npm ci`, `npm run build`, `COPY --from=build`, `exporting to image`)
  завершились без помилок.
- Key-resolution скрипт (scratchpad, position-aware: прив'язує кожен `X("key")` до
  найближчого **попереднього по порядку рядків у файлі** `const X = useTranslations(ns)`)
  — **354 статичних виклики з 20 файлів, 0 непрорезольваних ключів** в обох
  `messages/{uk,en}.json`. Плюс 4 динамічні template-literal виклики
  (`actions.${action}`, `dayShort.${dayKey}` ×2, `dayFull.${dayKey}`) перевірено вручну —
  усі цільові під-об'єкти (`logsPanel.actions.user.*`/`.provider.impersonate`,
  `scheduleTab.dayShort.*`/`dayFull.*`) присутні в обох locale. Окрема структурна
  перевірка симетрії дерева `Dashboard.provider`/`Dashboard.admin` uk↔en — 412/412 leaf-
  ключів збігаються.

## Файли

`frontend/features/provider/components/{TenantCard,ImpersonationBanner,
TenantDetailPanel,CreateTenantWizard,AddTenantUserModal,RolesSection,TeamTab,StatsTab,
EditMemberModal,InviteProviderMemberModal,ProviderLogsPanel,ScheduleTab,ChatSupportTab,
ProviderSupportTab}.tsx`, `frontend/features/provider/types.ts`,
`frontend/features/admin/components/{TenantTable,TenantDetailDrawer,CreateTenantModal}.tsx`,
`frontend/features/admin/types.ts`,
`frontend/app/(dashboard)/{provider/page,provider/team/page,admin/page}.tsx`,
`frontend/lib/providerPermissions.ts`,
`frontend/messages/{uk,en}.json` (нові `Dashboard.provider.*`/`Dashboard.admin.*`).

## Не в скоупі (свідомо)

- `modules`, `tenant-roles`, `legal-entities`, `settings`, `integrations` — Block 8b,
  окрема задача.
- Усе перекладене в Block 1-7.
- `features/service-desk/*` (Block 10) — `ProviderSupportTab.tsx` імпортує звідти
  нетранслейтед `TICKET_*_LABELS`/badge-компоненти, не редагував.
- `features/modules/types.ts`/`features/settings/components/ModulesTab.tsx` — власні
  незалежні копії `BUSINESS_TYPE_LABELS`/`ALL_MODULES`, не пов'язані імпортом з
  `provider`/`admin` types.ts (перевірено grep), не займав.
- Git commit/push — за інструкцією, користувач сам комітить і стежить за CI/деплоєм.
