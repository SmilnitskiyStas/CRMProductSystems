# API Contracts

**Owner:** backend-developer + frontend-developer
**Updated:** 2026-07-20
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
POST /api/auth/login        [public, rate limit 10/min per IP]
  Body: { email, password }
  200: { accessToken, user: AuthUserDto } + Set-Cookie: refreshToken (HttpOnly)
  200 (2FA увімкнено): { requiresTwoFactor: true, challengeToken } — БЕЗ токенів/cookie, challenge живе 5 хв
  401: { error }   (generic — lockout/inactive не розкриваються)
  429: { error: "Too many requests. Try again later." }

POST /api/auth/2fa/verify   [public, той самий ліміт]     (TASK-330)
  Body: { challengeToken, code }   — code: 6-значний TOTP або recovery-код XXXX-XXXX
  200: { accessToken, user: AuthUserDto } + Set-Cookie (як login)
  401: { error: "Invalid code." } | { error: "Invalid or expired challenge token." }

POST /api/auth/2fa/setup    [Authorize] → 200: { secret, otpauthUri } (pending, до enable 2FA вимкнена)
POST /api/auth/2fa/enable   [Authorize] Body: { code } → 200: { recoveryCodes: string[8] } (показуються один раз)
POST /api/auth/2fa/disable  [Authorize] Body: { password, code } → 204

POST /api/auth/refresh      [public — reads HttpOnly cookie, rate limit 30/min per IP]
  200: { accessToken, user: AuthUserDto } + rotated Set-Cookie
  401: { error }

POST /api/auth/logout       [Authorize]
  Reads: Cookie refreshToken
  204: (no body, cookie cleared)

GET  /api/auth/me           [Authorize]
  200: AuthUserDto
  401

POST /api/auth/change-password [Authorize]
  Body: { currentPassword, newPassword } — політика: 12+ символів, літера+цифра
  204: усі refresh-токени відкликано (інші пристрої розлогінено)
  400: { error } (текст політики англійською, показується as-is)
```

#### AuthUserDto
```json
{
  "id": "uuid", "email": "string", "fullName": "string", "role": "string",
  "tenantId": "uuid|null", "tenantName": "string|null", "storeId": "uuid|null",
  "permissions": { "page-slug": true } ,
  "legalEntityId": "uuid|null",
  "twoFactorEnabled": false,
  "capabilities": ["string", "..."] ,
  "telegramChatId": "string|null",
  "preferredLocale": "string|null",
  "tabs": ["string", "..."]
}
```
`capabilities` (ADR-020) and `tabs` (ADR-021, TASK-391b) are independent axes resolved from the
same `TenantRole` (via `TenantRoleId`) — both `null`/absent when the user has no `TenantRoleId`
or its template is archived. Both are UI-mirrors of the equally-named JWT claims below; real
enforcement is server-side (`RoleOrCapabilityHandler` for capabilities — nothing enforces `tabs`
server-side yet, see ADR-021 Tier 1/Tier 2).

#### JWT Claims
```
sub          — userId (Guid)
email        — user email
role         — role string
tenant_id    — tenantId (absent for provider users)
store_id     — storeId (absent if not assigned)
capabilities — comma-joined TenantRole capabilities (ADR-020; absent if empty)
tabs         — comma-joined TenantRole AllowedTabs (ADR-021, TASK-391b; absent if empty)
```

---

## ✅ Tenant Roles (ADR-020, extended ADR-021 — `/api/tenant-roles`)

Custom capability-template roles, additive on top of a user's base `Role`. Every action is
`[Authorize(Policy = AtLeastEnterpriseAdmin)]`, **no** capability-bypass (anti-escalation — a
`users.manage` capability holder must never be able to create/edit a template or grant more
access than the template intends).

```
GET    /api/tenant-roles/capabilities            -> TenantRoleCapabilityGroupDto[]  (catalog, not tenant-scoped, grouped by specialty)
GET    /api/tenant-roles/tabs                    -> TenantRoleTabDto[]              (catalog, not tenant-scoped, flat list — ADR-021, TASK-391b)
GET    /api/tenant-roles?includeInactive=        -> TenantRoleDto[]
GET    /api/tenant-roles/{id}                    -> TenantRoleDto | 404
POST   /api/tenant-roles       CreateTenantRoleRequest  -> 201 TenantRoleDto | 400 { error }
PUT    /api/tenant-roles/{id}  UpdateTenantRoleRequest  -> TenantRoleDto | 400 { error } | 404
DELETE /api/tenant-roles/{id}                    -> 204 (archives — IsActive=false, never hard-deleted) | 404

POST   /api/users/{id}/tenant-role  AssignTenantRoleRequest { "tenantRoleId": "uuid|null" }  -> 204 | 400 | 404
```

#### TenantRoleDto
```json
{
  "id": "uuid", "name": "string",
  "capabilities": ["users.manage", "..."],
  "allowedTabs": ["workforce", "analytics", "..."],
  "isActive": true, "createdAt": "ISO8601", "updatedAt": "ISO8601|null"
}
```

#### CreateTenantRoleRequest / UpdateTenantRoleRequest
```json
{ "name": "string", "capabilities": ["string", "..."], "allowedTabs": ["string", "..."] }
```
`allowedTabs` (TASK-391b) — optional on the wire (defaults to `[]` server-side), validated
against the fixed 10-key `TenantRoleTabs.All` catalog (see ADR-021); an unknown key is rejected
with `400`.

#### TenantRoleCapabilityDto / TenantRoleTabDto
```json
{ "key": "string", "labelUa": "string" }
```
`GET .../capabilities` groups these under `TenantRoleCapabilityGroupDto { "specialty": "string",
"capabilities": TenantRoleCapabilityDto[] }`; `GET .../tabs` returns a flat `TenantRoleTabDto[]`
(tabs aren't grouped by specialty).

---

## ✅ Users (`/api/users`)

Policy varies per action: Invite/Update/Deactivate accept `AtLeastStoreManager`-or-above **or**
a `users.manage` TenantRole capability (ADR-020); every other action below is role-rank-gated
only, no capability bypass (anti-escalation — see `UsersController.cs` header comment for the
full rationale, including the TASK-347 RoleRank re-check that applies regardless of which policy
path let the caller in).

```
GET    /api/users                                -> UserDto[]
GET    /api/users/{id}                           -> UserDto | 404
POST   /api/users/invite        InviteUserRequest -> 201 UserDto | 400 { error }
PUT    /api/users/{id}          UpdateUserRequest -> UserDto | 400 { error } | 404
DELETE /api/users/{id}                           -> 204 (soft — IsActive=false) | 404

PUT    /api/users/{id}/permissions        UpdatePermissionsRequest         -> UserDto | 400 | 404
POST   /api/users/{id}/permission-grants  GrantTemporaryPermissionRequest  -> 201 PermissionGrantDto | 400 | 404
GET    /api/users/{id}/permission-grants                                  -> PermissionGrantDto[] | 404
DELETE /api/users/{id}/permission-grants/{grantId}                        -> 204 | 400 | 404
GET    /api/users/{id}/activity?limit=50                                  -> ActivityLogDto[] | 404
POST   /api/users/{id}/tenant-role        AssignTenantRoleRequest         -> 204 | 400 | 404  (see Tenant Roles above)

PUT    /api/users/{id}/locations          UpdateUserLocationsRequest      -> 200 UserLocationsDto | 400 { error } | 404  (TASK-392b, ADR-022)
GET    /api/users/{id}/locations                                         -> 200 UserLocationsDto | 404               (TASK-392b, ADR-022)
```

#### UserDto
```json
{
  "id": "uuid", "email": "string", "fullName": "string", "phone": "string|null",
  "role": "string", "storeId": "uuid|null", "isActive": true, "hasTelegram": false,
  "createdAt": "ISO8601", "lastActiveAt": "ISO8601|null",
  "permissions": { "page-slug": true },
  "invitedByName": "string|null",
  "legalEntityId": "uuid|null",
  "tenantRoleId": "uuid|null",
  "preferredLocale": "string|null"
}
```

#### InviteUserRequest
```json
{ "email": "string", "fullName": "string", "role": "string", "password": "string",
  "storeId": "uuid|null", "legalEntityId": "uuid|null" }
```

#### UpdateUserRequest
```json
{ "fullName": "string", "phone": "string|null", "role": "string",
  "storeId": "uuid|null", "legalEntityId": "uuid|null" }
```

**`storeId` behavior (TASK-392b, ADR-022) — changed this session.** Previously accepted any GUID
with zero validation and was never read anywhere else. Now:
- **Validated**: must belong to the caller's own tenant (`ILocationService.BelongsToTenantAsync`)
  — mismatch returns `400 { "error": "Вказана локація не належить цьому тенанту." }`.
- **Actually consumed**: for single-location roles (`store_manager`, `merchandiser`,
  `storekeeper`, `cashier`, `staff`) it drives a same-transaction write of exactly one
  `user_locations` row (`UserService.SyncSingleLocationAsync`) — this is what will matter once
  ADR-022 Stage 3 ships. For `network_manager` (potentially multi-location) it has no assignment
  effect — use `PUT /api/users/{id}/locations` instead. For `enterprise_admin`/`supplier_admin`
  it's accepted but carries no access-control meaning (unconditional bypass / outside the
  store-scope model entirely).

#### `PUT /api/users/{id}/locations` — full-replace (TASK-392b, ADR-022 Stage 1)
```json
// Request
{ "locationIds": ["uuid", "uuid", "..."] }
// Response 200 — UserLocationsDto
{ "locationIds": ["uuid", "uuid", "..."] }
```
Full-replace semantics — `{"locationIds": []}` clears every assignment for the user. Every id
must belong to the caller's tenant; the first one that doesn't returns `400 { error }` and
**nothing is written** (fails closed, not partial). `AtLeastEnterpriseAdmin`-only, no capability
bypass — same anti-escalation posture as `AssignTenantRole`. **As of Stage 1, nothing reads this
table for access control yet** — ADR-022 Stage 3 (written, held on branch
`stage3-rls-enforcement-hold`, not deployed — see `.claude/docs/store-scope-rollout-checklist.md`)
is what will make these rows actually gate visibility on `product_stock`/`daily_sales`/
`pos_shifts`/etc.

#### `GET /api/users/{id}/locations`
```json
{ "locationIds": ["uuid", "..."] }
```
`404 { "error" }` if the target user doesn't exist or isn't in the caller's tenant.

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

### Products (legacy redirect — resolved, KI-008)
```
GET|POST /api/products[/*]  -> 301 Permanent Redirect to /api/items[/*]
```
`ProductsLegacyController` no longer serves data directly — every verb (`GET`, `POST`,
`PUT`, `DELETE`, `by-barcode`, `/suppliers`) issues `RedirectPermanent` to the
equivalent `/api/items` route, which is the real tenant-aware, authorized, paginated
catalog described above. The old unauthenticated POC `Products` table this section used
to describe is gone from the live routing surface.

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

---

### POS (v3.2 — TASK-068/069, cash reconciliation added TASK-356)

`[Authorize(Policy = CanAccessPos)]` (cashier, storekeeper, store_manager, network_manager,
enterprise_admin) unless noted. Offline-first (ADR-011): a sale's DB commit always succeeds
even if the fiscal provider is unreachable; `fiscalStatus` in the response reflects the
actual outcome. One open shift per **tenant** at a time today (not per store — see `known-issues.md`
KI-015 and `.claude/logs/tasks/356_2026-07-15_pos-fiscalization-audit_backend-developer.md`
§"Per-store shift plan" for why and what a per-store migration would take).
```
POST /api/pos/shifts/open           { storeId, openingCash? }             -> ShiftDto | 409 (already open)
GET  /api/pos/shifts/current                                              -> ShiftDto | 404
POST /api/pos/shifts/close          { actualClosingCash? }                -> ShiftDto | 404 (no open shift) | 400 (actualClosingCash < 0)
POST /api/pos/sales                 CreateSaleRequest                     -> 201 SaleDto | 400 | 409 (shift closed) | 423 (item fully expired)
GET  /api/pos/sales?shiftId=                                              -> SalesListDto
GET  /api/pos/sales/pending-fiscalization   [AtLeastStoreManager, worker service account]  -> PendingFiscalizationDto[]
POST /api/pos/sales/{id}/fiscalize          [AtLeastStoreManager, worker service account]  -> FiscalizeResultDto | 404
```

#### POST /api/pos/shifts/close — cash reconciliation (TASK-356)

Body is **optional** — omit it entirely (or send `{}`/`actualClosingCash: null`) to close
exactly as before: no reconciliation, `closingCash`/`expectedCashAmount`/`cashDiscrepancy`
all come back `null`. This keeps existing clients (mobile app currently sends no body)
working unchanged.

Request:
```json
{ "actualClosingCash": 1234.56 }
```
`actualClosingCash` — cash counted by the cashier at close, in UAH. Must be `>= 0`; a
negative value returns `400 { "error": "ActualClosingCash cannot be negative." }` and the
shift is **not** closed (stays open, nothing persisted). Closing an already-closed shift
(or when no shift is open) returns `404 { "error": "No open shift found." }`.

Response (`ShiftDto`, extended in TASK-356 — new fields at the end, existing consumers
unaffected):
```json
{
  "shiftId": "uuid", "storeId": "uuid",
  "status": "Closed",
  "openedAt": "ISO8601", "closedAt": "ISO8601",
  "providerShiftId": "string|null", "fiscalStatus": "closed|close_failed|local_only",
  "totalSales": 1234.56, "shiftNumber": 1,
  "openingCash": 500.00,
  "closingCash": 1234.56,
  "expectedCashAmount": 1200.00,
  "cashDiscrepancy": 34.56
}
```
- `expectedCashAmount` = `openingCash` (0 if null) + sum of this shift's **cash-only**
  sales (`PosTransaction.PaymentType == "cash"`) — card payments never touch the physical
  drawer, so they're excluded. Computed server-side
  (`IPosRepository.GetCashSalesTotalForShiftAsync`), not sent by the client.
- `cashDiscrepancy` = `closingCash - expectedCashAmount`. **Positive = surplus** (more
  cash than expected), **negative = shortage** (less cash than expected), `0` = exact
  match. `null` when the request didn't include `actualClosingCash`.
- These four fields (`openingCash`/`closingCash`/`expectedCashAmount`/`cashDiscrepancy`)
  are also present (mostly `null` until close) on the `ShiftDto` returned by
  `shifts/open` and `shifts/current` — `openingCash` is populated as soon as the shift is
  opened, the other three only after a reconciled close.

Frontend TODO (not yet built as of TASK-356): a cash-count input on the "close shift" UI
that posts `actualClosingCash`, and a way to surface `cashDiscrepancy` to the
cashier/manager (e.g. a warning banner when non-zero) — `frontend/features/pos` currently
has no UI for this at all.
