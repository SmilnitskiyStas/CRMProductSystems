# Handoff: TASK-317 (backend-developer) → TASK-318 (frontend-developer)

Бекенд співпраці готовий: угоди + договір PDF + Вчасно + marketplace-замовлення + тікети підтримки.
Build green, 639 тестів пройшло. Міграцію TASK-316 НЕ застосовано локально — застосується стандартним шляхом.

## Статуси

- Agreement: `pending → awaiting_signature (approve) → active (mark-signed) → terminated`; `pending → rejected (reject)`.
  Після rejected/terminated клієнт може подати нову заявку (створюється новий рядок).
- Order: `new → confirmed → shipped → delivered`; скасування: клієнт — тільки з `new`; постачальник — з `new`/`confirmed` (з причиною).
- Ticket: `open | in_progress | resolved | closed` (постачальник міняє довільно).

## Клієнтська сторона — `MarketplaceCooperationController`
Auth: звичайний `[Authorize]` + `[RequireModule("marketplace")]` (як POST відгуку). `{id}` у suppliers/{id}/... — **публічний supplierId** з маркетплейсу (не tenantId).

```
POST /api/marketplace/suppliers/{supplierId}/cooperation-requests
  body: { message?: string }            → 201 CooperationAgreementDto | 400 | 404 | 409 (дублікат: error містить статус живої угоди)
GET  /api/marketplace/cooperation       → 200 CooperationAgreementDto[]   (мої угоди, новіші перші)
GET  /api/marketplace/cooperation/{agreementId}/contract
                                        → 200 application/pdf (файл {contractNumber}.pdf) | 400 «Договір ще не згенеровано.» | 404
POST /api/marketplace/suppliers/{supplierId}/orders
  body: { items: [{ supplierItemId: uuid, qty: number }], comment?: string }
                                        → 201 MarketplaceOrderDto
                                        | 403 { error: "Замовлення доступні лише після укладення договору про співпрацю" }  ← ГЕЙТ
                                        | 400 (валідація: недоступна позиція / qty < minQty / qty > maxQty / порожній кошик) | 404
GET  /api/marketplace/my-orders         → 200 MarketplaceOrderDto[]
POST /api/marketplace/orders/{orderId}/cancel
  body: { reason: string }              → 200 MarketplaceOrderDto | 400 «Скасувати можна лише замовлення у статусі „нове“.» | 404
POST /api/marketplace/suppliers/{supplierId}/support-tickets     (БЕЗ угоди — відкрито всім)
  body: { subject: string, message: string }  → 201 SupplierSupportTicketDto (з messages) | 400 | 404
GET  /api/marketplace/my-support-tickets → 200 SupplierSupportTicketDto[] (messages = null)
GET  /api/marketplace/support-tickets/{ticketId}      → 200 SupplierSupportTicketDto (з messages, старіші перші) | 404
POST /api/marketplace/support-tickets/{ticketId}/messages
  body: { body: string }                → 201 SupportTicketMessageDto | 400 | 404
```

## Кабінет постачальника — `SupplierCabinetCooperationController`
Auth: `[Authorize(Policy = SupplierCabinet)]` + `[RequireModule("marketplace_supplier")]` (як решта кабінету).

```
GET  /api/supplier-cabinet/cooperation-requests?status=pending   → 200 CooperationAgreementDto[]
POST /api/supplier-cabinet/cooperation-requests/{id}/approve     → 200 dto | 400 (якщо реквізити не заповнені:
       «Спочатку заповніть реквізити договору (юридична назва, IBAN, назва послуги/товару) у налаштуваннях кабінету.») | 404
POST /api/supplier-cabinet/cooperation-requests/{id}/reject      body: { reason: string }  → 200 | 400 | 404
POST /api/supplier-cabinet/cooperation-requests/{id}/regenerate-contract  → 200 (лише awaiting_signature)
POST /api/supplier-cabinet/cooperation-requests/{id}/send-to-vchasno      → 200 (записує vchasnoDocumentId)
       | 400 { error: "Інтеграцію Вчасно не налаштовано." } (ключ додається через PUT /api/integrations/vchasno, config { api_key })
POST /api/supplier-cabinet/cooperation-requests/{id}/mark-signed → 200 (awaiting_signature → active)
POST /api/supplier-cabinet/cooperation-requests/{id}/terminate   body: { reason?: string } → 200 (active → terminated)
GET  /api/supplier-cabinet/cooperation-requests/{id}/contract    → 200 application/pdf | 400 | 404

GET  /api/supplier-cabinet/contract-settings   → 200 SupplierContractSettingsDto | 404 (ще не заповнено)
PUT  /api/supplier-cabinet/contract-settings   body: UpsertContractSettingsDto → 200 dto | 400 (legalName обовʼязкове)
POST /api/supplier-cabinet/contract-settings/signature-image   multipart file (png/jpg ≤2MB) → 200 { imageUrl } | 400
POST /api/supplier-cabinet/contract-settings/stamp-image       multipart file (png/jpg ≤2MB) → 200 { imageUrl } | 400
       (upload вимагає, щоб contract-settings уже існували — спершу PUT)

GET  /api/supplier-cabinet/orders                       → 200 MarketplaceOrderDto[]
POST /api/supplier-cabinet/orders/{id}/status           body: { status, reason? } → 200 | 400 (недозволений перехід
       або cancelled без reason) | 404. Дозволено: new→confirmed|cancelled, confirmed→shipped|cancelled, shipped→delivered.

GET  /api/supplier-cabinet/support-tickets              → 200 SupplierSupportTicketDto[] (messages = null)
GET  /api/supplier-cabinet/support-tickets/{id}         → 200 dto з messages | 404
POST /api/supplier-cabinet/support-tickets/{id}/messages  body: { body } → 201 SupportTicketMessageDto | 400 | 404
POST /api/supplier-cabinet/support-tickets/{id}/status    body: { status } → 200 | 400 | 404
```

## DTO shapes (`Dtos/CooperationDtos.cs`, camelCase у JSON)

```ts
CooperationAgreementDto {
  id, supplierTenantId, clientTenantId: uuid
  supplierName, clientName: string
  status: "pending"|"rejected"|"awaiting_signature"|"active"|"terminated"
  requestMessage: string|null
  rejectionReason: string|null      // і причина відмови, і причина розірвання
  contractNumber: string|null       // «ДС-2026-001»
  hasContractFile: boolean
  vchasnoDocumentId: string|null
  requestedAt, decidedAt?, signedAt?, terminatedAt?: ISO8601
}

SupplierContractSettingsDto / UpsertContractSettingsDto {
  legalName: string                 // required
  edrpou?, iban?, bankName?, legalAddress?, directorName?, phone?, email?,
  serviceName?, serviceDescription?: string|null
  isVatPayer: boolean
  // тільки в GET-dto:
  signatureImageUrl?, stampImageUrl?: string|null
  updatedAt: ISO8601
}
// Для approve мінімум: legalName + iban + serviceName.

MarketplaceOrderDto {
  id: uuid, orderNumber: string      // «MP-2026-001»
  agreementId, supplierTenantId, clientTenantId: uuid
  supplierName, clientName: string
  status: "new"|"confirmed"|"shipped"|"delivered"|"cancelled"
  comment, cancelReason: string|null
  totalAmount: number
  createdAt, updatedAt: ISO8601
  items: [{ id: uuid, supplierItemId: uuid|null, itemName: string, unit: string|null,
            price: number, qty: number, lineTotal: number }]   // снапшоти на момент замовлення
}

SupplierSupportTicketDto {
  id, supplierTenantId, clientTenantId: uuid
  supplierName, clientName: string
  subject: string
  status: "open"|"in_progress"|"resolved"|"closed"
  createdAt, updatedAt: ISO8601
  messages: SupportTicketMessageDto[] | null   // null у списках
}
SupportTicketMessageDto { id, ticketId, senderTenantId, senderUserId: uuid, body: string, isRead: boolean, createdAt }
// «моє повідомлення» = senderTenantId === мій tenant_id (як у чаті)
```

## UX-нотатки для TASK-318

- На сторінці постачальника: статус співпраці бери з `GET /api/marketplace/cooperation` (знайти по supplierTenantId — але клієнт знає supplierId; supplierTenantId є в dto угоди, тож зіставляй за назвою/через створення заявки). Кнопка «Замовити» показує 403-помилку гейта, якщо угода не active.
- Вчасно: інтеграція налаштовується існуючою сторінкою інтеграцій (`PUT /api/integrations/vchasno`, config `{ "api_key": "..." }`, маскується на GET як ПРРО).
- Помилки скрізь `{ error: string }` українською — показуй як є.
