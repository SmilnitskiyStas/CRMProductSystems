# TASK-384: Dashboard i18n (uk/en) — Block 6: Marketplace (B2B buyer side)

**Agent:** frontend-developer
**Date:** 2026-07-17
**Status:** done

## Зроблено

Переклав через існуючий `useTranslations`/`DashboardIntlProvider` (Block 1, TASK-376) —
жодного нового provider-wiring. Один новий top-level розділ `Dashboard.marketplace` у
`frontend/messages/{uk,en}.json` (додано після вже наявного `Dashboard.customers`), 25
під-неймспейсів (по одному на компонент/сторінку + 3 спільні: `planLabel`, `reviewCount`,
`{agreementStatus,orderStatus,ticketStatus}`).

**24 файли фічі + 3 сторінки** (усі файли з Cyrillic; `types.ts`/`api/marketplace-api.ts`/
`hooks/{useMarketplace,useCooperation}.ts` — лише коментарі розробника, 0 user-facing
рядків, підтверджено grep, не займав; `StarRating.tsx` — без тексту, не займав):

- **Спільні enum-неймспейси**: `planLabel.{all,free,premium}` (SupplierFilters,
  PlanBadge, SupplierProfileForm — "★ Premium"/"Free" лишив як є, це вже усталені
  англомовні назви тарифів, ідентичні в обох locale-файлах, за аналогією з UAH-кодом
  валюти). `reviewCount` — ICU plural (one/few/many/other) замінив кастомну
  `reviewWord()` з `utils.ts` (видалив функцію, залишив коментар-посилання на нове
  місце) — вжито в SupplierCard/SupplierReviewsTab/`[id]/page.tsx`. `resultsFound` у
  marketplace/page.tsx — той самий підхід, повний one/few/many/other для
  "постачальник/постачальники/постачальників" (свідоме покращення відносно
  спрощеного one/other precedent з Block 2a — коректна українська форма, не
  регресує існуючу поведінку `reviewWord`).
- **`CooperationBadges.tsx`** (найскладніший випадок): `AgreementStatusBadge`/
  `OrderStatusBadge`/`TicketStatusBadge` тепер резолвлять лейбл через `useTranslations`
  всередині компонента. Експортовані `AGREEMENT_STATUS_LABELS`/`ORDER_STATUS_LABELS`/
  `TICKET_STATUS_LABELS` — **свідомо залишені як є** (україномовні, без змін): їх напряму
  імпортує `features/supplier-cabinet/components/CabinetSupportTab.tsx` (Block 7, ще не
  перекладений) для списку статусів у `<select>`, видалення експорту зламало б той файл
  без його редагування.
- **`ItemCategoryFields.tsx`/`SupplierItemExtraFields.tsx`**: `findMissingRequiredField()`
  і `parseExtraFields()` — чисті функції (не компоненти), що використовуються і з
  marketplace (`AddSupplierItemModal.tsx`, в скоупі) і з
  `features/supplier-cabinet/components/CabinetItemModal.tsx` (Block 7, не займав).
  Обом додав **опціональний** параметр `t?` — якщо переданий, повертає перекладене
  повідомлення; якщо ні (виклик з CabinetItemModal.tsx лишився без змін), повертає
  ідентичний оригінальному україномовний рядок. Нуль поведінкових змін для Block 7,
  нуль зламаних імпортів.
- **`SupplierItemDetailDialog.tsx`/`SupplierItemsTab.tsx`**: `field.labelUa`/
  `categoryDef.labelUa` — дані з бекенд DTO (ADR-017 §4, категорійні лейбли), НЕ
  чіпав — лишаються україномовними незалежно від locale (аналогічно backend
  422-помилкам з Block 5, окремий Block 11 rollout-плану).
- **Форми з валідацією**: `SupplierProfileForm.tsx`, `AddSupplierItemModal.tsx`,
  `ReviewModal.tsx`, `CooperationRequestModal.tsx`, `SigningMethodChoice.tsx` (composite
  `window.confirm()`-рядок зібраний з 3 окремих ключів, як і в оригіналі).
- **Сторінки**: `marketplace/page.tsx` (module-gate за зразком `Dashboard.autoService.
  moduleGate`), `marketplace/[id]/page.tsx` (найбільша, `supplierPage` неймспейс),
  `marketplace/orders/page.tsx` (`ordersPage.{ordersTab,cooperationTab}`, `FragmentRow`/
  `CooperationTab` — окремі `useTranslations` виклики в межах свого компонента).

**Locale-aware formatting:** усі `toLocaleString/toLocaleDateString/toLocaleTimeString
("uk-UA", ...)` → `intlLocale` (`locale === "en" ? "en-US" : "uk-UA"` через `useLocale()`)
у SupplierItemsTab/SupplierOrderCart/SupplierChatPanel/SupportTicketsPanel/
SupplierReviewsTab/orders-page (`money()`/`formatDate()` module-level helpers отримали
`locale` параметром замість хардкоду). Currency (`style: "currency", currency: "UAH"`)
лишився UAH у всіх locale.

## Верифікація

- `npm run build` — exit 0, усі 52 сторінки згенеровано, включно з `/marketplace`,
  `/marketplace/[id]`, `/marketplace/orders`. `ENVIRONMENT_FALLBACK`-шум — той самий
  pre-existing діагностичний код, підтверджений у Block 2a-5.
- `npm run lint` — exit 0, "No ESLint warnings or errors".
- `npx tsc --noEmit` — exit 0 (додатковий safety-net, бо змінював кілька сигнатур:
  `parseExtraFields`, `findMissingRequiredField`, нові пропси `StringListEditor`/
  `FragmentRow`/`AddToCartCell`).
- `docker build -f frontend/Dockerfile frontend` (з кореня репо, синхронно, exit code
  перевірено напряму) — exit 0, той самий route table всередині контейнера.
- Key-resolution скрипт (scratchpad, position-aware: прив'язує кожен `X("key")`/
  `X.has("key")` до найближчого попереднього `const X = useTranslations(ns)` за
  позицією рядка у файлі) — **256 статичних викликів з 27 файлів, 0 пропущених ключів**
  в обох `messages/{uk,en}.json`. 7 динамічних викликів (`t(status)`, `tPlan(plan)`,
  `t(labelKey)`) звірені вручну проти TS union-типів (`SupplierPlan`, `CooperationStatus`,
  `MarketplaceOrderStatus`, `SupportTicketStatus`, та внутрішній labelKey-union у
  `parseExtraFields`) — усі значення присутні ідентично в обох namespace.

## Файли

`frontend/features/marketplace/components/{StarRating,PlanBadge,SupplierFilters,
SupplierProfileForm,SupplierCard,SupplierMetrics,ReviewModal,ItemCategoryFields,
SupplierItemExtraFields,SupplierItemDetailDialog,AddSupplierItemModal,
SupplierReviewsTab,CooperationBadges,SupplierOrderCart,SupplierItemsTab,
SupplierChatPanel,SupportTicketsPanel,SigningMethodChoice,
CooperationRequestModal}.tsx`, `frontend/features/marketplace/utils.ts` (reviewWord
видалено), `frontend/app/(dashboard)/marketplace/{page,[id]/page,orders/page}.tsx`,
`frontend/messages/{uk,en}.json` (новий `Dashboard.marketplace.*`, 25 під-неймспейсів).

## Не в скоупі (свідомо)

- Усе перекладене в Block 1-5.
- `features/supplier-cabinet/*` (Block 7) — не редагував жодного файлу; сумісність із
  трьома спільними файлами (`CooperationBadges.tsx`, `ItemCategoryFields.tsx`,
  `SupplierItemExtraFields.tsx`) забезпечена через незмінні експорти
  (`*_STATUS_LABELS`) і опціональні `t?`-параметри з ідентичним fallback-текстом.
- `types.ts`/`api/marketplace-api.ts`/`hooks/*.ts` — коментарі розробника лишились
  україномовними (не user-facing).
- Backend-driven `labelUa` category-field labels (ADR-017 §4) — окрема задача
  (Block 11 rollout-плану, аналогічно auto-service/production 422-помилкам з Block 5).
- Git commit/push — за інструкцією, користувач сам комітить і стежить за CI/деплоєм.
