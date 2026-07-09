# TASK-327 — Client legal entity on cooperation request + contract PDF

**Agent:** backend-developer
**Status:** done

## Change
Wired the already-migrated `SupplierAgreement.ClientLegalEntityId` (TASK-326)
through the Application layer: a client can now pick which of their own
registered legal entities (ТОВ/ФОП) is requesting cooperation, and the
generated contract PDF renders that legal entity's requisites alongside the
supplier's.

## Files changed
- `SupplierAgreementService.cs` — injected `ILegalEntityService`; `SubmitRequestAsync`
  now accepts `Guid? clientLegalEntityId`, validates it via `BelongsToTenantAsync`
  (new error `ClientLegalEntityNotOwnedError`), persists it on the new
  `SupplierAgreement`. `GenerateAndStoreContractAsync` fetches the `LegalEntity`
  via `ILegalEntityService.GetByIdAsync` when `ClientLegalEntityId` is set and
  passes its requisites into `ContractPdfData`; falls back to the existing
  tenant-display-name-only behavior when unset.
- `ISupplierAgreementService.cs` — `SubmitRequestAsync` signature extended with
  optional `clientLegalEntityId` param.
- `Dtos/CooperationDtos.cs` — `SubmitCooperationRequestDto(string? Message, Guid? ClientLegalEntityId = null)`.
- `IContractPdfGenerator.cs` — `ContractPdfData` extended with optional
  `ClientLegalName, ClientEdrpou, ClientIban, ClientBankName, ClientLegalAddress,
  ClientDirectorName, ClientIsVatPayer` (all default null/false — backward compatible).
- `ContractPdfGenerator.cs` — renders a new "РЕКВІЗИТИ КЛІЄНТА" table (same layout
  as the supplier's requisites table), only when `ClientLegalName` is set; the
  ЗАМОВНИК signature block now shows the legal name + director name when available.
- `MarketplaceCooperationController.cs` — passes `request.ClientLegalEntityId`
  through to `SubmitRequestAsync` (thin controller, no logic added).
- `SupplierAgreementServiceTests.cs` — constructor updated for the new DI param;
  added 3 tests (foreign legal entity rejected, own legal entity persisted,
  approve renders client requisites into `ContractPdfData`).

## Not changed
`SupplierCabinetCooperationController.cs` (supplier side) — no changes needed,
this feature is entirely client-side (client picks their own legal entity when
submitting the request).

## Build/Test
- `dotnet build`: 0 errors, 1 pre-existing unrelated warning.
- `dotnet test`: 648/648 passed (645 baseline + 3 new).

## Review notes
- PDF template change is additive/conditional (`if (data.ClientLegalName is { Length: > 0 })`)
  — existing contracts without a client legal entity render identically to before.
- `ILegalEntityService` and `ISupplierAgreementService` are both already
  registered as scoped in `ShelfGuard.Application/DependencyInjection.cs` — no DI
  changes required.
- Frontend (`CooperationRequestsTab.tsx` / request submission form) still needs
  a legal-entity picker wired to the new `ClientLegalEntityId` field — not part
  of this backend task.
