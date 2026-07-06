# API Contracts

**Owner:** backend-developer + frontend-developer
**Updated:** 2026-06-04
**Base URL:** http://localhost:5000/api (dev)

## Auth Headers
```
Authorization: Bearer {jwt_access_token}
```
Tenant context is derived from JWT payload — never from request body.

## Standard Response Shapes

### Success (200/201)
Returns typed DTO directly in body.

### Error
```json
{ "error": "Human-readable message" }
```

### Pagination
All LIST endpoints return a `PagedResult<T>` envelope. Default: `page=1`, `pageSize=50`. Max `pageSize=200` (clamped silently).
```json
{ "items": [...], "totalCount": N, "page": N, "pageSize": N, "totalPages": N }
```
Query params: `?page=1&pageSize=50`

Paginated endpoints:
- `GET /api/stock` — `PagedResult<ProductStockDto>`
- `GET /api/receipts` — `PagedResult<ReceiptDto>`
- `GET /api/write-offs` — `PagedResult<WriteOffDto>`
- `GET /api/transfers` — `PagedResult<TransferDto>`
- `GET /api/items` — `PagedResult<ItemDto>`
- `GET /api/suppliers` — `PagedResult<SupplierDto>`

---

## ✅ Implemented Endpoints

### Auth
```
POST /api/auth/login        [public]
  Body: { email, password }
  200: { accessToken, user: AuthUserDto } + Set-Cookie: refreshToken (HttpOnly)
  401: { error }

POST /api/auth/refresh      [public — reads HttpOnly cookie]
  200: { accessToken, user: AuthUserDto } + rotated Set-Cookie
  401: { error }

POST /api/auth/logout       [Authorize]
  Reads: Cookie refreshToken
  204: (no body, cookie cleared)

GET  /api/auth/me           [Authorize]
  200: AuthUserDto
  401
```

#### AuthUserDto
```json
{ "id": "uuid", "email": "string", "fullName": "string", "role": "string", "tenantId": "uuid|null", "storeId": "uuid|null" }
```

#### JWT Claims
```
sub          — userId (Guid)
email        — user email
role         — role string
tenant_id    — tenantId (absent for provider users)
store_id     — storeId (absent if not assigned)
```

---

## ✅ Catalog (v1 — tenant-aware, `/api/products`)

```
GET  /api/products              [CanViewStock]  → CatalogProductDto[]
  Query: ?category_id, ?segment_id, ?management_type

GET  /api/products/{id}         [CanViewStock]  → CatalogProductDto | 404
GET  /api/products/by-barcode/{code}  [CanViewStock]  → CatalogProductDto | 404

POST /api/products              [AtLeastStoreManager]
  Body: CreateProductRequest    → 201 CatalogProductDto | 400 { error }

PUT  /api/products/{id}         [AtLeastStoreManager]
  Body: UpdateProductRequest    → CatalogProductDto | 400 | 404

DEL  /api/products/{id}         [AtLeastStoreManager]  → 204 | 404  (soft delete)

GET  /api/products/{id}/suppliers  [CanViewStock]  → ProductSupplierSettingDto[]
POST /api/products/{id}/suppliers  [AtLeastStoreManager]
  Body: AddProductSupplierRequest  → 201 ProductSupplierSettingDto | 400 | 404
```

#### CatalogProductDto
```json
{
  "id": "uuid", "barcode": "string|null", "name": "string",
  "categoryId": "uuid|null", "categoryName": "string|null",
  "segmentId": "uuid|null", "segmentName": "string|null",
  "unit": "шт", "managementType": "MTS|MTO|NA|NM",
  "minStock": 0, "maxStock": 0, "safetyBuffer": 0,
  "storageTempMin": null, "storageTempMax": null, "shelfLifeDays": null,
  "defaultSupplierId": "uuid|null", "defaultSupplierName": "string|null",
  "vatRate": 20, "pricePurchase": null, "priceRetail": null,
  "imageUrl": null, "isActive": true, "createdAt": "ISO8601"
}
```

#### ProductSupplierSettingDto
```json
{
  "id": "uuid", "supplierId": "uuid", "supplierName": "string",
  "moq": 1, "usq": 1, "pricePurchase": null,
  "deliveryDays": 3, "isPrimary": false, "isActive": true
}
```

---

### Products (POC catalog — no auth required currently ⚠️)
```
GET  /api/products          -> ProductDto[]
GET  /api/products/{id}     -> ProductDto | 404
POST /api/products          -> 201 ProductDto | 409 { error }
PUT  /api/products/{id}     -> ProductDto | 404
DEL  /api/products/{id}     -> 204 | 404
```

#### ProductDto
```json
{
  "id": "uuid", "sku": "string", "name": "string", "description": "string|null",
  "category": "string", "unit": "string", "costPrice": 0, "salePrice": 0,
  "stockQuantity": 0, "reorderLevel": 0, "isActive": true,
  "createdAt": "ISO8601", "updatedAt": "ISO8601"
}
```
> ⚠️ This is the POC products endpoint backed by the legacy `Products` table (no tenant_id). Will be superseded by `/api/catalog` once TASK-003b is implemented.

---

## 🕐 Pending Endpoints (v1 backlog)

| Endpoint | Task | Blocks |
|---|---|---|
| GET /api/stock | TASK-011 | Dashboard real stats, /stock page |
| POST /api/stock | TASK-011 | Batch management |
| GET /api/stores | TASK-011 | Store map, zone data |
| GET /api/stores/:id/zones | TASK-011 | Store map component |
| GET /api/receipts | TASK-013 | Receipts page |
| POST /api/receipts | TASK-013 | Receiving goods |
| GET /api/transfers | TASK-014 | Transfers page |
| POST /api/transfers | TASK-014 | Store-to-store movement |
| GET /api/write-offs | TASK-015 | Write-offs page |
| POST /api/write-offs | TASK-015 | Write-off document |
| GET /api/analytics/expiry-summary | TASK-019 | Real dashboard stats (also: write-offs, movements, by-zone, by-category, losses) |
| GET /api/notifications/settings | TASK-017 | Notification settings |
| GET /api/users | future | User management |
| POST /api/catalog | TASK-003b | Replace POC products API |

---

### IoT (v3.1 — TASK-063)
```
GET    /api/iot/devices?store_id=          [CanViewStock]        -> IotDeviceDto[]
GET    /api/iot/devices/{id}               [CanViewStock]        -> IotDeviceDto | 404
POST   /api/iot/devices                    [AtLeastStoreManager] -> 201 IotDeviceDto | 400 { error }
PUT    /api/iot/devices/{id}               [AtLeastStoreManager] -> IotDeviceDto | 400 | 404
DELETE /api/iot/devices/{id}               [AtLeastStoreManager] -> 204 (soft, IsActive=false) | 404
GET    /api/iot/devices/{id}/readings?hours=24&limit=500         -> TemperatureReadingDto[] | 404
GET    /api/iot/temperature?store_id=      [CanViewStock]        -> LatestTemperatureDto[]
```

#### IotDeviceDto
```json
{
  "id": "uuid", "storeId": "uuid", "storeName": "string|null",
  "zoneId": "uuid|null", "zoneName": "string|null",
  "deviceType": "weight_sensor|camera|temp_sensor|barcode_reader",
  "deviceId": "shelf-A1-3", "name": "string|null", "mqttTopic": "string|null",
  "config": "jsonb string|null", "isActive": true,
  "isOnline": true, "lastSeenAt": "ISO8601|null",
  "batteryLevel": 80, "firmwareVersion": "string|null", "createdAt": "ISO8601"
}
```
isOnline = lastSeenAt within 30 minutes.

---

### Supplier Cooperation + Marketplace Orders + Supplier Support (v4.3 — TASK-317)

Client side (`[Authorize]` + module `marketplace`; `{supplierId}` = public marketplace supplier id):
```
POST /api/marketplace/suppliers/{supplierId}/cooperation-requests  { message? }        -> 201 CooperationAgreementDto | 409 (live agreement exists) | 404 | 400
GET  /api/marketplace/cooperation                                                      -> CooperationAgreementDto[]
GET  /api/marketplace/cooperation/{id}/contract                                        -> application/pdf | 400 | 404
POST /api/marketplace/suppliers/{supplierId}/orders  { items:[{supplierItemId,qty}], comment? }
                                                                                       -> 201 MarketplaceOrderDto | 403 (no ACTIVE agreement) | 400 | 404
GET  /api/marketplace/my-orders                                                        -> MarketplaceOrderDto[]
POST /api/marketplace/orders/{id}/cancel  { reason }                                   -> MarketplaceOrderDto | 400 (only status=new) | 404
POST /api/marketplace/suppliers/{supplierId}/support-tickets  { subject, message }     -> 201 SupplierSupportTicketDto (no agreement required)
GET  /api/marketplace/my-support-tickets                                               -> SupplierSupportTicketDto[]
GET  /api/marketplace/support-tickets/{id}                                             -> SupplierSupportTicketDto (with messages) | 404
POST /api/marketplace/support-tickets/{id}/messages  { body }                          -> 201 SupportTicketMessageDto | 400 | 404
```

Supplier cabinet (`SupplierCabinet` policy + module `marketplace_supplier`):
```
GET  /api/supplier-cabinet/cooperation-requests?status=            -> CooperationAgreementDto[]
POST /api/supplier-cabinet/cooperation-requests/{id}/approve       -> dto | 400 (contract settings incomplete) | 404
POST /api/supplier-cabinet/cooperation-requests/{id}/reject        { reason } -> dto
POST /api/supplier-cabinet/cooperation-requests/{id}/regenerate-contract      -> dto (awaiting_signature only)
POST /api/supplier-cabinet/cooperation-requests/{id}/send-to-vchasno          -> dto | 400 «Інтеграцію Вчасно не налаштовано.»
POST /api/supplier-cabinet/cooperation-requests/{id}/mark-signed   -> dto (awaiting_signature -> active)
POST /api/supplier-cabinet/cooperation-requests/{id}/terminate     { reason? } -> dto (active -> terminated)
GET  /api/supplier-cabinet/cooperation-requests/{id}/contract      -> application/pdf
GET|PUT /api/supplier-cabinet/contract-settings                    -> SupplierContractSettingsDto (PUT body: UpsertContractSettingsDto, legalName required)
POST /api/supplier-cabinet/contract-settings/signature-image|stamp-image  multipart file png/jpg <=2MB -> { imageUrl }
GET  /api/supplier-cabinet/orders                                  -> MarketplaceOrderDto[]
POST /api/supplier-cabinet/orders/{id}/status  { status, reason? } -> dto; new->confirmed|cancelled, confirmed->shipped|cancelled, shipped->delivered; cancel requires reason
GET  /api/supplier-cabinet/support-tickets                         -> SupplierSupportTicketDto[]
GET  /api/supplier-cabinet/support-tickets/{id}                    -> dto with messages
POST /api/supplier-cabinet/support-tickets/{id}/messages  { body } -> 201 SupportTicketMessageDto
POST /api/supplier-cabinet/support-tickets/{id}/status  { status } -> dto (open|in_progress|resolved|closed)
```

Key DTOs (`Features/Marketplace/Dtos/CooperationDtos.cs`): full shapes in
`.claude/logs/handoffs/317-to-318_frontend-developer.md`. Contract numbers «ДС-{yyyy}-{NNN}»,
order numbers «MP-{yyyy}-{NNN}» — sequential per supplier. Termination reason is stored in
`rejectionReason`. Вчасно integration: `PUT /api/integrations/vchasno` with config `{ "api_key" }`
(masked on GET like ПРРО secrets).
