# TASK-328 — Client legal entity picker on cooperation request form

**Agent:** frontend-developer
**Status:** done

## Change
Wired the backend's new optional `clientLegalEntityId` (TASK-327) into the
cooperation request modal, reusing the existing `legal-entities` feature.

## Files changed
- `frontend/features/marketplace/types.ts` — `CreateCooperationRequestBody` gained
  optional `clientLegalEntityId?: string`.
- `frontend/features/marketplace/components/CooperationRequestModal.tsx` —
  added a "Юридична особа (необовʼязково)" `<select>` populated via
  `useLegalEntities()` (from `@/features/legal-entities/hooks/useLegalEntities`),
  client-filtered to `isActive` entries. Option label: `legalName` + `(edrpou)`
  when present. Default option "— не вказано —". Selected id is passed as
  `clientLegalEntityId` (undefined when empty) in the mutate payload.
  - Empty-state: if the tenant has zero active legal entities, the select is
    hidden entirely and replaced with a small hint: "У вас ще немає
    зареєстрованих юридичних осіб — додати можна в Налаштування → Юридичні особи."

## Not changed
- `frontend/features/marketplace/api/marketplace-api.ts` — `createCooperationRequest`
  already forwards `body` unchanged to `api.post`, so the new field passes through
  with no code change needed.
- `frontend/features/legal-entities/*` — reused as-is (hook, types, api), no
  duplication.

## Build/Test
- `npx tsc --noEmit` in `frontend/`: 0 errors.
- No live browser test performed (no login credentials in this session) —
  manual verification pending in a separate pass.

## Review notes
- Empty-state UX (hint text vs. hiding field) is a judgment call — reviewer
  should confirm the Ukrainian copy/wording is acceptable.
- Inline styles match the existing modal's style (no Tailwind/shadcn in this
  file), kept consistent rather than introducing a new pattern.
