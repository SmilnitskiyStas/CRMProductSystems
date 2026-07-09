# TASK-326 — Client legal entity on SupplierAgreement

**Agent:** database-engineer
**Status:** done

## Change
Added nullable `ClientLegalEntityId` (Guid?) to `SupplierAgreement` so a client can
indicate which of their registered legal entities (ТОВ/ФОП, see TASK-321) is
requesting cooperation with a supplier in the B2B marketplace flow.

## Files changed
- `backend/ShelfGuard.Domain/Entities/SupplierAgreement.cs` — added `ClientLegalEntityId` property.
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` — FK to `LegalEntity`
  (`OnDelete(SetNull)`, `IsRequired(false)`), index on `ClientLegalEntityId`. Mirrors
  the existing `Location.LegalEntityId` / `User.LegalEntityId` pattern.

## Migration
`20260709162957_AddClientLegalEntityToSupplierAgreement`
- Adds `ClientLegalEntityId uuid NULL` to `supplier_agreements`
- Index `IX_supplier_agreements_ClientLegalEntityId`
- FK `FK_supplier_agreements_legal_entities_ClientLegalEntityId` → `legal_entities(Id)`, `ON DELETE SET NULL`
- No unrelated changes; no RLS changes needed.

Applied to local dev DB (Docker `crmproductsystems-postgres-1`, port 5435).

## Build/Test
- `dotnet build`: 0 errors, 1 pre-existing warning (unrelated).
- `dotnet test`: 645/645 passed (incl. all Marketplace/SupplierAgreement tests) — no test changes required since the field is nullable/optional.

## Next
Backend (service/DTO) and frontend (CooperationRequestsTab / request form) work needed
to let the client actually pick a legal entity when submitting a cooperation request —
not part of this schema task.
