# TASK-389: Dashboard i18n (uk/en) — Block 10: Support, Chat & Misc (останній блок хвилі)

**Agent:** frontend-developer
**Date:** 2026-07-19
**Status:** done

## Зроблено

Переклав через існуючий `useTranslations`/`DashboardIntlProvider` (Block 1, TASK-376) —
жодного нового provider-wiring. П'ять нових top-level розділів у `Dashboard.*`
(`serviceDesk`, `chat`, `aiAssistant`, `events`, `iot`) додано одразу після
`Dashboard.notifications` (останній ключ Block 9) у `frontend/messages/{uk,en}.json`.
211 leaf-ключів, uk/en структурно ідентичні (перевірено скриптом — 0 розбіжностей).

**22 файли фіча-скоупу** (12 `features/service-desk/*` — 7 змінено, 5 без Cyrillic;
5 `features/chat/*` — 2 змінено, 3 без Cyrillic; 4 `features/ai-assistant/*` — 1 змінено,
3 без Cyrillic; 5 `features/events/*` — 3 змінено, 2 без Cyrillic; 6 `features/iot/*` —
4 змінено, 2 без Cyrillic) + 3 сторінки (`service-desk/page.tsx`, `events/page.tsx`,
`iot/page.tsx`). `components/layout/SupportChatWidget.tsx` не займав (Block 1, окремий
компонент зі своїм `Dashboard.supportChat` неймспейсом — підтверджено, конфліктів немає).

- **Enum-style лейбли** (`TICKET_STATUS_LABELS`/`TICKET_PRIORITY_LABELS`/
  `TICKET_CATEGORY_LABELS` у `service-desk/types.ts`, `EVENT_TYPE_META` у `events/types.ts`,
  `DEVICE_TYPE_META` у `iot/types.ts`) → `KEYS[]` array + `getXLabel(t, key)` helper-функції,
  той самий патерн що `getRoleLabel`/`getEventTypeLabel` (Block 9). Де мапа поєднувала
  текст+стиль (`EVENT_TYPE_META.{label,color,bg}`, `DEVICE_TYPE_META.{label,icon}`) —
  розділив на текстовий i18n-helper + окремий `_STYLES`/`_ICONS` non-text constant.
- **Cross-feature ripple (обов'язковий, інакше зламав би build)**:
  `features/provider/components/ProviderSupportTab.tsx` (Block 8a) напряму імпортував старі
  `TICKET_*_LABELS` Record-и з `service-desk/types.ts` з явним коментарем "не перекладено ще
  (Block 10)" — після зміни їх на функції оновив усі 4 компоненти файлу (`TicketRow`,
  `TicketDetailPanel`, `CreateTicketModal`, `ProviderSupportTab`) на нові
  `getTicketStatusLabel`/`getTicketPriorityLabel`/`getTicketCategoryLabel` + власні
  `useTranslations("Dashboard.serviceDesk.{statuses,priorities,categories}")`. Жодного
  власного тексту `ProviderSupportTab.tsx` (вже перекладений) не чіпав.
- **Zod-схеми з validation-повідомленнями** (`EventForm.tsx`, `DeviceFormDialog.tsx`) →
  `buildSchema(t)` + `useMemo(() => buildSchema(t), [t])`, той самий патерн що
  `LegalEntityFormDialog.tsx` (Block 8b).
- **Weekday/month array-константи**: `EventCalendar.tsx`'s `WEEKDAYS` → `t.raw("weekdayLabels")`
  (той самий патерн що `TasksCalendar.tsx`, Block 7); `events/page.tsx`'s `MONTHS` масив →
  прибрано, замінено на `toLocaleDateString(intlLocale, { month: "long", year: "numeric" })`
  (той самий підхід, що `TasksCalendar.tsx`'s `monthLabel`, вже в проді).
- Locale-aware дати/час: усюди де був `toLocaleDateString/toLocaleString/toLocaleTimeString
  ("uk-UA", ...)` — `useLocale()` + `intlLocale = locale === "en" ? "en-US" : "uk-UA"`.
- ICU plural: `serviceDesk.ticketList/myTicketList.countLabel` (тікет/тікети/тікетів —
  замінив попередню ручну `count < 5` евристику на коректний CLDR one/few/many/other),
  `aiAssistant.widget.badge{CriticalBatches,Orders,SalesRows,Suppliers}`.
- `AiAssistantWidget.tsx` (компонент не змонтований в жодному дереві — інвентаризацію
  підтвердив, `SupportChatWidget.tsx` дублює цю ж функціональність окремою реалізацією) —
  перекладено повністю, як і вказано в скоупі, попри поточну відсутність точки монтування.

## Верифікація

- `npm run build` — exit 0, усі 52 роутів згенеровано, включно з `/service-desk` (12.7 kB),
  `/events` (6.65 kB), `/iot` (6.83 kB). (У логах є повторювані `ENVIRONMENT_FALLBACK`
  трейси під час "Generating static pages" — без прив'язки до файлів, не нові, не з
  моїх змін, білд завершився success.)
- `npm run lint` — exit 0, "No ESLint warnings or errors".
- `npx tsc --noEmit` — exit 0, порожній вивід, 0 помилок.
- `docker build -f frontend/Dockerfile frontend` (з кореня репо) — виконано СИНХРОННО
  (вивід у файл + `echo "EXIT: $?"` в тому самому виклику); `EXIT: 0`. `npm run build`
  усередині контейнера відпрацював реально (110.7s, не з кешу) — та сама 52-роутна
  вихідна структура, `exporting to image` без помилок.
- Key-resolution: regex-скрипт зв'язав кожен `X("literal")`/`X.raw("literal")` з найближчим
  `const X = useTranslations(ns)` у файлі — 224 виклики. Перший прогін дав 22 "FAIL" — усі
  виявились false-positive через один справжній edge-case: `EventForm.tsx` має ДВА `const
  t = useTranslations(...)` з РІЗними неймспейсами в різних компонентах (`EventForm` і
  вкладений `CoefficientEditor`) під однаковим ім'ям змінної `t` — скрипт (flat regex, не
  AST) не розрізняє function-scope. Обидва набори ключів (21 у `eventForm.*`, 5 +
  3 `scopeTypes.*` у `eventForm.coefficientEditor.*`) вручну підтверджені окремим
  прямим object-lookup — резолвляться в обох locale. Другий шар: structural-symmetry
  скрипт підтвердив 0 розбіжностей ключів між uk.json/en.json для всіх 5 нових
  неймспейсів (68+30+14+48+51 = 211 leaf-ключів).

## Файли

`frontend/features/service-desk/{types.ts,components/{TicketStatusBadge,PriorityBadge,
TicketCard,TicketList,TicketDetail,CreateTicketForm,MyTicketList}.tsx}`
(api/*, hooks/* — без змін, немає Cyrillic),
`frontend/features/chat/components/{RatingModal,ClientChatPanel}.tsx`
(types.ts, api/chat-api.ts, hooks/useChat.ts — без змін),
`frontend/features/ai-assistant/components/AiAssistantWidget.tsx`
(types.ts, api/aiAssistant.ts, hooks/useAiAssistant.ts — без змін),
`frontend/features/events/{types.ts,components/{EventCalendar,EventForm}.tsx}`
(api/events.ts, hooks/useEvents.ts — без змін),
`frontend/features/iot/{types.ts,components/{DeviceFormDialog,DevicesTable,
TemperaturePanel}.tsx}` (api/iot.ts, hooks/useIot.ts — без змін),
`frontend/app/(dashboard)/{service-desk/page,events/page,iot/page}.tsx`,
`frontend/messages/{uk,en}.json` (нові `Dashboard.{serviceDesk,chat,aiAssistant,
events,iot}.*`).

**Ripple-фікс поза декларованим скоупом** (обов'язковий, мінімальний, точковий —
інакше зламав би build через зміну сигнатури спільних `TICKET_*_LABELS`):
`frontend/features/provider/components/ProviderSupportTab.tsx` (Block 8a) — оновлено
лише імпорти й виклики `getTicketStatusLabel`/`getTicketPriorityLabel`/
`getTicketCategoryLabel` + 3 нових `useTranslations` у 4 компонентах файлу; жодного
власного (вже перекладеного) тексту файлу не чіпав.

## Не в скоупі (свідомо)

- Усе перекладене в Block 1-9.
- `components/layout/SupportChatWidget.tsx` — Block 1, окремий компонент, за інструкцією.
- Git commit/push — за інструкцією, користувач сам комітить і стежить за CI/деплоєм.

## Примітка щодо завершення хвилі i18n-rollout (Blocks 0-10)

Перед тим, як вважати весь `.claude/docs/i18n-rollout-plan.md` завершеним, варто
перевірити:
- **Block 11 (non-UI text sources)** у плані явно відкладений — backend error/validation
  рядки (92 хардкод-рядки, 16 файлів), `worker/src/jobs/*` (19 рядків, 4 файли), Checkbox
  ПРРО (2 рядки), email-шаблони. Це окремий scope, не частина Blocks 0-10.
  Mobile (Expo) — явно поза хвилею.
- Жоден агент Blocks 1-10 (включно з цим) не проганяв **наскрізний** key-resolution скрипт
  по ВСІХ `Dashboard.*` неймспейсах одразу — кожен блок верифікував лише свій скоуп
  (+ ripple-фікси). Разова наскрізна перевірка (всі ~40+ файлів `app/(dashboard)/*` +
  `features/*`, увесь `messages/{uk,en}.json`) закрила б залишковий ризик десинхронізації
  між суміжними блоками, якого жоден окремий блок не міг побачити.
- Ручна (не скриптова) перевірка в браузері — жоден блок цієї хвилі, включно з цим, не
  запускав preview/browser-перевірку кожної сторінки в обох locale; уся верифікація —
  build/lint/tsc/docker + статичний key-resolution. Візуальний прохід дашборду в `en`
  (перемикач локалі) перед вважанням хвилі "готовою до демо" — не зроблений жодним блоком.
