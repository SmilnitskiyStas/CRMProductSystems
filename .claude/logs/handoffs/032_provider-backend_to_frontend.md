# Handoff: Provider Panel Backend → Frontend

**From:** backend-developer (TASK-032)
**To:** frontend-developer (TASK-033)
**Date:** 2026-06-06

## Ready to connect

### Implemented endpoints (all require `Authorization: Bearer {provider_jwt}`)

```
GET    /api/provider/tenants
GET    /api/provider/tenants/:id
PUT    /api/provider/tenants/:id/plan      Body: { plan: "basic"|"standard"|"enterprise"|"trial" }
PUT    /api/provider/tenants/:id/modules   Body: { modules: string[] }
POST   /api/provider/tenants/:id/impersonate  → { accessToken, tenantName, tenantId }
DELETE /api/provider/tenants/:id/impersonate  → 204
GET    /api/provider/health
GET    /api/provider/logs?limit=100
```

### Key DTOs

**TenantSummaryDto:**
```json
{
  "id": "uuid",
  "name": "string",
  "slug": "string",
  "plan": "basic|standard|enterprise|trial",
  "modules": ["shelf_manager", "crm", "notifications", "auto_order"],
  "isActive": true,
  "createdAt": "ISO",
  "userCount": 12,
  "storeCount": 3,
  "expiredBatchCount": 5
}
```

**ProviderHealthDto:**
```json
{
  "totalTenants": 10,
  "activeTenants": 8,
  "totalUsers": 150,
  "totalExpiredBatches": 24,
  "timestamp": "ISO"
}
```

**ImpersonateResponse:**
```json
{
  "accessToken": "eyJ...",
  "tenantName": "ТОВ Свіжа Їжа",
  "tenantId": "uuid"
}
```

### Impersonation flow
1. Provider clicks "Увійти як клієнт" → `POST /api/provider/tenants/:id/impersonate`
2. Frontend stores the received `accessToken` temporarily (e.g., `sessionStorage` or Zustand)
3. Swap the API client's Bearer token to the impersonation token
4. Show a banner: "Ви в режимі перегляду: {tenantName}" with "Вийти" button
5. On exit → `DELETE /api/provider/tenants/:id/impersonate` + restore original token

### Access guard
Page `/provider` must only be rendered for `me.role === "provider"`. Redirect others to `/dashboard`.

### Modules whitelist (for checkbox UI)
`shelf_manager`, `crm`, `notifications`, `auto_order`, `iot`, `cv_camera`

### Plans list (for select/radio UI)
`basic`, `standard`, `enterprise`, `trial`
