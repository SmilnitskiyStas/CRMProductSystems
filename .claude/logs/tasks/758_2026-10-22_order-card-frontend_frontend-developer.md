# TASK-758 (frontend) — order card /orders/{id}
- New page app/(dashboard)/orders/[id]/page.tsx + features/orders/components/OrderCard.tsx + hooks/useOrderCard.ts.
- receipts api/types: updateHeader, addItem, removeItem, send; orderedAt/orderedByName.
- AutoOrdersSection: title/«Відкрити» -> router.push; «Взяти в роботу» = take then push (pending label + error toast).
- i18n uk/en: Dashboard.orders.page.card.*, auto.taking/open; receipts.status.ordered = "Відправлено постачальнику".
- tsc + eslint clean. Not browser-verified (needs TASK-757 backend).
Assumptions: DELETE items returns ReceiptDto or empty; send/header/addItem return ReceiptDto.
