# TASK-383: Dashboard i18n (uk/en) — Block 5: Auto-Service, Production, Customers

**Agent:** frontend-developer
**Date:** 2026-07-17
**Status:** done

## Зроблено

Переклав через існуючий `useTranslations`/`DashboardIntlProvider` (Block 1, TASK-376) —
жодного нового provider-wiring. Три нові top-level секції в `frontend/messages/{uk,en}.json`
під `Dashboard`: `autoService`, `production`, `customers` (додані після вже наявного
`Dashboard.dashboard`, той самий послідовний порядок блоків).

**17 TSX-компонентів + 8 сторінок + 2 JSON** (`types.ts`/`api/*.ts`/`hooks/*.ts` в усіх трьох
фічах — 0 кириличних рядків, підтверджено grep, чіпати не було чого):

- `features/auto-service/components/*` (9 файлів): `WorkOrderCard.tsx` — модульний конст
  `STATUS_LABELS` видалений, статус резолвиться локально в кожному компоненті через
  `Dashboard.autoService.workOrderStatus` (`t(status)`/`tStatus(nextStatus)`), той самий
  split-паттерн, що вже використовувався для `STATUS_LABEL`-style enum в Block 3/4.
  `WorkOrderKanban.tsx`, `WorkOrderDetail.tsx` (найбільший файл фічі, включно з таблицею
  рядків наряду) — оновлені imports (видалено мертвий `STATUS_LABELS` import з
  `WorkOrderDetail.tsx`, він там не використовувався). `ServiceCatalogTable.tsx` —
  дві форми (`ServiceItemForm`, `DeactivateButton`) + головна таблиця, кожна зі своїм `t`.
  `CustomerForm.tsx`/`VehicleForm.tsx`/`CustomerTable.tsx`/`CreateWorkOrderModal.tsx`/
  `WorkOrderLineForm.tsx` — форми з валідацією; у двох файлах довелось перейменувати
  локальні loop-змінні `t`/`opt`, що конфліктували з іменем перекладацького хука
  (`(t) => ...` у мапі типів рядка, `(t) => ...` у мапі тегів клієнта).
- `features/production/components/*` (5 файлів): `RecipeForm.tsx` (найважчий файл, форма +
  динамічний список інгредієнтів). `ProductionOrderTable.tsx` — `STATUS_OPTIONS` (для
  фільтра) перенесений з module-level в тіло компонента (потребує `t()`); `OrderStatusBadge`
  — окремий, іменово інший неймспейс `orderStatusBadge` (лейбли бейджа: "Заплановано/Готово/
  Скасовано" — інша граматична форма, ніж лейбли фільтра "Заплановані/Завершені/Скасовані",
  тому два різних набори ключів, а не один спільний). `ProductionOrderDetail.tsx` —
  регулярний вираз, що парсить структуровану 422-помилку бекенда
  (`/Недостатньо (.+?):\s*потрібно ([\d.]+),\s*є ([\d.]+)/i`), свідомо НЕ займав: бекенд
  досі повертає україномовний текст (Block 11 rollout-плану — окремий, ще не зроблений
  backend i18n), тому парсер має лишитись синхронним з форматом відповіді; переклав лише
  навколишній UI-текст і error-fallback рядки.
- `features/customers/components/*` (3 файли): `CustomerDetail.tsx` — `PaymentTypeBadge`
  (cash/card/online) і транзакційний `StatusBadge` (completed/pending/cancelled/refunded) —
  локальні неймспейси `Dashboard.customers.{paymentType,transactionStatus}`, НЕ
  перевикористання вже наявного `Dashboard.pos.paymentType` (той — лише Cash/Card,
  PascalCase-ключі, інший енум для іншого домену, збіг лише випадковий). Модульний
  `UAH = new Intl.NumberFormat("uk-UA", ...)` в обох файлах (`CustomerDetail.tsx`,
  `CustomerTable.tsx`) видалений — замінений на локальний `uah` всередині кожного
  компонента з `intlLocale` замість хардкодженого `"uk-UA"`.
- Сторінки: `app/(dashboard)/auto-service/{page,customers/page,service-catalog/page,
  work-orders/[id]/page}.tsx`, `production/{recipes/page,orders/page,orders/[id]/page}.tsx`,
  `customers/page.tsx` — усі 7 "модуль не активний" gate-сторінок (4 auto-service + 3
  production) використовують спільний `Dashboard.{autoService,production}.moduleGate.
  {title,body}` в межах своєї фічі (той самий текст повторювався 3-4 рази дослівно в
  межах фічі — не дублюю, `work-orders/[id]` і `production/orders/[id]` рендерять лише
  `.title`, без `.body`, як і в оригіналі).

**Locale-aware formatting:** усі `toLocaleString/toLocaleDateString("uk-UA", ...)` →
`intlLocale` (`locale === "en" ? "en-US" : "uk-UA"` через `useLocale()`) — по кілька місць
у `WorkOrderCard`/`WorkOrderDetail`/`WorkOrderLineForm`/`CustomerTable` (auto-service),
`ProductionOrderTable`/`ProductionOrderDetail`, `CustomerDetail`/`CustomerTable` (customers).
Currency-формат (`style: "currency", currency: "UAH"`) лишився UAH у всіх локалях (бізнес
працює в Україні) — змінювався лише параметр locale, не формат виводу.

## Верифікація

- `npm run build` — exit 0 (перевірено окремим синхронним викликом, код завершення
  прочитано напряму). Усі 52 сторінки згенеровано, включно з `/auto-service`,
  `/auto-service/customers`, `/auto-service/service-catalog`,
  `/auto-service/work-orders/[id]`, `/production/recipes`, `/production/orders`,
  `/production/orders/[id]`, `/customers`. `ENVIRONMENT_FALLBACK`-шум у логах —
  той самий pre-existing діагностичний код при static generation `[locale]`-сторінок
  (підтверджено в Block 2a/2b/3/4, не повязаний з цими змінами).
- `npm run lint` — exit 0, "No ESLint warnings or errors".
- `docker build -f frontend/Dockerfile frontend` (з кореня репо, синхронно, exit code
  перевірено напряму) — exit 0. Той самий route table всередині контейнера, що й
  локальний білд.
- Key-resolution скрипт (scratchpad, position-aware парсер: прив'язує кожен
  `t("key")`/`t.has("key")` до найближчого попереднього `const x = useTranslations(ns)`
  у файлі за позицією рядка) — 320 літеральних викликів з 25 файлів (17 компонентів +
  8 сторінок), усі резолвляться в обох `messages/{uk,en}.json`. Скрипт спершу підняв
  3 false positive в `features/customers/components/CustomerForm.tsx` (модульна функція
  `validate()` отримує `t` як параметр функції, а не через локальний `useTranslations` —
  скрипт не бачить проброс через аргумент); усі 3 ключі (`Dashboard.customers.form.
  {errorNameRequired,errorEmailInvalid,errorPhoneInvalid}`) підтверджені вручну прямим
  `require()`-lookup в обох файлах — присутні. Динамічні виклики (`t(status)`/
  `tStatus(status)`/`tStatus(nextStatus)` для `Dashboard.autoService.workOrderStatus` —
  5 ключів; `t(status)` для `Dashboard.production.orderStatusBadge` — 4 ключі; `t(type)`/
  `t(status)` для `Dashboard.customers.{paymentType,transactionStatus}` — 3+4 ключі)
  звірені вручну проти TS union-типів (`WorkOrderStatus`, `ProductionOrderStatus`) —
  усі присутні ідентично в обох файлах.

## Файли

`frontend/features/auto-service/components/{WorkOrderCard,WorkOrderKanban,WorkOrderDetail,
WorkOrderLineForm,CreateWorkOrderModal,CustomerForm,CustomerTable,VehicleForm,
ServiceCatalogTable}.tsx`, `frontend/features/production/components/{RecipeForm,RecipeTable,
ProductionOrderForm,ProductionOrderTable,ProductionOrderDetail}.tsx`,
`frontend/features/customers/components/{CustomerDetail,CustomerForm,CustomerTable}.tsx`,
`frontend/app/(dashboard)/auto-service/{page,customers/page,service-catalog/page,
work-orders/[id]/page}.tsx`, `frontend/app/(dashboard)/production/{recipes/page,orders/page,
orders/[id]/page}.tsx`, `frontend/app/(dashboard)/customers/page.tsx`,
`frontend/messages/{uk,en}.json` (нові top-level `Dashboard.autoService.{moduleGate,
workOrderStatus,kanban,card,createModal,customerForm,customerTable,vehicleForm,serviceForm,
serviceCatalog,detail,lineForm,workOrdersPage}`, `Dashboard.production.{moduleGate,
recipesPage,recipeTable,recipeForm,orderForm,orderTable,orderStatusBadge,orderDetail}`,
`Dashboard.customers.{page,table,form,detail,paymentType,transactionStatus}`).

## Не в скоупі (свідомо)

- Усе перекладене в Block 1-4.
- Решта фіча-модулів (Block 6+), лендінг (Block 0).
- Backend 422-помилка виробництва (`Недостатньо {item}: потрібно {x}, є {y}`) — досі
  україномовна, парситься regex-ом на фронтенді як є; backend i18n — Block 11
  рollout-плану, окрема задача.
- Git commit/push — за інструкцією, користувач сам комітить і стежить за CI/деплоєм.
