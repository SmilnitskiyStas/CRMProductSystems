# API Contracts

**Owner:** backend-developer + frontend-developer
**Updated:** 2026-06-03
**Base URL:** http://localhost:5000/api (dev)

## Auth Headers
Authorization: Bearer {jwt_access_token}
Tenant: derived from JWT payload (never from request body)

## Standard Response Shapes

### Success (200/201)
Returns typed DTO directly in body.

### Error
{ "error": "Human-readable message", "code": "OPTIONAL_CODE" }

### Pagination (future)
{ "items": [...], "total": N, "page": N, "pageSize": N }

## Implemented Endpoints

### Products
GET  /api/products          -> ProductDto[]
GET  /api/products/{id}     -> ProductDto | 404
POST /api/products          -> 201 ProductDto | 409 { error }
PUT  /api/products/{id}     -> ProductDto | 404
DEL  /api/products/{id}     -> 204 | 404

### ProductDto
{
  id, sku, name, description, category, unit,
  costPrice, salePrice, stockQuantity, reorderLevel,
  isActive, createdAt, updatedAt
}

## Auth (TASK-003)

POST /api/auth/login
  Body: { email, password }
  200: { accessToken, user: AuthUserDto }  +  Set-Cookie: refreshToken (HttpOnly)
  401: { error }

POST /api/auth/refresh
  Reads: Cookie refreshToken
  200: { accessToken, user: AuthUserDto }  +  rotated Set-Cookie
  401: { error }

POST /api/auth/logout  [Authorize]
  Reads: Cookie refreshToken
  204: (no body, cookie cleared)

GET /api/auth/me  [Authorize]
  200: AuthUserDto
  401

### AuthUserDto
{ id, email, fullName, role, tenantId, storeId }

### JWT Claims
sub: userId (Guid)
email: user email
http://schemas.microsoft.com/ws/2008/06/identity/claims/role: role
tenant_id: tenantId (Guid) — absent for provider users
store_id: storeId (Guid) — absent if not assigned

## Pending (v1 backlog)
- /api/stock/*
- /api/receipts/*
- /api/transfers/*
- /api/write-offs/*
- /api/stores/*
- /api/users/*
- /api/analytics/*
- /api/notifications/*
