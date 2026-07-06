# TASK-317 — Backend: cooperation agreements + contract PDF + Вчасно + orders + support tickets

**Agent:** backend-developer · **Date:** 2026-07-06 · **Status:** done

## Зроблено

- **Application** (`Features/Marketplace/`): `ISupplierAgreementService`/impl (заявка, approve з генерацією PDF та номером «ДС-{yyyy}-{NNN}», reject, regenerate, send-to-vchasno, mark-signed, terminate, download, contract-settings + upload підпису/печатки), `IMarketplaceOrderService`/impl (гейт «лише active agreement» → 403, валідація позицій minQty/maxQty/IsAvailable, снапшоти назви/ціни, номер «MP-{yyyy}-{NNN}», матриця переходів статусів), `ISupplierSupportService`/impl (тікети без угоди, party-check, статуси). DTOs у `Dtos/CooperationDtos.cs`.
- **Infrastructure**: `Documents/ContractPdfGenerator.cs` (QuestPDF 2024.10.4, шрифти DejaVu Sans з кирилицею — `Fonts/*.ttf`, CopyToOutputDirectory), `Integrations/Vchasno/VchasnoClient.cs` + `VchasnoClientFactory.cs` (per-tenant api_key з integration_configs, патерн FiscalServiceFactory). License QuestPDF Community у DI.
- **IntegrationService**: провайдер `vchasno` доданий у whitelist, api_key маскується на GET / merge на PUT (`VchasnoSecrets`, дзеркало PrroSecrets).
- **Api**: `MarketplaceCooperationController` (клієнт, `[Authorize]+[RequireModule("marketplace")]`), `SupplierCabinetCooperationController` (кабінет, SupplierCabinet policy + marketplace_supplier). Помилки `{error}`, гейт → 403, дублікат заявки → 409.
- **Тести** (+32): SupplierAgreementServiceTests (дублікат-гард, реквізити-гард, нумерація договорів, статусні переходи), MarketplaceOrderServiceTests (гейт, валідація, нумерація, матриця переходів), ContractPdfGeneratorTests (реальний PDF з українським текстом + зображення).

## Статус

- `dotnet build` — 0 errors; `dotnet test` — **639/639 passed**.
- Міграція TASK-316 не застосовувалась (за планом).

## Нотатки

- Вчасно: офіційні доки були недоступні з dev-середовища; форма запиту (POST /api/v2/documents, multipart `file`, Authorization: token) задокументована в `VchasnoClient.cs` з TODO — усе ізольовано в одному файлі.
- Termination reason зберігається в `rejection_reason` (без зміни схеми).
- Handoff: `.claude/logs/handoffs/317-to-318_frontend-developer.md` (усі ендпоінти + DTO shapes).
