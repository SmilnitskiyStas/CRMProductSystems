# TASK-320 — Client chooses contract signing method (physical / Вчасно)

**Agent:** backend-developer · **Date:** 2026-07-08 · **Status:** done

## Що зроблено
Клієнт тепер обирає спосіб підписання договору співпраці, коли угода в статусі
`awaiting_signature`: `"physical"` (нічого не робимо, крім запису вибору —
постачальник підтверджує отримання через існуючу «Позначити підписаним») або
`"vchasno"` (обовʼязковий email → одразу вивантажуємо PDF у Вчасно з цим
email як отримувачем, незалежно від наявної supplier-only ручної кнопки
«Надіслати у Вчасно»).

## Файли
- `SupplierAgreement.cs` — `SigningMethod`, `SigningEmail` (nullable).
- `AppDbContext.cs` — конфігурація колонок (varchar 20 / 255).
- Міграція `20260708170724_AddSigningMethodToSupplierAgreements` — чисто
  адитивна (2 nullable varchar), без змін RLS.
- `SupplierAgreementService.cs` — нові константи помилок
  `InvalidSigningMethodError`, `SigningEmailRequiredError`; новий метод
  `ChooseSigningMethodAsync` (client-side, поруч з `DownloadContractAsync`);
  `ToDto`/`ToDtoAsync`/`ToDtosAsync` тепер резолвять `SupplierLegalAddress`
  (лише для `awaiting_signature`/`active`, з кешем per-tenant у batch-мапері,
  за зразком `GetNameCachedAsync`). `SendToVchasnoAsync` не чіпали, окрім
  додавання `recipientEmail: null` в один виклик.
- `ISupplierAgreementService.cs` — сигнатура `ChooseSigningMethodAsync`.
- `CooperationDtos.cs` — `CooperationAgreementDto` +3 поля в кінці
  (`SigningMethod`, `SigningEmail`, `SupplierLegalAddress`); новий
  `ChooseSigningMethodDto(string Method, string? Email)`.
- `IVchasnoClient.cs` / `VchasnoClient.cs` — `UploadDocumentAsync` отримав
  `recipientEmail` параметр (форм-поле `email_recipient`, той самий
  speculative-naming застереження, що й `edrpou_recipient`).
- `MarketplaceCooperationController.cs` — новий ендпоінт
  `POST /api/marketplace/cooperation/{id}/signing-method`.

Не чіпали: `ApproveAsync`, `RejectAsync`, `RegenerateContractAsync`,
`MarkSignedAsync`, `TerminateAsync`, `SupplierCabinetCooperationController.cs`.

## Фінальна форма `CooperationAgreementDto`
```
Id, SupplierTenantId, ClientTenantId, SupplierName, ClientName, Status,
RequestMessage, RejectionReason, ContractNumber, HasContractFile,
VchasnoDocumentId, RequestedAt, DecidedAt, SignedAt, TerminatedAt,
SigningMethod, SigningEmail, SupplierLegalAddress
```

## Перевірка
- `dotnet build` (повне рішення) — 0 errors, 0 warnings.
- `dotnet test` — 645/645 green (жоден існуючий тест не конструював
  `CooperationAgreementDto` позиційно — правок не знадобилось).
- Міграція застосована до локальної dev БД (docker
  `crmproductsystems-postgres-1`, порт 5435, db `crm`) — колонки
  `SigningEmail`/`SigningMethod` підтверджені через `\d supplier_agreements`.
  (Локальний `dotnet run` API-процес тримав lock на Api.dll — застосував
  міграцію через `--startup-project ShelfGuard.Infrastructure`, той процес не
  чіпали.)
- Продакшн не чіпали (без деплою/SSH).
