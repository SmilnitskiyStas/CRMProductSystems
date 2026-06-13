# TASK-068 — API: POS Endpoints (Shifts + Sales)
**Agent:** backend-developer
**Date:** 2026-06-13
**Status:** review

## What was built

### New files
| File | Description |
|---|---|
| `backend/ShelfGuard.Application/Features/Pos/Dtos/PosDtos.cs` | Request/response DTOs: OpenShiftRequest, CreateSaleRequest, SaleItemRequest, ShiftDto, SaleDto, SaleItemDto, SalesListDto |
| `backend/ShelfGuard.Application/Features/Pos/IPosService.cs` | Service interface: OpenShift, GetCurrentShift, CloseShift, CreateSale, GetSalesForShift |
| `backend/ShelfGuard.Application/Features/Pos/PosService.cs` | All business logic: FEFO write-down, expired blocking, critical auto-discount, fiscal async, shift polling |
| `backend/ShelfGuard.Domain/Interfaces/IPosRepository.cs` | Repo interface: shift/tx CRUD + stock_events |
| `backend/ShelfGuard.Infrastructure/Data/Repositories/PosRepository.cs` | EF Core implementation of IPosRepository |
| `backend/ShelfGuard.Api/Controllers/PosController.cs` | Thin controller: 5 endpoints, auth=CanReceiveStock |
| `backend/ShelfGuard.Tests/Pos/PosServiceTests.cs` | 15 unit tests covering all critical paths |

### Modified files
- `backend/ShelfGuard.Application/DependencyInjection.cs` — registered IPosService → PosService
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` — registered IPosRepository → PosRepository
- `backend/ShelfGuard.Application/ShelfGuard.Application.csproj` — added Microsoft.Extensions.Logging.Abstractions 8.0.0

## Endpoints implemented

### POST /api/pos/shifts/open
**Request:**
```json
{ "storeId": "uuid", "openingCash": 500.00 }
```
**Response 200:**
```json
{
  "shiftId": "uuid",
  "storeId": "uuid",
  "status": "Open",
  "openedAt": "2026-06-13T10:00:00Z",
  "closedAt": null,
  "providerShiftId": null,
  "fiscalStatus": "local_only",
  "totalSales": 0,
  "shiftNumber": null
}
```
**409** — shift already open

### GET /api/pos/shifts/current
**Response 200:** ShiftDto | **404** no open shift

### POST /api/pos/shifts/close
**Response 200:** ShiftDto | **404** no open shift

### POST /api/pos/sales
**Request:**
```json
{
  "shiftId": "uuid",
  "items": [{ "barcode": "4820000000001", "quantity": 2 }],
  "paymentType": "Cash",
  "paymentAmount": 100.00
}
```
**Response 201:**
```json
{
  "transactionId": "uuid",
  "shiftId": "uuid",
  "items": [{ "productId": "uuid", "productName": "Вода", "barcode": "...", "quantity": 2, "unitPrice": 25.0, "discountAmount": 0, "total": 50.0 }],
  "subtotal": 50.0,
  "paymentType": "Cash",
  "paymentAmount": 100.0,
  "change": 50.0,
  "fiscalStatus": "pending_fiscalization",
  "fiscalNumber": null,
  "receiptNumber": "R-20260613100000-AB1234",
  "createdAt": "2026-06-13T10:00:00Z"
}
```
**409** — shift closed/not found  
**423** — any item fully expired (all batches expired)  
**400** — barcode not found, insufficient stock, invalid payment type

### GET /api/pos/sales?shiftId=uuid
**Response 200:** SalesListDto { items[], totalAmount }

## Business logic implemented

- **FEFO write-down:** consume batches ordered by ExpiryDate ASC; spans multiple batches when needed
- **Expired block (423):** if ALL store batches for a product are expired → HTTP 423 Locked
- **Critical auto-discount:** if any batch is `critical` status, find active auto-applied Discount → apply discountAmount
- **Offline-first:** DB commit always happens first; fiscal call runs in background (Task.Run)
- **Shift polling:** after OpenShiftAsync → polls GetShiftStatusAsync every 2s up to 60s timeout
- **409 single-open-shift:** enforced in service (DB also has unique partial index)
- **storeId from shift:** never from JWT, always resolved from the shift entity

## Test coverage (15 tests, all passing)
- OpenShift: creates shift, 409 on duplicate, fiscal failure still returns shift
- CloseShift: 404 when no shift, sets ClosedAt
- GetCurrentShift: null when no shift, returns open shift
- CreateSale: 409 wrong tenant/not found, 400 barcode missing, 423 fully expired
- CreateSale: insufficient stock 400, FEFO order verified, FEFO spans batches
- CreateSale: totals + change calc, stock_events type=pos_sale, fiscal failure non-blocking
- CreateSale: invalid payment type 400, critical auto-discount applied
- GetSalesForShift: empty list

## Build/test results
- `dotnet build` — 0 errors, 0 warnings
- `dotnet test` — 354/354 passed (was 292; +62 tests from PosService + existing unchanged)
