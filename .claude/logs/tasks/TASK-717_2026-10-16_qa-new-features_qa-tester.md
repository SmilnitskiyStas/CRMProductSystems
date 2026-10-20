# TASK-717 QA нових фіч (qa-tester, 2026-10-16)

Покрито: order tracking, postpone, customer activity/receipts, no-purchase list, incoming supply, daily-sales sort/paging, AI cap, tsc/eslint, dotnet test.
Результат: критичних/високих багів немає. dotnet test: 2467/2467 pass (+1 новий тест AiOrderCapTests). tsc: чисто. eslint: 0 errors, 2 warnings (orders/page.tsx:44 useMemo deps).

Знахідки (low/medium):
- [low] GET /api/marketing-analytics/no-purchase/customers з неіснуючим storeIds повертає клієнтів (не 400/порожньо); витоку між тенантами немає.
- [low] /api/movements productName завжди null (не з цієї зміни).
- [low] ReceiptDto відповіді PUT postpone не містить postponeCount/originalExpectedAt (видно лише через /orders/tracking).
- [low] orders/page.tsx: allLines = order?.lines ?? [] — нестабільна залежність useMemo (eslint warning).
- [info] Відкладене замовлення лишається status=new (draft), не in_transit.
- [info] Виключення Status=fiscalization_failed перевірено лише по коду (у БД таких рядків нема).
- [info] Дані: у marketplace supplierName у БД літерально '????' (тестові дані).

Додано: backend/ShelfGuard.Tests/AiOrders/AiOrderCapTests.cs.
Побічно змінено тестові дані: receipt be81668f (Пчілка) відкладено двічі (ExpectedAt 2026-10-23, PostponeCount=2).
