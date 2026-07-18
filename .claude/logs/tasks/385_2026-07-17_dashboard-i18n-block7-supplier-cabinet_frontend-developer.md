# TASK-385: Dashboard i18n (uk/en) — Block 7: Supplier Cabinet (B2B supplier side)

**Agent:** frontend-developer
**Date:** 2026-07-17
**Status:** done

## Зроблено

Переклав через існуючий `useTranslations`/`DashboardIntlProvider` (Block 1, TASK-376) —
жодного нового provider-wiring. Один новий top-level розділ `Dashboard.supplierCabinet` у
`frontend/messages/{uk,en}.json` (додано одразу після `Dashboard.marketplace`, останній
ключ Block 6), 19 під-неймспейсів (по одному на компонент/сторінку-групу + спільний
`taskStatus`).

**16 файлів фічі + 11 сторінок** (усі файли з Cyrillic; `types.ts`/
`api/supplier-cabinet-api.ts`/`hooks/{useSupplierCabinet,useCabinetCooperation}.ts` —
лише коментарі розробника, 0 user-facing рядків, підтверджено grep, не займав):

- **Спільний `taskStatus`** (`pending`/`in_progress`/`completed`/`cancelled`) — раніше
  дубльований `STATUS_LABELS`-const в `TasksCalendar.tsx` і `TasksBoard.tsx`, тепер один
  неймспейс через окремий `useTranslations("Dashboard.supplierCabinet.taskStatus")` в
  обох файлах (константи видалені).
- **`CabinetSupportTab.tsx`**: замість імпорту сирого `TICKET_STATUS_LABELS` з
  `CooperationBadges.tsx` (Block 6 explicitly залишив цей експорт саме для сумісності з
  цим ще неперекладеним файлом) — перевів `<select>`-опції статусу на
  `useTranslations("Dashboard.marketplace.ticketStatus")` (той самий неймспейс, що вже
  використовує `TicketStatusBadge`). `CooperationBadges.tsx` не редагував (не в скоупі);
  `TICKET_STATUS_LABELS`/`AGREEMENT_STATUS_LABELS`/`ORDER_STATUS_LABELS` лишаються
  експортованими, але тепер без жодного споживача в репо (перевірено grep) — нешкідливо.
- **`RolesTab.tsx`**: `SUPPLIER_PERMISSIONS` (лейбл-мапа з `lib/supplierPermissions.ts`,
  файл поза скоупом задачі) замінено на локальний `t(\`permissionLabels.${p}\`)` —
  `lib/supplierPermissions.ts` НЕ редагував, `ALL_SUPPLIER_PERMISSIONS` (масив ключів)
  лишився імпортованим і використовується як і раніше. `BASE_ROLE_LABELS`-const видалено,
  замінено на `t("baseRoleLabels.supplier_admin")`.
- **`CabinetItemModal.tsx`** (спеціальний випадок з брифу): виклики
  `findMissingRequiredField(...)`/`parseExtraFields(...)` (спільні функції з
  `features/marketplace/components/{ItemCategoryFields,SupplierItemExtraFields}.tsx`,
  Block 6 додав їм опціональний `t?`-параметр саме для цього моменту) тепер отримують
  `tCategoryFields`/`tExtraFields` — два окремі `useTranslations` виклики на неймспейси
  `Dashboard.marketplace.{itemCategoryFields,itemExtraFields}`. Кабінет постачальника
  тепер повністю двомовний, без україномовного fallback.
- **Знайдено і виправлено 2 бага змінного затінення** (`.map((t) => ...)` затіняв
  зовнішній `const t = useTranslations(...)`) — в `CabinetSupportTab.tsx` (перейменував
  loop-змінну на `ticketItem`) і `CooperationRequestsTab.tsx` (`FILTER_TABS.map((t) =>` →
  `(tab) =>`). Спіймано до tsc/build, не потрапило в раннер.
- **Виправлено 1 пропущений рядок** при першому проході: `" · Вчасно"`-суфікс біля номера
  договору в `CooperationRequestsTab.tsx` (не коментар, реальний UI-текст) — додав ключ
  `vchasnoSuffix` (uk: " · Вчасно", en: " · Vchasno" — власна назва сервісу транслітерована,
  як і в `vchasnoChoice`).
- **Локалізований non-UI backend-текст лишив недоторканим свідомо**: substring-перевірка
  `err.message.toLowerCase().includes("реквізит")` в `CooperationRequestsTab.tsx` —
  бекенд повертає україномовний текст помилки незалежно від locale (Block 11
  rollout-плану, поза скоупом); залишив коментар з поясненням.
- **Сторінки**: усі 11 (`items,reviews,profile,team,tasks,requests,contract-settings,
  orders,support,messages,clients`) — однаковий патерн `useTranslations("Dashboard.
  supplierCabinet.pages")`, спільний `supplierOnlyAccess` (був продубльований 11 разів
  україномовним літералом), плюс `team.noAccess`/`tasks.noAccess`/`clients.noAccess` для
  трьох сторінок з додатковою permission-перевіркою.

**Locale-aware formatting:** усі `toLocaleString/toLocaleDateString/toLocaleTimeString
("uk-UA", ...)` → `intlLocale` (`locale === "en" ? "en-US" : "uk-UA"` через `useLocale()`)
у CabinetItemsTable/CabinetReviews/TasksCalendar/TasksBoard/CabinetOrdersTab/
CabinetSupportTab/SupplierClientChatPanel/ChatInboxTab/ClientsTab/CooperationRequestsTab
(`money()`/`formatDate()` module-level helpers отримали `locale` параметром). Currency
(UAH) лишився без змін у всіх locale.

## Верифікація

- `npx tsc --noEmit` — exit 0 (окремо, до build — спіймав би обидва variable-shadowing
  баги і сигнатуру `findMissingRequiredField`/`parseExtraFields`, якби щось було зламано).
- `npm run lint` — exit 0, "No ESLint warnings or errors".
- `npm run build` — exit 0, усі 52 сторінки згенеровано, включно з усіма 11
  `/supplier/*`. `ENVIRONMENT_FALLBACK`-шум — той самий pre-existing діагностичний код
  (з'являється і на `/login`, не мій), підтверджений у Block 2a-6.
- `docker build -f frontend/Dockerfile frontend` (з кореня репо) — виконано двічі
  синхронно: перший раз через `| tail`, другий раз з виводом у файл саме для коректної
  перевірки exit code напряму (пайп через `tail` повертає exit code `tail`, не `docker
  build` — відомий підводний камінь). Реальний exit 0 підтверджено після другого прогону
  (усі шари CACHED, той самий route table в контейнері).
- Key-resolution скрипт (scratchpad, position-aware: прив'язує кожен `X("key")`/
  `X(\`prefix.${var}\`)` до найближчого **попереднього за позицією символу у файлі**
  `const X = useTranslations(ns)`, а не глобально по імені змінної — перша версія
  скрипта дала 25 хибних спрацювань саме через це на `RolesTab.tsx`/`TasksBoard.tsx`,
  де `const t` легітимно переоголошується в вкладеному modal-компоненті з іншим
  неймспейсом) — **313 статичних викликів з 27 файлів, 0 непрорезольваних ключів** в
  обох `messages/{uk,en}.json`. Плюс окрема структурна перевірка симетрії ключів
  (рекурсивний diff дерева) для `Dashboard.supplierCabinet` і трьох перевикористаних
  marketplace-неймспейсів (`itemCategoryFields`/`itemExtraFields`/`ticketStatus`) — OK.

## Файли

`frontend/features/supplier-cabinet/components/{CabinetItemsTable,CabinetItemModal,
CabinetStaffPanel,CabinetReviews,RolesTab,InviteStaffModal,CabinetProfileForm,ClientsTab,
TasksCalendar,TasksBoard,ContractSettingsForm,CabinetOrdersTab,CabinetSupportTab,
SupplierClientChatPanel,ChatInboxTab,CooperationRequestsTab}.tsx`,
`frontend/app/(dashboard)/supplier/{items,reviews,profile,team,tasks,requests,
contract-settings,orders,support,messages,clients}/page.tsx`,
`frontend/messages/{uk,en}.json` (новий `Dashboard.supplierCabinet.*`, 19 під-неймспейсів;
повторне використання `Dashboard.marketplace.{itemCategoryFields,itemExtraFields,
ticketStatus}` без змін тих неймспейсів).

## Не в скоупі (свідомо)

- Усе перекладене в Block 1-6, включно з `features/marketplace/*` (читав лише для
  контексту спільних функцій/неймспейсів, жодного файлу там не редагував).
- `lib/supplierPermissions.ts` — не редагував; `SUPPLIER_PERMISSIONS` (display-мапа)
  замінено на переклад лише в `RolesTab.tsx` (споживач), `ALL_SUPPLIER_PERMISSIONS`
  (масив ключів) лишився імпортованим без змін.
- `features/marketplace/components/CooperationBadges.tsx` — не редагував; коментар у
  файлі про "Block 7, ще не перекладений" тепер трохи застарілий (єдиний споживач сирих
  `*_STATUS_LABELS` вже не існує), але сам файл поза скоупом цієї задачі.
- Backend-driven помилки (`err.message.includes("реквізит")`) — Block 11 rollout-плану.
- Git commit/push — за інструкцією, користувач сам комітить і стежить за CI/деплоєм.
