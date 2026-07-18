# TASK-388: Dashboard i18n (uk/en) — Block 9: People Ops (Users, Schedules, Profile, Notifications)

**Agent:** frontend-developer
**Date:** 2026-07-18
**Status:** done

## Зроблено

Переклав через існуючий `useTranslations`/`DashboardIntlProvider` (Block 1, TASK-376) —
жодного нового provider-wiring. Чотири нових top-level розділи у `Dashboard.*` (`roles`
— новий спільний неймспейс, `users`, `schedules`, `notifications`) додано одразу після
`Dashboard.modules` (останній ключ Block 8) у `frontend/messages/{uk,en}.json`, плюс
розширення вже існуючого `Dashboard.profile` (Block 1 має тільки `language`) п'ятьма
секціями (`infoForm`, `changePassword`, `twoFactor`, `telegram`, `card`). 427 leaf-ключів,
uk/en структурно ідентичні (перевірено скриптом — 0 розбіжностей).

**22 файли фіча-скоупу** (6 `features/users/*`, 5 `features/schedules/*`,
5 `features/profile/*`, 4 `features/notifications/*`) + 3 сторінки (`users/page.tsx`,
`schedules/page.tsx`, `notifications/page.tsx`). `LanguageSwitcher.tsx` не займав
(Block 1, за інструкцією).

- **Спільні хардкод-константи, що перетинали кілька фіч** (як `ALL_PERMISSIONS`/
  `providerPermissions.ts` у Block 8a) — конвертовані на `KNOWN_KEYS[]` + `getXLabel(t, key)`
  helper-функції замість прямого `Record<string,string>`:
  - `ROLE_LABELS` (`features/profile/types.ts`) → `ROLE_KEYS[]` + `getRoleLabel(t, role)`.
    Найширший call-site: 7 файлів у скоупі + **2 файли поза декларованим скоупом Block 9**
    (`components/layout/UserMenu.tsx` — Block 1, `app/(dashboard)/settings-user/page.tsx` —
    Block 8b явно залишив цей рядок недоторканим "до Block 9", див. його task log). Обидва
    отримали лише точковий фікс (новий `useTranslations("Dashboard.roles")` + заміна одного
    рядка), решта файлу не чіпав.
  - `ACTION_LABELS` (`features/users/types.ts`) → `KNOWN_ACTIONS[]` + `getActionLabel(t, action)`,
    той самий патерн що `ProviderLogsPanel.tsx`'s `actionLabel()` (Block 8a) — action-рядки
    з крапкою ("user.invited") мапляться на nested JSON (`actions.user.invited`), не flat-key.
  - `PAGES[].label` (`features/users/types.ts`) → прибрано з `PageDef`, лейбли через
    `t(page.slug)` (слаги без крапок — самі є валідним leaf-key, мапінг-таблиця не потрібна).
  - `EVENT_TYPE_LABELS`/`EVENT_TYPE_SOURCE`/`CHANNEL_LABELS` (`features/notifications/types.ts`)
    → `EVENT_TYPE_I18N_KEY` map + `getEventTypeLabel`/`getEventTypeSource`/`getChannelLabel`.
    `CHANNEL_ICONS` (емодзі, не текст) лишив як є.
- **Дубльовані locale-константи в межах фічі** (не спільні між фічами, тому НЕ винесені в
  helper) — перекладені на місці кожна: `ShiftStatus`-лейбли (ShiftForm.tsx/MyShifts.tsx,
  спільний неймспейс `Dashboard.schedules.shiftStatus`), `ScheduleStatus`-лейбли
  (ScheduleForm.tsx і ScheduleList.tsx мають РІЗНИЙ текст для "archived" в оригіналі —
  залишив цю розбіжність, не уніфікував).
- **`WeekGrid.tsx`**: `DAY_LABELS` masив → `DAY_KEYS` + `t(\`dayShort.${key}\`)`, той самий
  патерн що `features/provider/components/ScheduleTab.tsx`'s `DAY_KEYS`/`dayShort` (Block 8a).
- **`NotificationHistoryList.tsx`**: `formatPayloadPreview()` — складна функція генерації
  прев'ю з JSON payload (7 різних текстових шаблонів залежно від eventType) — переведена на
  `t`-параметр (виклик з `tPayload`, окремий неймспейс
  `Dashboard.notifications.history.payload`).
- Locale-aware дати: усюди де був `toLocaleDateString/toLocaleString("uk-UA", ...)` —
  `useLocale()` + `intlLocale = locale === "en" ? "en-US" : "uk-UA"`, той самий патерн що
  `ProviderLogsPanel.tsx` (Block 8a).
- ICU plural: `footerCount` (users), `shiftsCount` (schedules), `maxDurationError`/
  `tempExpiresLabel` (permission-grant days) — `{count, plural, one {...} few {...}
  many {...} other {...}}` (uk) / `{one {...} other {...}}` (en), той самий патерн що
  `capabilityCount`/`userCount` (Block 8b).

## Верифікація

- `npx tsc --noEmit` — перший прогін: 2 помилки (1 — `tRoles` used у `PageHeader()`, а
  `useTranslations` був доданий у сусідню `UserSettingsPage()`, різний React-компонент =
  різний scope; 2 — `formatPayloadPreview`'s власний `t`-параметр типізований
  `Record<string, unknown>`, next-intl очікує `Record<string, string|number|Date>`). Обидва
  виправлено. Другий прогін — exit 0, без помилок.
- `npm run lint` — exit 0, "No ESLint warnings or errors".
- `npm run build` — exit 0, усі сторінки згенеровано, включно з `/users` (13.7 kB),
  `/schedules` (11.1 kB), `/settings-user` (14.8 kB, ripple-фікс).
  `/notifications` — SSG, білд пройшов без помилок.
- `docker build -f frontend/Dockerfile frontend` (з кореня репо) — виконано СИНХРОННО
  (вивід у файл + `echo "EXIT: $?"` в тому самому виклику); останній рядок логу: `EXIT: 0`.
  Усі стадії (`npm ci`, `npm run build` усередині контейнера, `exporting to image`) —
  без помилок.
- Key-resolution: два скрипти. (1) Статичний regex-парсер прив'язує кожен `X("literal")` до
  найближчого попереднього `const X = useTranslations(ns)` у тому самому файлі — 419
  викликів, 0 непрорезольваних (9 початкових "MISSING" виявились false-positive через
  shadowing — `formatPayloadPreview(t, ...)`'s локальний параметр `t` збігається ім'ям з
  зовнішнім component-level `t`, скрипт не робить справжній lexical scope). (2) Окремий
  скрипт явно підставив усі реальні значення для 12 динамічних/indirect шляхів
  (`getRoleLabel`, `getActionLabel`, `tPages`, `getEventTypeLabel`, `getChannelLabel`,
  `getEventTypeSource`, `tStatus`, `tShiftStatus`, обидва Schedule-status шляхи, `dayShort`,
  `tPayload`) — 100+ конкретних ключів, усі резолвляться в обох locale.

## Файли

`frontend/features/users/{types.ts,components/{UsersList,UserDetailPanel,
UserPermissionsEditor,UserActivityLog,InviteUserModal}.tsx}` (api/hooks — без змін),
`frontend/features/schedules/components/{ScheduleForm,ScheduleList,ShiftForm,MyShifts,
WeekGrid}.tsx` (types.ts/api/hooks/ShiftCard.tsx — без змін, немає UI-тексту),
`frontend/features/profile/{types.ts,components/{ProfileInfoForm,ChangePasswordForm,
TwoFactorSection,TelegramLinkSection,UserProfileCard}.tsx}` (LanguageSwitcher.tsx —
не займав, Block 1; api/hooks — без змін),
`frontend/features/notifications/{types.ts,components/{NotificationHistoryList,
NotificationFilterDrawer,NotificationSettingsTable,NotificationDetailDrawer}.tsx}`
(api/hooks — без змін),
`frontend/app/(dashboard)/{users/page,schedules/page,notifications/page}.tsx`,
`frontend/messages/{uk,en}.json` (нові `Dashboard.{roles,users,schedules,notifications}.*`
+ розширення `Dashboard.profile.*`).

**Ripple-фікси поза декларованим скоупом** (мінімальні, точкові — інакше зламали б build
через зміну сигнатури спільного `ROLE_LABELS`):
`frontend/components/layout/UserMenu.tsx` (Block 1), `frontend/app/(dashboard)/settings-user/page.tsx`
(Block 8b) — обидва отримали `useTranslations("Dashboard.roles")` + заміну
`ROLE_LABELS[role] ?? role` на `getRoleLabel(tRoles, role)`, жодних інших змін.

## Не в скоупі (свідомо)

- Усе перекладене в Block 1-8.
- `LanguageSwitcher.tsx` — Block 1, за інструкцією.
- Git commit/push — за інструкцією, користувач сам комітить і стежить за CI/деплоєм.
