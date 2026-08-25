# API Contracts

**Owner:** backend-developer + frontend-developer
**Updated:** 2026-08-24
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
  401: { error: "Temporary password has expired. Please request a new one." }  (TASK-465) —
        повертається ТІЛЬКИ коли пароль зійшовся з хешем, що належить простроченому тимчасовому
        паролю; на дійсно невірний пароль завжди generic-помилка вище, ніколи ця
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

POST /api/auth/forgot-password [public, rate limit 5/min per IP]  (TASK-456; редизайн TASK-464..466, 2026-08-04)
  Body: { email }
  204: завжди — незалежно від того, чи існує email/чи активний користувач (той самий
        no-enumeration принцип, що й login). UI не має розгалужувати копію за відповіддю.
        Генерує і надсилає тимчасовий пароль (14 символів, `RandomNumberGenerator`-backed,
        літера+цифра гарантовані конструктивно, без неоднозначних 0/O/1/I/l) — він одразу стає
        реальним `PasswordHash` акаунта, дійсний 3 години (`User.TempPasswordExpiresAt`). Немає
        окремого кроку "перейти за лінком і ввести новий пароль" — користувач одразу логіниться
        цим паролем через звичайний `POST /api/auth/login`.
```
Доставка (лист/Telegram) — через той самий існуючий Postgres outbox
(`INotificationRepository.EnqueueAsync`, `EventType="auth.password_reset_requested"`,
`Payload={tempPassword, expiresInMinutes: 180}`, `Channels=[email, telegram]`), не новий C# BullMQ
producer — механізм доставки не змінився попри редизайн, див. ADR-024 (superseded) + ADR-026.

Зміна пароля (включно з позбавленням від тимчасового статусу) йде через уже існуючий,
задокументований вище `POST /api/auth/change-password` — тепер він додатково скидає
`TempPasswordExpiresAt`. `POST /api/auth/reset-password` **більше не існує** (видалений разом зі
схемою токенів, TASK-464..466) — запит на цей шлях повертає стандартний 404.

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
  "tabs": ["string", "..."],
  "passwordIsTemporary": false,
  "temporaryPasswordExpiresAt": "ISO8601|null"
}
```
`capabilities` (ADR-020) and `tabs` (ADR-021, TASK-391b) are independent axes resolved from the
same `TenantRole` (via `TenantRoleId`) — both `null`/absent when the user has no `TenantRoleId`
or its template is archived. Both are UI-mirrors of the equally-named JWT claims below; real
enforcement is server-side (`RoleOrCapabilityHandler` for capabilities — nothing enforces `tabs`
server-side yet, see ADR-021 Tier 1/Tier 2).

`passwordIsTemporary`/`temporaryPasswordExpiresAt` (TASK-465, ADR-026) mirror
`User.HasActiveTempPassword`/`TempPasswordExpiresAt` — computed fresh by the same `ToDto` mapper
at every mint site (`login`, `2fa/verify`, `refresh`) and on `GET /auth/me`, so any one of them is
safe to read the flag from, not just the initial login response. Self-clears once the temp
password is changed (`change-password`) or simply expires — no client action needed to reset it.

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
GET    /api/users?storeIds=uuid                  -> UserDto[]  (storeIds repeated, TASK-517)
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

**`GET /api/users` `storeIds` filter (TASK-517, header store selector).** Repeated query param
(`?storeIds=uuid&storeIds=uuid`) — omitted/empty means "all stores" (unchanged behavior), same
convention as `PriceSegmentsController`'s `storeIds`. When non-empty, a user is included if
EITHER their role is outside `LocationScopedRoles` (`enterprise_admin` etc. — always visible,
unconditional bypass) OR they have at least one `user_locations` row whose location is in
`storeIds`. A `LocationScopedRoles` user with zero `user_locations` rows is excluded once a
specific-store filter is active, but still shows under "all stores" — same user `UserDto.
NeedsLocationAssignment` flags as needing setup. This filter never changes what
`NeedsLocationAssignment` means: that flag always reflects the user's full, unfiltered
assignment, not just locations among the currently filtered stores.

**Caller-scoping clamp (TASK-519, security fix).** TASK-517's `storeIds` was originally trusted
at face value with no check that the acting (JWT) caller was actually authorized to see those
stores — `users` was deliberately excluded from ADR-022 Stage 3's RLS rollout, so nothing at the
database layer compensated for that gap. `UsersController.GetAll` now passes the acting caller's
own id into `UserService.GetAllAsync`. When that caller's own role is in `LocationScopedRoles`
(`network_manager`, `store_manager`, `merchandiser`, `storekeeper`, `cashier`, `staff`), their
effective `storeIds` is ALWAYS clamped to their own `user_locations` assignment, regardless of
what they request: an explicit `storeIds` is intersected with their own stores; an
omitted/empty `storeIds` ("all stores") is treated as "my own stores," never the whole tenant;
and if the clamp collapses to zero effective stores (no assignment at all, or the entire request
falls outside their scope), the response fails closed — zero `LocationScopedRoles` users, still
including the always-visible non-scoped roles (e.g. `enterprise_admin`). A caller whose own role
is outside `LocationScopedRoles` gets no new restriction.

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
POST /api/marketplace/suppliers/{supplierId}/orders  { items:[{supplierItemId,qty}], comment?, destinationStoreId }
                                                                                       -> 201 MarketplaceOrderDto | 403 (no ACTIVE agreement) | 400 (empty items / missing destinationStoreId, TASK-586) | 404
GET  /api/marketplace/my-orders                                                        -> MarketplaceOrderDto[] (now carries destinationStoreId, null on pre-TASK-586 orders)
POST /api/marketplace/orders/{id}/cancel  { reason }                                   -> MarketplaceOrderDto | 400 (only status=new) | 404
POST /api/marketplace/suppliers/{supplierId}/support-tickets  { subject, message }     -> 201 SupplierSupportTicketDto (no agreement required)
GET  /api/marketplace/my-support-tickets                                               -> SupplierSupportTicketDto[]
GET  /api/marketplace/support-tickets/{id}                                             -> SupplierSupportTicketDto (with messages) | 404
POST /api/marketplace/support-tickets/{id}/messages  { body }                          -> 201 SupportTicketMessageDto | 400 | 404
```

**Marketplace order receiving (v4.3.1 — TASK-586, ADR-033).** Client-confirmed receipt of a
shipped order (scan/qty/expiry) — replaces the supplier's one-click Deliver; the endpoints below
are the *only* remaining path that can set an order's status to `delivered`
(`supplier-cabinet/orders/{id}/status` with `status:"delivered"` on a `shipped` order now always
400s — no `shipped` entry left in the transition table). Route addressing is order-centric
throughout (`orderId` in every path, never a separately surfaced receipt id — the receipt is 1:1
with its order). Auth: same `[Authorize]` + module `marketplace` as the rest of this controller;
mutations (b/d/e below) additionally require `CanReceiveStock` (storekeeper+).
```
GET  /api/marketplace/orders/awaiting-receipt                          -> 200 MarketplaceOrderDto[] (status=shipped, own tenant)
POST /api/marketplace/orders/{orderId}/receipt                         -> 200/201 MarketplaceOrderReceiptDto (idempotent create-or-get) | 404 order not found/not owned | 400 order not shipped | 400 order has no destinationStoreId (pre-TASK-586 order)
GET  /api/marketplace/orders/{orderId}/receipt                         -> 200 MarketplaceOrderReceiptDto | 404 no receipt started yet
PUT  /api/marketplace/orders/{orderId}/receipt/items/{itemId}  { productId?, quantityReceived?, expiryDate?, batchNumber?, discrepancyNotes? }
                                                                        -> 200 MarketplaceOrderReceiptDto | 404 receipt/item not found | 400 receipt already received | 400 negative quantity | 400 unknown productId
POST /api/marketplace/orders/{orderId}/receipt/finalize                -> 200 MarketplaceOrderReceiptDto (status=received, order status=delivered) | 404 | 400 already received | 400 gate: some item missing productId/quantityReceived/expiryDate
```
`productId` in the PUT body is resolved by the caller beforehand via the existing
`GET /api/items/by-barcode/{code}` (client-catalog-first — no marketplace-specific barcode
mapping). PUT field semantics: `quantityReceived`/`discrepancyNotes` overwrite directly (omit to
clear — resend the full known value each call); `productId`/`expiryDate`/`batchNumber` merge with
the existing value when omitted (`null` = leave alone, not clear). On finalize: creates one
`ProductStock` + one `StockMovement` per item (`sourceType`/`referenceType` =
`"marketplace_order_receipt"`), sets the order to `delivered`. No supplier notification is
enqueued (ADR-033 scopes that out — the plan only asked for a read-only supplier-cabinet view,
not built in this stage). DTOs: `MarketplaceOrderReceiptDto { id, marketplaceOrderId,
clientTenantId, supplierTenantId, destinationStoreId, destinationStoreName, status(draft|received),
createdByUserId?, receivedByUserId?, receivedAt?, createdAt, updatedAt, items: [...] }`,
`MarketplaceOrderReceiptItemDto { id, marketplaceOrderItemId, productId?, itemNameSnapshot,
productName?, quantityOrdered, quantityReceived?, expiryDate?, batchNumber?, discrepancyNotes?,
isResolved }` (`isResolved` = productId + quantityReceived + expiryDate all set — the exact
per-item finalize-gate condition, precomputed so callers don't re-implement it).

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
POST /api/supplier-cabinet/orders/{id}/status  { status, reason?, estimatedDeliveryDays? } -> dto; new->confirmed|cancelled, confirmed->shipped|cancelled; cancel requires reason, ship requires estimatedDeliveryDays (>0, TASK-584) and enqueues client notification "marketplace_order.shipped". No transition out of shipped any more (TASK-586, ADR-033) — status:"delivered" on a shipped order always 400s "Перехід зі статусу 'shipped' у 'delivered' неможливий."; delivered is now set only by the client's own receiving flow, see "Marketplace order receiving" below.
POST /api/supplier-cabinet/orders/{id}/delay-reason  { reason }    -> dto | 400 (empty reason / order not shipped) | 404; supplier-only, only while status=shipped (TASK-585), enqueues client notification "marketplace_order.delay_reason_added"
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
  "amount": 0.00, "balanceAfter": 0.00, "note": "string|null", "createdAt": "ISO8601",
  "posTransactionId": "uuid|null" }
```
`posTransactionId` (TASK-624) is set only on `accrual` entries created at checkout (and only
going forward — entries recorded before TASK-624 shipped have it null). Consumer clients use
it to offer "leave a review" (`POST /api/consumer/reviews`) on the underlying purchase.

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

---

### Post-Campaign Analysis (`/api/marketing-analytics/post-campaign`, Фаза 4 — TASK-472)

Base route: `api/marketing-analytics/post-campaign`. Same class-level gate as RFM/Price Segments/
Audience Builder — `[Authorize(Policy = MarketingAnalyticsViewOrCapability)]` +
`[RequireModule("marketing_analytics")]`, same module key as the rest of Фаза 1-3 (not a new one).
All DTO fields camelCase. The one wire-facing enum this module reuses is Фаза 1's `RfmSegmentKey`
(`segmentBefore`/`segmentAfter` below — PascalCase string, e.g. `"Champions"`, nullable = "Без
покупок"); `PostCampaignBehaviorStatus` (reactivated/retained/dropped/notReturned — see
`glossary.md`) is classified server-side but never itself serialized — only its aggregate counts
appear in `PostCampaignSummaryDto`.

**⚠️ `Import` carries a STRICTER gate than every other action on this controller — easy to miss on
a skim, since every other module controller in this file uses exactly one floor for all its
actions.** `ListSegments`, `Analyze`, all 5 report-tab GETs, `Explain`, and both exports only need
the class-level `MarketingAnalyticsViewOrCapability` floor (store_manager+ **or** the
`marketing_analytics.view` TenantRole capability). `Import` additionally requires
`MarketingAnalyticsAuthorization.CanImportSegments` — **role-only** (`AtLeastStoreManagerRoles`,
no capability-widening escape hatch at all, unlike `CanExportPii`'s role-OR-capability shape) — so
a caller holding `marketing_analytics.view` (or even `marketing_analytics.export_pii`) but ranked
below store_manager can open every report tab on an already-analyzed segment yet cannot import a
new one. Added by TASK-477 as the fix for TASK-474 finding B (source doc §32's explicit "окреме
право на upload" ask); returns `403 Forbid()`, not a silent no-op. See `decisions.md` ADR-023
addendum (Фаза 4) point (e) for why this is role-only rather than a new delegable capability.

`storeIds` is a repeated query param on every report GET (`?storeIds=guid1&storeIds=guid2`,
omitted/empty = all stores) — same shape as Фаза 1/2/3. A `PostCampaignSegment` isn't itself
store-scoped (its members came from an externally-sourced list, not a store-scoped query); this is
purely an analysis-time filter over which stores' transactions count toward the before/after KPIs.

#### `GET /segments` → `PostCampaignSegmentListItemDto[]`
```ts
{ id: string; name: string | null; createdAt: string;
  uploadedCount: number; matchedCount: number; isAnalyzed: boolean;
  afterStart: string | null; afterEnd: string | null;    // "YYYY-MM-DD", null while draft
  analyzedAt: string | null }
```

#### `POST /segments/import` — multipart/form-data (the only non-JSON endpoint in this module,
since the same call must also accept a binary file)
```
file?: binary             -- .csv / .xlsx / .txt only (extension allowlist)
rawText?: string           -- exactly one of file/rawText must be present
name?: string              -- optional segment label, e.g. "SMS блиц — серпень"
columnIndex?: number       -- re-submit the SAME file with an explicit column after reviewing
                              the auto-detected preview (see columnPreview below)
```
Request-size cap: **10 MB**, enforced by `[RequestSizeLimit]` at the Kestrel/authorization-filter
stage — genuinely enforced (confirmed this app runs Kestrel-behind-Nginx, not IIS in-process), and
covers the whole multipart body (file + rawText + other fields together), not just the file part.

**Import size limits are two layers, not one — verify-the-fix lesson from TASK-474→477 (full story
in `decisions.md` ADR-023 addendum, Фаза 4 point d):**
1. **`ImportLimits.MaxUncompressedZipEntryBytes` (20 MB), XLSX only, checked BEFORE ClosedXML ever
   opens the file.** A `.xlsx` is a ZIP container; `System.IO.Compression.ZipArchive` +
   `ZipArchiveEntry.Length` reads each part's real uncompressed size directly off the ZIP central
   directory — cheap (0-9 ms measured even against an 85 MB payload) regardless of file size,
   because no decompression is needed to read it. This is the layer that actually matters: `new
   XLWorkbook(stream)` alone (ClosedXML's constructor) fully materializes the whole workbook into
   memory before any row/column count is available to check against — a <5 MB crafted file was
   measured costing ~38 s / ~1.7 GB allocated inside that constructor call alone.
2. **`ImportLimits.MaxRows` (25,000) / `MaxColumns` (300)**, checked against the parsed range's
   bounding box before the per-cell copy loop runs. Applies to XLSX; the row check alone (no
   column check — a rows-by-columns nested materialization only happens for XLSX) also applies to
   CSV/raw-text via `SegmentImportParser.ParseTextList`/`ParseCsvText`, ahead of their own
   per-token/per-line work (defense-in-depth — that path was already bounded by the 10 MB request
   cap above).

Both ceilings are deliberately more generous than `PostCampaignService.MaxAcceptedRows`
(**20,000** — the real, user-facing business cap, checked separately and later in `ImportAsync`)
so a submission only a little over 20,000 still gets that friendlier, specific error message
instead of the generic "too large" one; only a drastically oversized upload (the actual attack
shape) hits the `ImportLimits` ceilings. Exceeding any of the three ceilings returns `400 { error
}` — never a 500 (TASK-477 finding C also hardened the malformed-file case: a `.xlsx`-named file
ClosedXML can't actually parse now returns a clean `400`, not an unhandled exception).

```ts
PostCampaignImportResultDto = {
  segmentId: string; name: string | null;
  uploadedCount: number; matchedCount: number; duplicateCount: number;
  unknownCount: number; invalidCount: number;
  unknownTokensSample: string[];       // first ~20 unmatched-but-well-formed tokens
  invalidTokensSample: string[];       // first ~20 tokens that failed format validation entirely
  columnPreview: {                      // null for raw-text imports (no tabular structure)
    headers: string[]; detectedColumnIndex: number; detectedColumnHeader: string | null;
    sampleRows: (string | null)[][]     // every column, not just the detected one
  } | null;
  createdAt: string;
}
```
A fresh `PostCampaignSegment` row is created on EVERY import call, even a re-upload of the same
content — there is no "update an existing draft" endpoint (see `glossary.md` "Draft vs. analyzed
segment"). Each token classifies as a whole GUID or a whole phone number (never a substring — the
source doc's own documented competitor bug), then resolves against real `Customer` rows in this
tenant by `Customer.Id` OR normalized phone, reusing Фаза 0's existing `PhoneNormalizer` (see
`decisions.md` ADR-023 addendum, Фаза 4, point b).

#### `POST /segments/{id}/analyze` — body `{ afterStart: string; afterEnd: string }` (`"YYYY-MM-DD"`)
```ts
PostCampaignAnalyzeResultDto = {
  segmentId: string; afterStart: string; afterEnd: string;
  beforeStart: string; beforeEnd: string;       // before window auto-derived, equal length
  segmentHash: string; analyzedAt: string;
}
```
`400` if `afterStart > afterEnd`; `404` if the segment doesn't exist or doesn't belong to this
tenant. Safe to call again on an already-analyzed segment with new dates — re-freezes the window in
place (`glossary.md` "Draft vs. analyzed segment").

#### Report tabs — all `GET`, all take `?storeIds=` only
All five `404` if the segment doesn't exist; a still-**draft** segment returns `400` (not `404`) —
`MapError` maps any service error ending in `"not found."` to `404`, everything else (including
"Segment has not been analyzed yet") to `400`.

`GET .../summary` → `PostCampaignSummaryDto`:
```ts
{ matchedCount: number;
  moneyBefore: number; moneyAfter: number; moneyDeltaPercent: number | null;
  buyersAfter: number;
  reactivated: number; reactivationRatePercent: number | null;    // reactivated / inactiveBefore
  inactiveBefore: number;                                         // reactivated + notReturned
  retained: number; retentionRatePercent: number | null;          // retained / activeBefore
  activeBefore: number;                                           // retained + dropped
  dropped: number; churnRatePercent: number | null;                // dropped / activeBefore
  notReturned: number;
  recommendation: { triggerUa: string; actionUa: string; offerUa: string; cautionUa: string };
  segmentHash: string; afterStart: string; afterEnd: string;
  beforeStart: string; beforeEnd: string; calculatedAt: string; }
```
`reactivated + retained + dropped + notReturned` always equals `matchedCount` (`glossary.md`).
Every `*RatePercent` is `null`, never `0`, when its own denominator is zero.

`GET .../daily-turnover` → `PostCampaignDailyTurnoverDto`:
```ts
{ points: { dayIndex: number; afterCalendarDate: string; beforeAmount: number; afterAmount: number }[];
  totalBefore: number; totalAfter: number;
  segmentHash: string; afterStart: string; afterEnd: string;
  beforeStart: string; beforeEnd: string; calculatedAt: string; }
```
`dayIndex` is 1-based, shared ordinal position between the before/after series (aligned by ordinal
day-in-window, not calendar date); `afterCalendarDate` is the real after-window date for that
ordinal day, for the chart's X-axis labels.

`GET .../rfm-activity` → `PostCampaignRfmActivityDto`:
```ts
{ checksBefore: number; checksAfter: number; checksDeltaPercent: number | null;
  turnoverBefore: number; turnoverAfter: number; turnoverDeltaPercent: number | null;
  averageCheckBefore: number | null; averageCheckAfter: number | null; averageCheckDeltaPercent: number | null;
  recencyBeforeDays: number | null; recencyAfterDays: number | null; recencyDeltaPercent: number | null;
  recencyDenominatorBefore: number; recencyDenominatorAfter: number;   // see below
  segmentHash: string; afterStart: string; afterEnd: string;
  beforeStart: string; beforeEnd: string; calculatedAt: string; }
```
`recencyDenominatorBefore`/`After` expose exactly how many segment members had ANY purchase in that
window — customers with none are excluded from the recency average entirely rather than pulled in
with a made-up value (source doc's "show the denominator" transparency rule).

`GET .../customers?storeIds=&page=&pageSize=&sortBy=&sortDescending=` → `PostCampaignCustomerTableDto`.
`sortBy` allowlist: `checksbefore|checksafter|turnoverbefore|turnoverafter|transition`, default
`turnoverafter` descending (unrecognized value silently falls back, never a `400`). Full server
pagination, **no Top-200 cap** — same explicit fix-over-competitor pattern as Фаза 3's
AudienceBuilder customer table.
```ts
PostCampaignCustomerRowDto = {
  customerId: string; name: string; phone: string | null;    // masked unless CanExportPii
  checksBefore: number; checksAfter: number; turnoverBefore: number; turnoverAfter: number;
  segmentBefore: RfmSegmentKey | null; segmentBeforeLabelUa: string;   // null = "Без покупок"
  segmentAfter: RfmSegmentKey | null; segmentAfterLabelUa: string;
}
```
`phone` resolves `CanViewUnmaskedPii` server-side (`MarketingAnalyticsAuthorization.CanExportPii`),
same as every sibling phase's on-screen table — no client-facing unmask parameter on this endpoint.

`GET .../migration` → `PostCampaignMigrationDto`:
```ts
{ beforeDistribution: { segment: RfmSegmentKey | null; labelUa: string; count: number; sharePercent: number }[];
  afterDistribution:  { segment: RfmSegmentKey | null; labelUa: string; count: number; sharePercent: number }[];
  matrix: { before: RfmSegmentKey | null; beforeLabelUa: string;
            after: RfmSegmentKey | null; afterLabelUa: string; count: number }[];
  upCount: number; stableCount: number; downCount: number;      // sum to matchedCount
  recommendation: { triggerUa: string; actionUa: string; offerUa: string; cautionUa: string };
  segmentHash: string; afterStart: string; afterEnd: string;
  beforeStart: string; beforeEnd: string; calculatedAt: string; }
```
`matrix` is **sparse** (only cells that actually occurred) — the frontend renders the full fixed
12×12 grid itself with dots for empty cells. `null` segment = the "Без покупок" bucket. See
`glossary.md` "RFM migration matrix" and `decisions.md` ADR-023 addendum (Фаза 4) point (c) for how
`beforeDistribution`/`afterDistribution`/`matrix` are computed without a second RFM implementation.

#### `POST /segments/{id}/explain` → `ExplainPostCampaignResultDto`
```json
{ "explanationUa": "string", "model": "string", "tokensUsed": 0 }
```
`503 { "error": "..." }` when no Claude key is configured — same failure shape as every other
`/explain` endpoint in this module. The prompt never receives the raw uploaded token samples —
only aggregate counts/rates and the already-shown template recommendation strings, plus the
staff-typed segment `Name` (low-severity, same accepted shape as Фаза 1's `TopProductName`
precedent — TASK-474 item 8).

#### Exports — both `POST`, response is the raw `.xlsx` file
```
POST .../exports/customers        Body: { storeIds: string[] | null; unmaskPii: boolean }
POST .../exports/unknown-tokens   No body
```
`exports/customers` — same row shape as the on-screen customer table; `unmaskPii` is ANDed
server-side with `CanExportPii` (requesting unmask without permission silently falls back to
masked, never a `403`). `exports/unknown-tokens` — the uploader's own raw unmatched/invalid tokens
(never resolved Customer PII, so no PII gate applies), backing the "download error report" flow
after an import with unknowns. Both write an `ActivityLog` row
(`marketing_analytics.post_campaign.export_customers` / `..._export_unknown_tokens`), same audit
contract as every sibling phase's exports, and both pass every cell through the same
`ExcelExportService`/`SanitizeForSpreadsheet` formula-injection guard as every other export in this
codebase (TASK-414) — confirmed to cover raw uploaded token text too, not just resolved customer
names (TASK-474 item 2).

---

### Store Migration (`/api/marketing-analytics/store-migration`, TASK-501/504)

Same class-level gate as the rest of this controller — `[Authorize(Policy =
MarketingAnalyticsViewOrCapability)]` + `[RequireModule("marketing_analytics")]`, no new module
key. Same shared filter query params as `/overview` above (`period`/`from`/`to`/`storeIds`,
`storeIds` repeated, empty = all stores). All responses camelCase.

```
GET  /api/marketing-analytics/store-migration            -> StoreMigrationOverviewDto
GET  /api/marketing-analytics/store-migration/customers  -> StoreMigrationCustomerRowDto[]
POST /api/marketing-analytics/exports/store-migration    -> .xlsx
```

**Migration definition**: within `[from, to]`, a customer "migrated" if their earliest
transaction's store differs from their latest transaction's store — first→last only, not every
hop in between (a customer who visited store A→B→A in the period shows as "not migrated," not as
two flows). **Store filter semantics differ from every other GET in this file**: a flow/customer
row matches the selected `storeIds` if EITHER the from-store or the to-store is in the list (OR,
not AND) — selecting just one store surfaces both "customers who left this store" and "customers
who arrived at this store."

#### `GET /store-migration` → `StoreMigrationOverviewDto`
```ts
{
  activeCustomerCount: number;      // customers with >=1 receipt in the period (store filter applied)
  migratedCustomerCount: number;    // sum of flows[].customerCount
  migratedSharePercent: number;     // migratedCustomerCount / activeCustomerCount * 100, 0 if activeCustomerCount=0
  flows: { fromStoreId: string; fromStoreName: string; toStoreId: string; toStoreName: string;
           customerCount: number; revenue: number }[];      // non-zero matrix cells only
  netFlowByStore: { storeId: string; storeName: string; gained: number; lost: number; net: number }[];
  periodFrom: string; periodTo: string;    // "YYYY-MM-DD"
}
```
No `filtersHash`/`calculatedAt` (unlike the RFM overview/segment DTOs above) — this DTO
deliberately omits them.

#### `GET /store-migration/customers` → `StoreMigrationCustomerRowDto[]`
Not in the original plan — added because the repository/service layer both needed a
customer-drill-down query anyway (on-screen small limit + export large limit), following this
controller's existing pattern of separate drill-down GETs (e.g.
`segments/{key}/products/{productName}/affinity`). Same query params as the overview GET above,
plus `limit` (int, default 100, max 500 — an out-of-range value silently falls back to 100, never
`400`). Ordered most-recent-migration-first.
```ts
{
  customerId: string; name: string; phone: string | null; email: string | null;
  fromStoreId: string; fromStoreName: string; fromDate: string;     // "YYYY-MM-DD"
  toStoreId: string; toStoreName: string; toDate: string;
  transactionCountInPeriod: number; revenueInPeriod: number;
}[]
```
`phone`/`email` are **always masked** here — there is no unmask query param on this endpoint,
unlike other on-screen tables in this module where masking depends on `CanExportPii`. Unmasked
data is only ever available via the export below.

#### `POST /exports/store-migration` → raw `.xlsx`
```
Body: { storeIds: string[] | null; from: string; to: string; unmaskPii: boolean }
```
No `key` field — unlike the other 3 exports on this controller, this one has no RFM-segment
concept. Same response shape as every other export (raw bytes, `Content-Disposition` filename
`store_migration_<timestamp>.xlsx`). `unmaskPii: true` is honored only if the caller passes
`MarketingAnalyticsAuthorization.CanExportPii` server-side — same gate as the other 3 exports, no
new capability introduced. Columns (in order): Ім'я, Телефон, Email, Заклад (перша покупка), Дата
першої покупки, Заклад (остання покупка), Дата останньої покупки, К-сть чеків, Сума.

Both GETs return `200` with empty/zeroed data for a tenant/period with no migrations — never
`404` (same "empty state is still a valid DTO" convention as the rest of this file). Single-store
tenants always get `flows: [], netFlowByStore: [], migratedCustomerCount: 0` (no cross-store data
to detect).

---

### Consumer Profile self-service (`/api/consumer/profile`, TASK-613/614)

ConsumerAccount session only (`[Authorize]`, claim `consumer_account_id` — same posture as every
other `/api/consumer/*` controller in this file; a staff JWT is rejected with 403 before any DB
call). `ConsumerAccountProfileChange` carries no RLS at all (`database-schema.md`), so this claim
check is the whole app-level boundary for the history endpoint, not a backstop on top of RLS.
```
GET api/consumer/profile                              -> 200 ConsumerProfileDto | 403 | 404
PUT api/consumer/profile           Body: UpdateConsumerProfileRequest -> 200 ConsumerProfileDto | 400 { error } | 403 | 404 | 409 { error }
PUT api/consumer/profile/phone     Body: ChangeConsumerPhoneRequest   -> 200 ConsumerProfileDto | 400 { error } | 403 | 404 | 409 { error }
GET api/consumer/profile/history?page=1&pageSize=50   -> 200 PagedResult<ConsumerProfileChangeDto>
```
`409` — duplicate email/phone (app-level check, case-insensitive for email; no DB unique
constraint on `Email`).

#### ConsumerProfileDto
```json
{ "consumerAccountId": "uuid", "fullName": "string", "email": "string|null",
  "phone": "string", "registeredAt": "ISO8601" }
```

#### UpdateConsumerProfileRequest
```json
{ "fullName": "string|null", "email": "string|null" }
```
Each field independently optional — `null` leaves it unchanged. Empty/whitespace `email` clears it
(sets null); `fullName` may not be blank if provided.

#### ChangeConsumerPhoneRequest
```json
{ "newPhone": "string", "currentPassword": "string" }
```
Gated by password re-entry, not SMS/OTP — no SMS gateway exists in this repo, and registration
itself never verifies phone either (see `decisions.md` ADR-034). Setting the phone to its own
current normalized value succeeds silently and writes no audit row.

#### ConsumerProfileChangeDto
```json
{ "fieldName": "phone|email|full_name", "oldValue": "string|null", "newValue": "string|null",
  "changedAt": "ISO8601" }
```
Same shape as the staff-facing `GET /api/customers/{id}/profile-history` response below — both
read the same underlying data, just reached via a different id.

---

### Loyalty tier ladder — admin CRUD (`/api/settings/loyalty/tiers`, TASK-615)

`[AtLeastEnterpriseAdmin]`, mirrors `LoyaltySettingsController`'s upsert shape.
```
GET api/settings/loyalty/tiers   -> 200 LoyaltyTierDefinitionDto[]   -- ordered by sortOrder, [] not null
PUT api/settings/loyalty/tiers   Body: UpsertTierRequest[] -> 200 LoyaltyTierDefinitionDto[] | 400 { error }
```

#### LoyaltyTierDefinitionDto
```json
{ "id": "uuid", "name": "string", "sortOrder": 0, "minCompositeScore": 0.0,
  "accrualMultiplier": 1.0, "discountPercent": 0.0 }
```

#### UpsertTierRequest (PUT body — array, no `id`)
```json
{ "name": "string", "sortOrder": 0, "minCompositeScore": 0.0,
  "accrualMultiplier": 1.0, "discountPercent": 0.0 }
```
**Bulk-replace, matched by `sortOrder`** — see `domain-model.md`'s `LoyaltyTierDefinition` entry
for the identity-preservation/reordering implications. `400` validation: duplicate `sortOrder`,
empty `name`, `accrualMultiplier` outside `[0, 999.99]`, `discountPercent` outside `[0, 100]`.

---

### Loyalty tier ladder — consumer-facing (`/api/consumer/loyalty/{tenantId}/tiers*`, TASK-615)

Same `ConsumerLoyaltyController` as the existing wallet/code/history endpoints above — `[Authorize]`
+ `consumer_account_id` claim.
```
GET api/consumer/loyalty/{tenantId}/tiers                              -> 200 LoyaltyTierProgressDto | 403 | 404
GET api/consumer/loyalty/{tenantId}/tiers/history?page=1&pageSize=50   -> 200 PagedResult<LoyaltyTierChangeHistoryDto> | 403 | 404
```

#### LoyaltyTierProgressDto
```json
{ "currentTierId": "uuid|null", "currentTierName": "string|null",
  "accrualMultiplier": 1.0, "discountPercent": 0.0, "compositeScore": 0.0,
  "nextTierId": "uuid|null", "nextTierName": "string|null", "scoreToNextTier": 0.0 }
```
`currentTierId`/`currentTierName` null + `accrualMultiplier`/`discountPercent` at their neutral
defaults (1.0/0) means no tier assigned yet — matches exactly how `PosService` treats a tierless
membership. `nextTier*`/`scoreToNextTier` are null at the top rung, or when no ladder is configured
at all.

#### LoyaltyTierChangeHistoryDto
```json
{ "id": "uuid", "fromTierName": "string|null", "toTierName": "string|null",
  "fromScore": 0.0, "toScore": 0.0, "changedAt": "ISO8601" }
```

---

### Consumer support tickets (TASK-613/616)

Two controllers, one shared `ConsumerSupportTicketDto` (`messages` is `null` on list endpoints,
populated oldest-first only on the single-ticket read).

**Consumer-facing** (`/api/consumer/support`, `[Authorize]` + `consumer_account_id` claim):
```
POST api/consumer/support/tickets                 Body: CreateConsumerSupportTicketRequest -> 201 ConsumerSupportTicketDto | 400 { error } | 404
GET  api/consumer/support/tickets?tenantId=&page=&pageSize=   -> 200 PagedResult<ConsumerSupportTicketDto>   -- tenantId required
GET  api/consumer/support/tickets/{id}             -> 200 ConsumerSupportTicketDto | 404   -- 404 for both "doesn't exist" and "not yours", never distinguished
POST api/consumer/support/tickets/{id}/messages    Body: AddConsumerSupportTicketMessageRequest -> 201 ConsumerSupportTicketMessageDto | 400 { error } | 404
```

**Staff-facing** (`/api/customer-support`, `[AtLeastStoreManager]`, not `[RequireModule]`-gated —
matches `CustomersController`'s own unconditional access):
```
GET  api/customer-support/tickets?status=&page=&pageSize=   -> 200 PagedResult<ConsumerSupportTicketDto>   -- status optional filter, newest-first
GET  api/customer-support/tickets/{id}             -> 200 ConsumerSupportTicketDto   -- marks unread consumer messages read as a side effect
POST api/customer-support/tickets/{id}/reply       Body: AddStaffSupportReplyRequest -> 201 ConsumerSupportTicketMessageDto | 400 { error } | 404
PUT  api/customer-support/tickets/{id}/status      Body: UpdateConsumerSupportTicketStatusRequest -> 200 ConsumerSupportTicketDto | 400 { error } | 404
```
`status` ∈ `open | in_progress | resolved | closed`. A consumer reply on a Resolved/Closed ticket
flips it back to `open` automatically (server-side, not a client choice).

**⚠️ `GetInboxAsync` has no `customerId` filter param** — the web `/customer-support?customerId=`
deep link filters client-side over a widened page instead of a true backend filter. See
`known-issues.md` KI-034.

#### ConsumerSupportTicketDto
```json
{ "id": "uuid", "tenantId": "uuid", "consumerAccountId": "uuid", "consumerName": "string",
  "consumerPhone": "string", "customerId": "uuid|null", "customerName": "string|null",
  "subject": "string", "status": "open|in_progress|resolved|closed",
  "createdAt": "ISO8601", "updatedAt": "ISO8601",
  "messages": null }
```
`messages` is `ConsumerSupportTicketMessageDto[]` (oldest-first) on the two single-ticket GETs,
`null` on both list endpoints.

#### ConsumerSupportTicketMessageDto
```json
{ "id": "uuid", "ticketId": "uuid", "senderConsumerAccountId": "uuid|null",
  "senderUserId": "uuid|null", "body": "string", "isRead": false, "createdAt": "ISO8601" }
```
Exactly one of `senderConsumerAccountId`/`senderUserId` is set per message — the client derives
"mine vs. theirs" from that.

#### Request bodies
```ts
CreateConsumerSupportTicketRequest      { tenantId: string; subject: string; body: string }
AddConsumerSupportTicketMessageRequest  { body: string }
AddStaffSupportReplyRequest             { body: string }
UpdateConsumerSupportTicketStatusRequest{ status: string }
```
`tenantId` travels in the body, not the route, on ticket creation — a consumer session is
cross-tenant by design (same shape `ConsumerLoyaltyController`'s `SetPreferredStoreRequest` uses).

#### Realtime — SignalR Hub (TASK-625)

Delivery-only transport layered on top of the REST channel above. **Message creation always
stays on REST** (`POST .../messages`, `POST .../reply`) — the Hub never accepts a message write,
it only pushes an event after the REST call's `SaveChangesAsync` has already committed. Not a
guaranteed-delivery transport: the backend keeps no outbox/redelivery queue, so a client must
still treat REST (`GET` the ticket) as the source of truth on every reconnect.

**Hub URL (final):** `/api/hubs/consumer-support` — under the `/api` prefix, consistent with
every REST route in this document. (No alternate `/hubs/...` mapping exists; the spec that
produced this task allowed either, `/api/hubs/consumer-support` was chosen and is the only one
registered in `Program.cs`.)

**Auth:** same JWT bearer token as the REST endpoints above (`[Authorize]`, no extra policy —
both a consumer token and a staff token connect to the same Hub). A WebSocket/SSE/long-polling
handshake can't set an `Authorization` header, so the SignalR client library instead appends
`?access_token=<jwt>` to the Hub URL — the API's `JwtBearerEvents.OnMessageReceived` accepts the
token from that query parameter, but **only** on requests to `/api/hubs/consumer-support`; every
other route still requires a real `Authorization: Bearer` header. Transport: WebSocket first,
falling back to SignalR's other built-in transports (Server-Sent Events, then long polling) —
default `HubConnectionBuilder` behavior, no client-side transport pinning needed.
`KeepAliveInterval`/`ClientTimeoutInterval`/`HandshakeTimeout` are 15s/30s/15s server-side
(`AddSignalR()` in `ShelfGuard.Infrastructure/DependencyInjection.cs`) — the framework defaults,
set explicitly rather than left implicit.

**Hub methods (client → server):**
```
JoinTicket(ticketId: string)   // adds this connection to group "consumer-support-ticket:{ticketId}"
LeaveTicket(ticketId: string)  // explicit exit — disconnect also drops every group automatically
```
`JoinTicket` re-validates access server-side on every call — the client-supplied `ticketId` is
never trusted on its own:
- **Consumer token:** allowed only if `ticket.consumerAccountId` (looked up server-side from the
  ticket row, not from anything the client sent) equals the JWT's own `consumer_account_id` claim.
- **Staff token:** allowed only if the JWT carries a role at or above `store_manager`
  (`AppPolicies.AtLeastStoreManagerRoles` — same floor as `GET /api/customer-support/tickets/{id}`)
  **and** `ticket.tenantId` equals the JWT's `tenant_id` claim.
- Any other case (ticket not found, wrong owner, wrong tenant, role below the floor, a token with
  neither claim) throws a SignalR `HubException` ("Access denied.") and the connection is **not**
  added to the group — no partial/silent failure state to handle client-side, just catch the
  exception from the `.invoke("JoinTicket", ticketId)` call.
- On reconnect (SignalR auto-reconnect or a fresh connection after a drop), the client must call
  `JoinTicket` again for every ticket thread it still has open — group membership does not
  survive a connection change — and should follow up with `GET
  api/consumer/support/tickets/{id}` (or the staff equivalent) to pick up anything sent while
  disconnected, since SignalR itself replays nothing.

**Server events (server → client), both sent only to group `consumer-support-ticket:{ticketId}`:**

`SupportMessageCreated` — after a successful `POST .../messages` or `POST .../reply`:
```json
{
  "ticketId": "uuid",
  "message": {
    "id": "uuid",
    "ticketId": "uuid",
    "senderConsumerAccountId": "uuid|null",
    "senderUserId": "uuid|null",
    "body": "string",
    "isRead": false,
    "createdAt": "ISO8601"
  }
}
```
`message` is the exact same `ConsumerSupportTicketMessageDto` (§ above) already returned in the
triggering HTTP response — `message.id` is guaranteed to equal that response's `id`. The event
can arrive back to whichever party sent the message (they're typically still joined to the group
themselves) — de-duplicate client-side on `message.id`, don't assume the sender is excluded.

`SupportTicketStatusChanged` — after a successful `PUT api/customer-support/tickets/{id}/status`:
```json
{ "ticketId": "uuid", "status": "open|in_progress|resolved|closed", "updatedAt": "ISO8601" }
```
Note: a consumer reply that server-side auto-reopens a Resolved/Closed ticket (see above) does
**not** publish this event — that implicit status flip is only visible via the ticket's own
`status` field on the next `GET`, same as any other REST-only field change before this task.

**Cross-tenant/cross-consumer isolation:** enforced entirely server-side inside `JoinTicket`
(never by trusting a client-supplied `tenantId`/`consumerAccountId`) — see the access rules
above. A connection that never successfully joined a ticket's group receives no events for it,
regardless of what it may already know (e.g. a valid ticket id guessed or seen elsewhere).

---

### Purchase reviews (TASK-613/617)

Two controllers, one shared `PurchaseReviewDto`.

**Consumer-facing** (`/api/consumer/reviews`, `[Authorize]` + `consumer_account_id` claim):
```
POST api/consumer/reviews   Body: CreatePurchaseReviewRequest -> 201 PurchaseReviewDto | 400 { error } | 403 { error } | 404 | 409 { error }
GET  api/consumer/reviews?tenantId=&page=&pageSize=   -> 200 PagedResult<PurchaseReviewDto>   -- tenantId required
```
`403` — the transaction belongs to a different consumer, **or** has no loyalty-ledger link at all
(walk-in sale) — both cases return the same generic message, never disclosing which (see
`domain-model.md`'s `PurchaseReview` entry for the ownership-resolution mechanism and its
walk-in-purchase limitation). `409` — a review already exists for this `posTransactionId` (checked
pre-insert, backstopped by the DB's own unique constraint).

**Staff-facing** (`/api/reviews`, `[AtLeastStoreManager]`, not `[RequireModule]`-gated):
```
GET api/reviews?rating=&page=&pageSize=   -> 200 PagedResult<PurchaseReviewDto>   -- rating optional filter, newest-first
PUT api/reviews/{id}/reply   Body: ReplyToPurchaseReviewRequest -> 200 PurchaseReviewDto | 400 { error } | 404 | 409 { error }
```
`409` on a second reply attempt — **one reply per review, enforced here even though the older,
analogous `SupplierReview`/`SupplierCabinetService.ReplyToReviewAsync` silently allows
overwriting** (deliberate divergence, see `decisions.md` ADR-034).

#### PurchaseReviewDto
```json
{ "id": "uuid", "tenantId": "uuid", "consumerAccountId": "uuid", "consumerName": "string",
  "consumerPhone": "string", "posTransactionId": "uuid", "rating": 5, "comment": "string|null",
  "createdAt": "ISO8601", "replyText": "string|null", "repliedAt": "ISO8601|null",
  "repliedByUserId": "uuid|null" }
```

#### Request bodies
```ts
CreatePurchaseReviewRequest    { tenantId: string; posTransactionId: string; rating: number; comment?: string }
ReplyToPurchaseReviewRequest   { replyText: string }
```
`tenantId` travels in the body on create, same reasoning as `CreateConsumerSupportTicketRequest`
above. `rating` outside `1..5` is `400`, rejected before any repository call.

---

### Customer detail extension + staff profile-change history (`/api/customers`, TASK-618/621b)

`GET /api/customers/{id}` (unchanged route/auth, `AtLeastStoreManager`) — `CustomerDetailDto`
gained:
```json
{
  "currentTierName": "string|null",
  "compositeScore": 0.0,
  "tierProgressPercent": 0.0,
  "openTicketCount": 0,
  "recentReviews": [ { "rating": 5, "comment": "string|null", "createdAt": "ISO8601",
                        "replyText": "string|null" } ]
}
```
Null-state semantics (**not** all-or-nothing as a group):
- No `LoyaltyMembership` at all → `currentTierName`/`compositeScore`/`tierProgressPercent` all
  null — "not enrolled," not a 0% bar.
- Membership exists, no tier assigned yet → `compositeScore` is a real number,
  `currentTierName`/`tierProgressPercent` both null — "enrolled, no tier yet," a distinct UI state.
- Membership at the top tier → `tierProgressPercent` null (no next tier to progress toward),
  `currentTierName`/`compositeScore` populated.
- `openTicketCount`/`recentReviews` are always populated (`0`/`[]`), never null.
- `tierProgressPercent` (when non-null) = `compositeScore / nextTier.minCompositeScore * 100`,
  clamped 0–100.

New, separate, lazily-fetched endpoint — not inlined into the DTO above, since profile history can
be arbitrarily long:
```
GET api/customers/{id}/profile-history?page=1&pageSize=50 -> 200 PagedResult<ConsumerProfileChangeDto>
```
`{id}` is the CRM `Customer.Id` (same id used everywhere else under `/customers`), not a
`consumerAccountId` — the backend resolves the link internally via the customer's
`LoyaltyMembership`. A customer never enrolled in loyalty gets `200` with an empty page
(`items: []`, `totalCount: 0`), never `404`. `ConsumerProfileChangeDto` shape is identical to the
consumer's own self-service history endpoint above.

---

**Mobile hand-off note:** the four consumer-facing groups above —
`api/consumer/profile*`, `api/consumer/loyalty/{tenantId}/tiers*`, `api/consumer/support*`, and
`api/consumer/reviews` — are the complete backend surface for the mobile screens described in the
plan's mobile-handoff section (`goofy-bubbling-naur.md` §4): profile editing, tier/progress
display, support-ticket creation, and review submission. None of these have a mobile UI yet — see
`.claude/logs/handoffs/623-to-mobile-codex.md`.

**⚠️ Known data-correctness gap for non-exempt roles (store_manager/network_manager) — see
`known-issues.md` KI-033.** The `pos_transactions` `store_scope` RLS policy silently narrows what
these endpoints (and the pre-existing `/overview`) can see to the caller's own granted locations,
which for store-migration can flip a genuinely-migrated customer to "not migrated," not just
undercount revenue.
