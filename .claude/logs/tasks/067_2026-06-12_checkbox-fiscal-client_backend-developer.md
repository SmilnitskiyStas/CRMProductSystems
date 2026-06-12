# TASK-067 — Infrastructure: Checkbox fiscal client (IFiscalService)

**Agent:** backend-developer · **Date:** 2026-06-12 · **Status:** done
**ADR:** ADR-012 (Checkbox behind IFiscalService), ADR-011 (offline-first flow unchanged)

## What was built

### Application (provider-agnostic — never sees Checkbox shapes)
- `ShelfGuard.Application/Features/Pos/Fiscal/IFiscalService.cs` — PingAsync, OpenShiftAsync,
  CloseShiftAsync, CreateReceiptAsync, GetReceiptStatusAsync
- `FiscalModels.cs` — FiscalReceiptRequest/Item/Payment (decimal UAH), FiscalReceiptResult
  (ProviderReceiptId, Status, FiscalNumber, FiscalDate, TotalAmount, TaxUrl), FiscalShiftResult,
  FiscalHealthResult, enums (receipt: PendingFiscalization/Created/Done/Error/Cancelled;
  shift: PendingFiscalization/Created/Opening/Opened/Closing/Closed)
- `FiscalProviderException.cs` — ProviderCode, HttpStatus, IsTransient (retry-job hint)
- `NoopFiscalService.cs` — everything → pending_fiscalization when PRRO__PROVIDER unset

### Infrastructure (`Integrations/Prro/` — isolation per ADR-012)
- `CheckboxFiscalClient.cs` — typed HttpClient implementing IFiscalService:
  - signin via PIN (preferred, X-License-Key required) or login/password → bearer token
  - token cached in singleton `CheckboxTokenStore` (SemaphoreSlim-deduped signin);
    re-auth + single retry on 401 **and** on 403 `{"message":"Not authenticated"}` —
    live API uses the latter for missing/expired tokens
  - money UAH → integer kopecks; quantity → integer thousandths (1 шт = 1000, 2.25 кг = 2250)
  - LocalReceiptId (pos_transaction uuid) sent as Checkbox receipt `id` → idempotent re-submits
  - error mapping: `{code,message}` / 422 `{detail}` → FiscalProviderException;
    5xx/timeout/network → IsTransient=true; Ping never throws
- `PrroOptions.cs` — bound from `PRRO` section (env PRRO__PROVIDER/BASEURL/LICENSEKEY/
  CASHIER__LOGIN/CASHIER__PASSWORD/CASHIER__PINCODE), TimeoutSeconds default 30
- `CheckboxTokenStore.cs`
- DI (`DependencyInjection.cs`): PRRO:PROVIDER=checkbox → AddHttpClient<IFiscalService,
  CheckboxFiscalClient> (BaseAddress + timeout from options); otherwise NoopFiscalService —
  app runs without any PRRO config.

### Config plumbing
- `Program.cs`: loads optional `appsettings.Secrets.json` (gitignored) then re-adds env vars
  so PRRO__* env still wins in Docker/prod
- Local secrets: `backend/ShelfGuard.Api/appsettings.Secrets.json` created with the real test
  license key (verified gitignored via `git check-ignore`)
- `.env.production.example` + `docker-compose.production.yml` api service: PRRO__* wired
  (placeholders only, no secrets)

### Tests — `ShelfGuard.Tests/Pos/` (fake HttpMessageHandler, no network)
27 tests: PIN + login/password signin flows, token caching, 401 re-auth retry,
403-Not-authenticated re-auth retry, genuine 403 NOT retried, shift open/close mapping,
receipt mapping (kopecks, thousandth quantities, barcode omission, CASH/CASHLESS),
receipt status polling, error mapping (403 code, 5xx transient, timeout transient),
kopeck/quantity rounding theories, Noop behavior.

**Build:** green (0 warnings) · **Tests:** 290/290 pass (was 263 + 27 new)

## API contract verification (OpenAPI v2.99.4, fetched live)
- Spec: `https://api.checkbox.in.ua/api/openapi.json` (NOT /api/v1/openapi.json)
- `POST cashier/signin {login,password}` / `POST cashier/signinPinCode {pin_code}` (+X-License-Key)
  → `{access_token, token_type:"bearer"}`
- `POST shifts` (202, X-License-Key + bearer), `POST shifts/close` (202) → `{id, serial, status}`
- `POST receipts/sell {id?, cashier_name?, goods:[{good:{code,name,price,barcode?},quantity}],
  payments:[{type:CASH|CASHLESS,value}]}` (201) → ReceiptModel `{id, serial, status, fiscal_code,
  fiscal_date, total_sum, tax_url}`; `GET receipts/{id}` for polling
- price = kopecks per quantity 1000; quantity ×1000; payment value = kopecks

## Live smoke test (api.checkbox.in.ua, 2026-06-12)
1. ⚠️ **`dev-api.checkbox.in.ua` does NOT resolve (DNS NXDOMAIN)** — working test host is
   `api.checkbox.in.ua`. Docs (integrations.md, access.md) corrected; PrroOptions default updated.
2. `GET /api/v1/cash-registers/info` with X-License-Key only → **200**:
   `{"fiscal_number":"TEST582378","is_test":true,"has_shift":false,"number":1,
   "address":"УКРАЇНА, М.КИЇВ ГОЛОСІЇВСЬКИЙ Р-Н, Тестова, 41а",...}` — license key valid,
   register matches the cabinet data. (PingAsync uses exactly this endpoint.)
3. `POST /cashier/signinPinCode {"pin_code":"0000"}` + license key → **403**
   `{"code":"cashier.invalid_credentials","message":"Невірний пінкод"}`
4. `POST /cashier/signin {"login":"nologin","password":"nopass"}` → **403**
   `{"code":"cashier.invalid_credentials","message":"Невірний логін або пароль"}`
5. `POST /shifts` with license key but no bearer → **403**
   `{"message":"Not authenticated","code":null}` — drove the extended re-auth trigger.

## Blocker — RESOLVED 2026-06-12
Cashier creds received (test cashier «Тестовий касир», Старший касир — see
.claude/private/access.md §«Касир»). PINCODE/LOGIN/PASSWORD added to the gitignored
`backend/ShelfGuard.Api/appsettings.Secrets.json`. Full live e2e executed — see below.

## Live e2e (with cashier creds) — 2026-06-12, api.checkbox.in.ua — ALL GREEN

**Harness:** new `ShelfGuard.Tests/Pos/CheckboxLiveE2ETests.cs` — drives the real
`CheckboxFiscalClient` (not raw curl) against the live test register. Gated: passes
trivially unless env `PRRO_LIVE_E2E=1`, so CI / normal `dotnet test` never touch the
network. Config: PRRO__* env vars win, fallback to `appsettings.Secrets.json` (found by
walking up from the test bin dir). Run:
`PRRO_LIVE_E2E=1 dotnet test --filter "FullyQualifiedName~CheckboxLiveE2E"`

Results (single run, total ~6 s; poll interval 2 s, timeout 60 s):

| Step | Result |
|---|---|
| a. PingAsync (X-License-Key only) | reachable=true, fiscal_number=**TEST582378**, is_test=**true**, has_shift=false |
| b. Cashier signin | PIN signin (`cashier/signinPinCode`) succeeded on first try — bearer token issued; login/password fallback not needed |
| c. Open shift | `POST shifts` → 202, shift id **60f890d6-9f1c-4acc-8d86-c95849df4fa5**, serial 1, status **CREATED** → polled `GET shifts/{id}` → **OPENED** on poll #2 (~2 s), opened_at 2026-06-12T19:56:42Z |
| d. Sell receipt | «Тестовий товар» ×1 шт @ 5.50 грн, CASH 5.50; LocalReceiptId (uuid) sent as receipt id and **honored by Checkbox** (provider id == our uuid **928f04f3-6ac3-4c8a-8da8-a16be8a1316b**) → status CREATED |
| e. GetReceiptStatusAsync poll | **DONE** on poll #2 (~2 s): fiscal_code **TEST-KcEsEF**, fiscal_date 2026-06-12T19:56:45Z, total 5.50 UAH (kopeck round-trip correct), tax_url `https://cabinet.tax.gov.ua/cashregs/check?id=TEST-KcEsEF&date=20260612&...&fn=TEST582378&sm=5.50&mac=...` |
| f. Close shift (Z-report) | `POST shifts/close` → status **CLOSING** → polled → **CLOSED** on poll #1, closed_at 2026-06-12T19:56:47Z |

**Client fix made during e2e prep:** `IFiscalService` had no way to poll shift state —
Checkbox opens/closes shifts asynchronously (CREATED→OPENED, CLOSING→CLOSED), so polling
is mandatory. Added `GetShiftStatusAsync(providerShiftId)` to `IFiscalService`,
`CheckboxFiscalClient` (`GET shifts/{id}`) and `NoopFiscalService`, + unit test.
No other client bugs surfaced — field names, status mapping, kopecks/quantity units and
idempotency id all verified correct against the live API.

**Build:** green (0 warnings) · **Tests:** 292/292 pass (290 + GetShiftStatus unit + live e2e)

**Note for TASK-068:** shift open/close endpoints must poll `GetShiftStatusAsync` (or let
the worker do it) — the 202 response status is CREATED/CLOSING, not the final state.

## Handoff
TASK-068 (POS endpoints) can consume `IFiscalService` directly; inject it in the sale flow as
fire-and-forget/async step — sale commit must never await fiscalization (ADR-011).
TASK-069 retry job: use `FiscalProviderException.IsTransient` to decide retry vs. mark error.
