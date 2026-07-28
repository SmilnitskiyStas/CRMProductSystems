# API Contracts

**Owner:** backend-developer + frontend-developer
**Updated:** 2026-07-27
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
GET    /api/tenant-roles/tabs                    -> TenantRoleTabGroupDto[]         (catalog, not tenant-scoped, hierarchy — ADR-021, TASK-398; flat list originally, TASK-391b)
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
`allowedTabs` — optional on the wire (defaults to `[]` server-side), validated against
`TenantRoleTabs.All` (see ADR-021 + its TASK-398 addendum); an unknown key is rejected with `400`.
`All` unions two key flavours, both currently valid on the same list with no wire-level
distinction between them:
- **Group-level** (10 keys, TASK-391b — `dashboard`, `operations`, `sales`, `procurement`,
  `marketplace`, `auto_service`, `production`, `analytics`, `workforce`, `support`): grants a
  whole `Sidebar.tsx` NavGroup at once.
- **Item-level** (27 keys, TASK-398 — the literal `NavItem.href` per page, e.g. `"/inventory"`,
  `"/receipts"`, `"/pos"`): grants a single page inside a group without unlocking the rest.

#### TenantRoleCapabilityDto / TenantRoleTabDto / TenantRoleTabGroupDto
```json
{ "key": "string", "labelUa": "string" }
```
`GET .../capabilities` groups these under `TenantRoleCapabilityGroupDto { "specialty": "string",
"capabilities": TenantRoleCapabilityDto[] }`.

`GET .../tabs` (TASK-398 — supersedes TASK-391b's flat `TenantRoleTabDto[]`) returns a hierarchy:
```json
[
  { "groupKey": null, "groupLabelUa": "Дашборд", "items": [ { "key": "dashboard", "labelUa": "Дашборд" } ] },
  { "groupKey": "operations", "groupLabelUa": "Операції", "items": [
    { "key": "/inventory", "labelUa": "Каталог" },
    { "key": "/stock", "labelUa": "Залишки" }
  ] }
]
```
`groupKey` is null only for the standalone Dashboard section (Dashboard is a top-level NavItem in
Sidebar.tsx, not a NavGroup). When non-null it is itself one of the 10 group-level keys above —
independently valid in `allowedTabs` — in addition to each nested `items[].key` (item-level).

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

---

### Consumer Auth (Loyalty Фаза 0 — TASK-405)

Wholly separate identity from staff `/api/auth` — a `ConsumerAccount`, never a `User` row. Same
rate-limit policy as staff login (`"auth-login"`, 10/min per IP, TASK-329).
```
POST /api/consumer-auth/register   [AllowAnonymous, rate limit "auth-login"]
  Body: { phone, password, fullName, email? }
  200: ConsumerAuthResponse
  400: { error }              -- validation
  409: { error }              -- phone already registered

POST /api/consumer-auth/login      [AllowAnonymous, rate limit "auth-login"]
  Body: { phone, password }
  200: ConsumerAuthResponse
  401: { error }               -- generic (lockout/wrong password not distinguished)
```
`phone` is normalized server-side (`PhoneNormalizer`, `+380XXXXXXXXX`) — any common UA format is
accepted on input. Lockout mirrors TASK-329 exactly (5 failed attempts → 15 min,
`ConsumerAccount.FailedLoginAttempts`/`LockoutUntil`).

#### ConsumerAuthResponse
```json
{ "accessToken": "string", "consumerAccountId": "uuid", "fullName": "string", "phone": "string" }
```
No refresh-token flow — the consumer access token is long-lived (30 days,
`Jwt:ConsumerAccessTokenDays`) with **no revocation mechanism yet** (flagged by security-reviewer,
TASK-412 item #4 — accepted for initial rollout, not closed). JWT carries `sub` +
`consumer_account_id` = the ConsumerAccount id, `role="consumer"`, **no** `tenant_id` claim at all
— this is what makes the session cross-tenant. Same audience as the staff access token (not a
separate challenge-token audience), since it must pass the same `[Authorize]` middleware.

---

### Loyalty — consumer wallet (`/api/consumer/loyalty`, Фаза 0 — TASK-405)

`[Authorize]` + a ConsumerAccount session (claim `consumer_account_id`) — a staff JWT never
carries this claim and is rejected by the controller's own claim check (RLS's
`consumer_self_access` policy backs this up at the DB layer regardless). Deliberately **not**
gated by `[RequireModule("loyalty")]` — that filter reads the `tenant_id` claim, which a consumer
session never has; module activation is checked inside `LoyaltyService.JoinAsync` itself instead.
```
POST /api/consumer/loyalty/{tenantId}/join                          -> 200 LoyaltyMembershipSummaryDto | 4xx { error }
GET  /api/consumer/loyalty/memberships                              -> 200 LoyaltyMembershipSummaryDto[]   -- the "wallet", all tenants
GET  /api/consumer/loyalty/{tenantId}/code                          -> 200 LoyaltyCodeDto | 4xx { error }
GET  /api/consumer/loyalty/{tenantId}/history?page=1&pageSize=50    -> 200 LoyaltyLedgerEntryDto[] | 4xx { error }
```
`Join` is idempotent — an existing membership is returned as-is, never an error.

#### LoyaltyMembershipSummaryDto
```json
{ "membershipId": "uuid", "tenantId": "uuid", "tenantName": "string",
  "balance": 0.00, "status": "active|blocked", "joinedAt": "ISO8601" }
```

#### LoyaltyCodeDto — the "live" rotating QR/barcode payload
```json
{ "code": "string", "balance": 0.00, "expiresInSeconds": 30 }
```
`code` is the raw rotating TOTP code returned by the server (the secret itself never leaves the
backend). The actual scannable/typeable string the client renders is
`SGLOY1.{membershipId}.{code}` (version prefix + membership id for O(1) staff-side lookup + the
rotating code) — client-assembled, not returned pre-formatted by this endpoint. Poll this endpoint
only while the wallet screen has focus (recommended ~20-25s, matches `CodeTtlSeconds`).

#### LoyaltyLedgerEntryDto
```json
{ "id": "uuid", "entryType": "accrual|redemption|manual_adjustment|expiry",
  "amount": 0.00, "balanceAfter": 0.00, "note": "string|null", "createdAt": "ISO8601" }
```

---

### Loyalty — staff/POS (`/api/loyalty`, Фаза 0 — TASK-405)

`[RequireModule("loyalty")]` at the controller level (a staff JWT always carries `tenant_id`, so
the standard filter applies cleanly here, unlike the consumer controller above).
```
POST /api/loyalty/resolve-code   [CanAccessPos]
  Body: { code: "SGLOY1.{membershipId}.{code}" }   -- full string, from scan OR manual entry, never just the 6 digits
  200: ResolveLoyaltyCodeResult
  400: { error }    -- malformed payload, unknown membership, blocked status
  409: { error }    -- code already claimed (anti-replay) — "ask the customer to refresh their code"
  429: { error }    -- rate-limited (per membershipId, in-memory, 5/15min shape)

POST /api/loyalty/manual-adjust  [AtLeastStoreManager]
  Body: { membershipId, amount, note? }             -- amount signed; guarded against negative balance
  200: LoyaltyMembershipSummaryDto | 400 { error } | 404
  409: { error }    -- optimistic-concurrency conflict (TASK-414) — safe to retry

GET  /api/loyalty/my-membership   [Authorize]        -- Кейс 2: staff in their OWN employer's program
  200: LoyaltyMembershipSummaryDto | 404

POST /api/loyalty/join-as-staff   [Authorize]        -- Кейс 2: creates/backfills the caller's own membership
  200: LoyaltyMembershipSummaryDto | 400 { error }
```

#### ResolveLoyaltyCodeResult
```json
{ "membershipId": "uuid", "customerId": "uuid|null", "customerName": "string|null",
  "maskedPhone": "string|null", "balance": 0.00 }
```
Does **not** carry the redemption cap (`RedemptionCapPercent`/`MinRedemptionBalance` live in
`LoyaltyProgramSettings`, `enterprise_admin`-only — a cashier session can't read it). Clients
should soft-cap a redemption UI to `min(balance, saleSubtotal)` as a hint and rely on the server's
`400` on `POST /api/pos/sales` for the real cap enforcement (see POS extension below).

---

### Loyalty settings (`/api/settings/loyalty`, Фаза 0 — TASK-405)

`[AtLeastEnterpriseAdmin]`, mirrors `PrroSettingsController`'s upsert shape.
```
GET /api/settings/loyalty   -> 200 LoyaltyProgramSettingsDto
PUT /api/settings/loyalty   Body: UpsertLoyaltyProgramSettingsRequest -> 200 LoyaltyProgramSettingsDto | 400 { error }
```
GET returns proposed defaults (`isEnabled=true`, `accrualRatePercent=3.0`,
`redemptionCapPercent=50.0`, `minRedemptionBalance=0`, `codeTtlSeconds=30`) when the tenant has
never saved a row — the program works "out of the box" the moment the module is activated, no
mandatory Settings visit first.

#### LoyaltyProgramSettingsDto / UpsertLoyaltyProgramSettingsRequest (same shape)
```json
{ "isEnabled": true, "accrualRatePercent": 3.0, "redemptionCapPercent": 50.0,
  "minRedemptionBalance": 0.0, "codeTtlSeconds": 30, "updatedAt": "ISO8601|null" }
```

---

### POS extension — loyalty + customer on sales (Фаза 0 — TASK-405/410)

`POST /api/pos/sales` (`CreateSaleRequest`) gained three optional fields — a sale without them
behaves exactly as before:
```json
{ "customerId": "uuid|null", "loyaltyMembershipId": "uuid|null", "redeemAmount": 0.00 }
```
`redeemAmount` requires `loyaltyMembershipId`. Redemption is checked against balance/cap/
`MinRedemptionBalance` and reduces `TotalAmount` **before** tax; accrual is computed on that same
net amount — both happen inside the sale's one existing commit.

`SaleDto` (both `POST /api/pos/sales` and `GET /api/pos/sales`) gained:
```json
{ "customerId": "uuid|null", "customerName": "string|null",
  "loyaltyAccrued": 0.00, "loyaltyRedeemed": 0.00, "loyaltyBalance": 0.00 }
```
All five `null` when the sale carried no customer/membership. `loyaltyBalance` is the ledger
entry's `BalanceAfter` **at the time of this specific sale** (a historical snapshot), not the
membership's current live balance — it can differ from a later `GET` if the same membership had
further activity afterward. TASK-410 closed a gap where `GetSalesForShiftAsync` (what
`GET /api/pos/sales` actually calls) never populated these fields even though `CreateSaleAsync`
did from TASK-405 onward.

---

### Marketing Analytics / RFM (`/api/marketing-analytics`, Фаза 1 — TASK-406/409)

`[Authorize(Policy = MarketingAnalyticsViewOrCapability)]` (store_manager+ **or** the
`marketing_analytics.view` TenantRole capability, ADR-020 — widened from a role-only floor by the
TASK-414 security fix, see ADR-023) + `[RequireModule("marketing_analytics")]`. All responses
camelCase (default `System.Text.Json` web policy).

#### Shared filter query params (every GET below, plus `/explain`)
```
period?: "3m" | "6m" | "12m" | "all" | anything-else   (default "6m" if no from/to either)
from?: "YYYY-MM-DD"
to?: "YYYY-MM-DD"                 -- from+to together always override period
storeIds?: string[]               -- repeated param: ?storeIds=guid1&storeIds=guid2; omitted/empty = all stores
```

#### `RfmSegmentKey` — string enum on the wire, exact spelling, case-insensitive on the way in
`"Champions" | "Loyal" | "CannotLoseThem" | "AtRisk" | "New" | "PotentialLoyalist" |
"Promising" | "AboutToSleep" | "Hibernating" | "Lost" | "NeedsAttention"`
— numeric order = classification priority (first matching rule wins). "Without purchase" is
**not** a member of this enum (zero-transaction customers never enter R/F/M scoring at all) — it
is a separate card, `RfmOverviewDto.noPurchase`.

```
GET  /api/marketing-analytics/overview                                          -> RfmOverviewDto
GET  /api/marketing-analytics/segments/{key}                                    -> RfmSegmentDetailDto
GET  /api/marketing-analytics/segments/{key}/products/{productName}/affinity    -> RfmAffinityResultDto
GET  /api/marketing-analytics/segments/{key}/products/{productName}/basket      -> RfmBasketResultDto
POST /api/marketing-analytics/segments/{key}/explain                           -> ExplainRfmSegmentResultDto | 503 { error }
POST /api/marketing-analytics/exports/segment                                  -> .xlsx
POST /api/marketing-analytics/exports/product-buyers                           -> .xlsx
POST /api/marketing-analytics/exports/product-pair-buyers                      -> .xlsx
```
`{productName}` must be URL-encoded by the caller (free text — spaces/Cyrillic/punctuation).
`/explain` is `POST` (triggers a real Claude call, costs tokens) but still reads its filter from
the query string like the GETs — no body needed. Every export writes one `ActivityLog` row.

#### `GET /overview` → `RfmOverviewDto`
```ts
{
  periodFrom: string; periodTo: string;                          // "YYYY-MM-DD"
  periodCustomerCount: number; periodRevenue: number;
  registeredCustomerCount: number; everPurchasedCustomerCount: number;
  everPurchasedSharePercent: number;                              // 0-100
  segments: {
    key: RfmSegmentKey; labelUa: string; shortDescriptionUa: string;
    customerCount: number; sharePercentOfPeriodCustomers: number;
    revenue: number; sharePercentOfPeriodRevenue: number;
  }[];                                     // ALWAYS 11 entries, priority order, zero-count included
  noPurchase: { customerCount: number; sharePercentOfRegisteredBase: number };
  filtersHash: string; calculatedAt: string;                      // ISO8601 with offset
}
```
`registeredCustomerCount` is tenant-wide (`Customer` has no store association at all);
`everPurchasedCustomerCount`/`noPurchase` **do** respect the store filter — a store-scoped view
can reasonably show "no purchase at these stores" for a customer with history elsewhere in the
tenant. Segment shares sum to ~100% of period customers/revenue (rounding).

#### `GET /segments/{key}` → `RfmSegmentDetailDto`
```ts
{
  key: RfmSegmentKey; labelUa: string; shortDescriptionUa: string; customerCount: number;
  topProducts: { rank: number; productName: string; coveragePercent: number;
                 uniqueCustomerCount: number; receiptCount: number; barcode: string | null }[];
  behavior: {
    peakDayOfWeekIso: number | null;   // 1=Mon..7=Sun
    peakHour: number | null;           // 0-23, Europe/Kyiv
    averageTicket: number; receiptCount: number; receiptsPerCustomer: number;
    lastVisit: string | null;          // "YYYY-MM-DD"
    averageRecencyDays: number; averageLtv: number; totalLtv: number;   // LTV always all-time
    byDayOfWeek: { dayOfWeekIso: number; sharePercent: number }[];
    byHour: { hour: number; sharePercent: number }[];
    topPeakHours: { hour: number; sharePercent: number }[];             // top 3 by receipts
  };
  recommendation: { triggerUa: string; actionUa: string; offerUa: string; cautionUa: string;
                    productsForPromo: string[] };
  filtersHash: string; calculatedAt: string;
}
```
Empty segment (0 customers) still returns 200 — all numeric fields 0, arrays `[]`, a
fully-populated `recommendation` (templates generate sensible zero-KPI copy) — never 404.

#### `GET .../affinity` → `RfmAffinityResultDto` (optional `?limit=10`, 1-50)
```ts
{ segmentKey: RfmSegmentKey; anchorProductName: string;
  items: { productName: string; lift: number; bothBuyersCount: number;
           shareAmongAnchorBuyersPercent: number; shareAmongSegmentPercent: number;
           barcode: string | null }[];   // sorted by lift desc, may be []
  filtersHash: string; calculatedAt: string; }
```

#### `GET .../basket` → `RfmBasketResultDto` (same `?limit=` — different formula, different numbers)
```ts
{ segmentKey: RfmSegmentKey; anchorProductName: string;
  items: { productName: string; togetherSharePercent: number; bothReceiptsCount: number;
           barcode: string | null }[];
  filtersHash: string; calculatedAt: string; }
```

#### `POST .../explain` → `ExplainRfmSegmentResultDto`
```json
{ "explanationUa": "string", "model": "string", "tokensUsed": 0 }
```
`503 { "error": "..." }` when no Claude key is configured (tenant `integration_configs` nor env).

#### Exports — all 3 `POST`, JSON body, response is the raw `.xlsx` file
```
POST /exports/segment              Body: { key, from, to, storeIds: string[]|null, unmaskPii: boolean }
POST /exports/product-buyers       Body: { key, from, to, storeIds, unmaskPii, productName: string }
POST /exports/product-pair-buyers  Body: { key, from, to, storeIds, unmaskPii, productName, pairedProductName: string }
```
`unmaskPii: true` only actually unmasks if the caller holds `marketing_analytics.export_pii`
(TenantRole capability) or is store_manager+ — otherwise silently masked, never a 403. Phone
masked as `+380 XX *** ** 67`-style; email masked the same way (first char + domain kept) since
TASK-414 — both were previously inconsistent (email was unmasked, fixed by the TASK-412 security
review). Row cap 50 000 with a visible truncation banner row. Every cell is passed through
formula-injection sanitization (`ExcelExportService`, TASK-414 fix) — a leading `=`/`+`/`-`/`@`
in any string value (e.g. a self-registered consumer's `FullName` flowing into a `Customer.Name`
export) is neutralized via Excel's own quote-prefix convention, not just string-escaped.

---

### Price Segments + Frequency/Reactivation (`/api/marketing-analytics/price-segments`, Фаза 2 — TASK-420)

Base route for every path below: `api/marketing-analytics/price-segments`. Same gate as the RFM
dashboard above — `[Authorize(Policy = MarketingAnalyticsViewOrCapability)]` +
`[RequireModule("marketing_analytics")]` at class level, same module key (not a new one). All
responses camelCase. Three independent modes on one controller: **comparison** (default),
**all-time** (no period param — a genuinely separate mode, design doc §10), **frequency** (no
all-time mode — needs two comparable windows by definition).

#### Shared period resolution (comparison + frequency GETs, and their `/explain` siblings)
```
period?: "30" | "60" | "90" | anything-else   (default "30" — competitor's own confirmed default)
from?: "YYYY-MM-DD"
to?: "YYYY-MM-DD"                 -- from+to together always override period
storeIds?: string[]               -- repeated param, omitted/empty = all stores
```
Previous period = same length, immediately preceding.

#### Enums on the wire — strings, exact spelling
`PriceSegmentKey`: `"Tier1".."Tier7"` (1=lowest spend .. 7=open-ended top tier; ₴ range label
computed dynamically per tenant, never hardcoded). `PriceAudienceKey` (comparison mode):
`"RealGrowth" | "PriceGrowth" | "Declining" | "Stable"`. `FrequencyAudienceKey`:
`"Sleeping" | "Declining" | "Growing" | "Other"`.

#### Comparison mode
```
GET  .../overview?period=&from=&to=&storeIds=                            -> PriceSegmentsOverviewDto
GET  .../audiences/{audience}?...&page=&pageSize=&sortBy=&sortDescending= -> PriceAudienceTableDto
POST .../audiences/{audience}/explain?...                                 -> ExplainPriceSegmentResultDto | 503
POST .../exports/audience   Body: ExportPriceAudienceRequest             -> .xlsx
```
`sortBy`: `name|segment|items|check|ltv` — an unrecognized value silently falls back to a hardcoded
default (never a 400; allowlisted in `PriceSegmentSortKeys`, never reaches SQL text).

`PriceSegmentsOverviewDto`:
```ts
{
  periodFrom, periodTo, previousPeriodFrom, previousPeriodTo: string;   // "YYYY-MM-DD"
  analyzedCount: number;             // comparison cohort = bought BOTH windows
  currentPeriodBuyerCount, previousPeriodBuyerCount: number;  // each window's own active-buyer count
  raisedCount, declinedCount, stableCount: number;            // sum to analyzedCount
  priceIndexPercent: number;
  distribution: { segment, rangeLabelUa, currentCount, previousCount }[];  // always 7
  audiences: { audience, labelUa, customerCount, sharePercentOfAnalyzed, averageLtv }[];  // always 4
  filtersHash: string; calculatedAt: string;
}
```
`analyzedCount`/`currentPeriodBuyerCount`/`previousPeriodBuyerCount` are THREE different
denominators — never conflate (the competitor's own UI blurs exactly this, analysis doc §6.2/§25.1).

`PriceAudienceTableDto`: `totalCount`, `withPhoneCount`, `rows: [{customerId, name, phone,
previousSegment/currentSegment (+ …LabelUa each), itemsPerReceiptPrevious/Current,
typicalCheckCurrent, ltv}]`, `page/pageSize/totalPages`, `sortBy/sortDescending` (echoes the
normalized value), `recommendation: {triggerUa, actionUa, offerUa, cautionUa}`.

#### All-time mode (no period param at all)
```
GET  .../all-time?selectedSegment=Tier1..7&storeIds=                     -> PriceSegmentsAllTimeOverviewDto
GET  .../all-time/customers?segment=&storeIds=&page=&pageSize=&sortBy=&sortDescending= -> AllTimeCustomerTableDto
POST .../all-time/segments/{segment}/explain?storeIds=                   -> ExplainPriceSegmentResultDto | 503
POST .../exports/all-time   Body: ExportAllTimeRequest                    -> .xlsx
```
`sortBy`: `name|segment|items|check|purchases|ltv`.

`PriceSegmentsAllTimeOverviewDto`: `customersInBase`, `networkAverageCheck` (arithmetic mean —
**not** the median typical check), `purchasesTotal`, `turnoverTotal`,
`monthlyTrend: [{year, month, medianCheck, itemsPerReceipt}]`,
`insights: {yoyPercent, last3MonthsTrendPercent, belowPeakPercent, historicalPeakMedianCheck,
itemsPerReceiptChangePercent}` (all nullable — not enough history yet),
`distribution: [{segment, rangeLabelUa, customerCount, averageLtv}]` (always 7), `selectedSegment`,
`recommendation` (**null until a segment is selected** — mirrors the competitor's own "Оберіть
сегмент нижче" prompt, not a missing-data bug).

`AllTimeCustomerTableDto` — same page/sort envelope as the comparison table; rows carry
`purchaseCount` instead of before/after segment; no `recommendation` slot (table-level, not
row-level).

#### Frequency mode (`period=30|60|90`, no all-time)
```
GET  .../frequency/overview?...&declineThresholdPercent=                 -> FrequencyOverviewDto
GET  .../frequency/audiences/{audience}?...&minSpend=&maxSpend=&priceSegment=Tier1..7&declineThresholdPercent=&page=&pageSize=&sortBy=&sortDescending= -> FrequencyAudienceTableDto
POST .../frequency/audiences/{audience}/explain?...                      -> ExplainPriceSegmentResultDto | 503
POST .../exports/frequency-audience   Body: ExportFrequencyAudienceRequest -> .xlsx
```
`declineThresholdPercent` omitted → tenant's saved
`PriceSegmentSettings.DefaultFrequencyDeclineThresholdPercent` (default 30%).

`FrequencyOverviewDto`: `activeCurrentBuyerCount`, `activeBuyerCountChangePercent`,
`averageFrequencyCurrent/Previous`, `unionPopulationCount` (current ∪ previous buyers — frequency's
own denominator, distinct from comparison mode's `analyzedCount`), `atRiskCount`, **both**
`atRiskPercentOfUnionPopulation` and `atRiskPercentOfActiveCurrentBuyers` (the competitor only ever
shows the latter, ambiguously — analysis doc §17.6/§25.2), `averageSpendCurrentPeriod`, `audiences`
(4: Sleeping/Declining/Growing/Other).

`FrequencyAudienceTableDto` rows: `{customerId, name, phone, previousFrequency, currentFrequency,
frequencyDeltaAbsolute, frequencyDeltaPercent (nullable — null when previousFrequency=0, render
"—" never "∞"), typicalCheckCurrent (nullable — **always null for Sleeping**, render "—"),
spendCurrentPeriod, ltv}`. **For `audience=Sleeping`, `minSpend`/`maxSpend`/`priceSegment` filter
against the customer's PREVIOUS-period figures**, not current (current is always 0/null for
Sleeping by definition — the competitor's equivalent filter silently breaks here instead, analysis
doc §20.2) — only the filter re-orients, the displayed columns keep their stated meaning.

#### Settings (`/api/settings/price-segments`)
`[Authorize(Policy = AtLeastEnterpriseAdmin)]`, no `[RequireModule]` — same convention as
`LoyaltySettingsController` (enterprise_admin-gated settings controllers never carry one).
```
GET/PUT api/settings/price-segments -> PriceSegmentSettingsDto { defaultFrequencyDeclineThresholdPercent,
  minReceiptsForBoundaries, updatedAt }
```
GET returns proposed defaults (30%, `null`) before first save; `updatedAt` stays `null` until an
actual save happens. `PUT` rejects `defaultFrequencyDeclineThresholdPercent` outside 0-100 and a
negative `minReceiptsForBoundaries` with `400`.

#### Exports — all 3 `POST`, JSON body, response is the raw `.xlsx` file
Same PII posture as Фаза 1: phone masked by default (`PiiMasking.MaskPhone`, moved verbatim from
`MarketingAnalyticsService`, not forked), `unmaskPii` re-derived server-side against
`MarketingAnalyticsAuthorization.CanExportPii` regardless of what the client sends. Phase 2 never
selects `Email` anywhere, so there is no email-masking surface to check here (unlike Фаза 1's
export). Row cap 50 000, hardcoded server-side — export request DTOs carry no page/pageSize field
at all.

#### On-screen `phone` masking (all 3 GET tables — TASK-425 fix)
`PriceAudienceTableDto`/`AllTimeCustomerTableDto`/`FrequencyAudienceTableDto` rows' `phone` field
is masked by default (same `PiiMasking.MaskPhone`), full number only when the caller passes the
SAME `MarketingAnalyticsAuthorization.CanExportPii` check the exports already use — resolved
server-side by the controller, no client-facing parameter exists for this on any of the 3 GET
endpoints (unlike exports, there is no request-body `unmaskPii` to neutralize). QA (TASK-424)
found these 3 endpoints previously returned `phone` raw and unconditionally — masking existed only
in the export builders — a real gap wherever `marketing_analytics.view` is granted without
`marketing_analytics.export_pii` (ADR-020 capability split); fixed in TASK-425.

---

### Audience Builder (`/api/marketing-analytics/audience-builder`, Фаза 3 — TASK-429)

Base route: `api/marketing-analytics/audience-builder`. Same gate as RFM/Price Segments —
`[Authorize(Policy = MarketingAnalyticsViewOrCapability)]` + `[RequireModule("marketing_analytics")]`
at class level, same module key (not a new one). Every read endpoint below is **POST**, not GET —
the filter shape (term list, exclusions, two thresholds) doesn't fit a query string cleanly. All
DTO fields camelCase; enums are PascalCase **strings** (`JsonStringEnumConverter`), e.g.
`"kind": "Text"`.

#### `AudienceTermRequest`
```ts
{ kind: "Text" | "Category"; text: string | null; categoryId: string /* guid */ | null }
```
Only the field matching `kind` is read; the other is ignored. A term missing its own kind's value
is silently dropped server-side (never a 400). `Text` matches `Item.Name` (substring `ILIKE`), OR
an exact `Item.Barcodes` entry, OR an exact `Item.Id` — one field covers name/barcode/internal-id,
mirroring the competitor's "name, barcode, or external ID" box (this schema has no separate
external-SKU-id column, so `Item.Id` fills that role — see `glossary.md` "Term").

#### `GET /categories?search=&limit=` → `AudienceCategoryOptionDto[]`
`{ categoryId, name, itemCount }`. `limit <= 0` falls back to 20 server-side, clamped to `[1,100]`.

#### `POST /overview` — body `AudienceBuildRequest` → `AudienceOverviewDto`
```ts
AudienceBuildRequest = {
  from, to: string;                          // "YYYY-MM-DD"
  storeIds: string[] | null;                 // guids, null/empty = all stores
  terms: AudienceTermRequest[];
  mode: "Any" | "All";                       // OR / AND — see glossary.md "Term coverage"
  minQuantity, minAmount: number | null;      // combined via AND when both set
  excludedItemIds: string[] | null;           // manual SKU curation, guids
  page: number; pageSize: number; sortBy: string | null; sortDescending: boolean;
  canViewUnmaskedPii: boolean;                // IGNORED server-side on every read below — the
                                               // controller always recomputes it from the caller's
                                               // own capability, send false/omit
}
AudienceOverviewDto = { participantsCount, itemsInSelectionCount, unitsPurchased, totalSpend,
                         filtersHash, calculatedAt }
```
An empty (or all-malformed) `terms` list never touches the database — returns a zeroed DTO with a
valid `filtersHash` (mirrors "formation button disabled until a term exists").

#### `POST /buyers` — same body → `AudienceBuyerTableDto`
```ts
AudienceBuyerRowDto = { customerId, name, phone, quantityPurchased, receiptCount, totalAmount }
AudienceBuyerTableDto = { totalCount, withPhoneCount, rows: AudienceBuyerRowDto[],
  page, pageSize, totalPages, sortBy, sortDescending, filtersHash, calculatedAt }
```
`sortBy` allowlist: `name|qty|receipts|amount`, default `qty` descending. Unrecognized values
silently fall back to default (never a 400).

#### `POST /matched-items` — same body → `MatchedItemsTableDto`
```ts
MatchedItemRowDto = { itemId, name, barcodesJoined, isExcluded, quantitySold, receiptCount, buyerCount }
MatchedItemsTableDto = { totalCount, rows: MatchedItemRowDto[], page, pageSize, totalPages,
  sortBy, sortDescending, filtersHash, calculatedAt }
```
`sortBy` allowlist: `name|sold|receipts|buyers`, default `sold` descending. `barcodesJoined` is
`null` (never `""`) when the item has no barcode. Zero-sales SKUs are included (all 3 sales fields
`0`); `isExcluded` reflects whatever `excludedItemIds` was sent in the SAME request — toggling a
checkbox and re-calling this endpoint is how the UI refreshes it (no separate toggle endpoint).

#### `POST /exports/buyers` — body `ExportAudienceBuyersRequest` → `.xlsx`
Same fields as `AudienceBuildRequest` minus paging, plus `unmaskPii: boolean` (real client flag,
ANDed server-side with the caller's actual capability — requesting unmask without permission
silently falls back to masked, never a 403). **Receipt-level**, not customer-level — one row per
receipt (so one participant can produce several rows): Ім'я, Телефон, № чека, Дата, Заклад,
Куплено (шт — only the matched/selected SKUs on THAT receipt, not the receipt's full total), Сума
(₴ — same restriction). Capped at 50,000 rows server-side.

#### `POST /competitor/overview` — body `CompetitorAudienceRequest` → `CompetitorOverviewDto`
```ts
CompetitorAudienceRequest = {
  from, to: string; storeIds: string[] | null;
  ownTerms: AudienceTermRequest[]; ownExcludedItemIds: string[] | null;   // SAME state as the main tab
  competitorTerms: AudienceTermRequest[];
  horizon: "InPeriod" | "AllTime";           // see glossary.md "Exclusion horizon"
  page, pageSize, sortBy, sortDescending, canViewUnmaskedPii   // same as AudienceBuildRequest
}
CompetitorOverviewDto = { newAudienceCount, competitorItemsCount, unitsPurchased, totalSpend,
                           filtersHash, calculatedAt }
```
`ownTerms`/`competitorTerms` must each resolve to at least 1 valid term or the request
short-circuits to a zeroed result without touching the database. `unitsPurchased`/`totalSpend` are
always period-scoped — `horizon` only changes who counts as "new," never the KPI window.

#### `POST /competitor/buyers` — same body → `CompetitorBuyerTableDto`
Same row/table shape as `/buyers` (`CompetitorBuyerRowDto`/`CompetitorBuyerTableDto` — identical
fields, different type names). Same `sortBy` allowlist as `/buyers`, default `qty` descending.

#### `POST /exports/competitor-buyers` — body `ExportCompetitorBuyersRequest` → `.xlsx`
Same shape as `CompetitorAudienceRequest` minus paging, plus `unmaskPii`. **Customer-level**, not
receipt-level (Ім'я, Телефон, Куплено шт, Чеків, Сума ₴) — no raffle/draw scenario here, so
receipt granularity isn't needed.

#### PII + capability posture (same as RFM/Price Segments — TASK-431 confirmed)
`buyers`/`competitor/buyers` reads always resolve `CanViewUnmaskedPii` server-side
(`MarketingAnalyticsAuthorization.CanExportPii(User)`), never trusting the client's
`canViewUnmaskedPii` field. `AudienceOverviewDto`/`MatchedItemRowDto` carry no phone field at all.
Every export writes an `ActivityLog` row (filter snapshot + row count + masked flag), same audit
contract as Фаза 1/2. See `database-schema.md` TASK-428 and `decisions.md` ADR-023 addendum (Фаза
3) for the accepted Seq-Scan-on-ILIKE tradeoff behind the text-term search these endpoints use.
