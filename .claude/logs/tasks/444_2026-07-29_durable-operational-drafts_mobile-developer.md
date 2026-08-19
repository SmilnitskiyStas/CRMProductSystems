# TASK-444 — Durable warehouse and production drafts

**Agent:** mobile-developer  
**Date:** 2026-07-29  
**Status:** transfer_draft_device_pass / receipt-create contract pending

## Delivered

- Added a reusable versioned AsyncStorage format for receipt, write-off, transfer, and production
  draft payloads with strict tenant+user ownership and per-operation/per-scope keys.
- Added explicit whitelist serialization. Auth tokens, QR/TOTP/recovery codes, and arbitrary
  fields cannot enter durable storage.
- Serialized writes so rapid edits cannot let an older write overwrite the latest draft.
- Corrupt, incompatible-schema, and wrong-owner records are removed fail-closed.
- Integrated restore/autosave, offline submit guards, explicit discard confirmation, failure

## Physical-device acceptance — 2026-07-29

- Transfer note autosave and visible offline preservation — **PASS**.
- Offline banner `Немає мережі. Чернетка збережена.` — **PASS**.
- Wi-Fi restored to its original ON state; mobile data remained OFF.
- Different user cannot see the manager draft — **PASS for non-disclosure**.
- Returning to the original owner restores the draft — **FAIL**; the foreign-owner load deleted
  the shared operation key. See
  `.claude/logs/reviews/bug-task444-owner-switch-deletes-draft_2026-07-29.md`.
- Explicit discard — **PASS**; disposable marker absent after cold restart.
- Write-off item draft — **not tested** because the form requires scanning a real product/batch.
- Production draft — **not tested** because no safe enabled production form/data was established.
- No write-off, transfer, production order, receipt, stock change, shift, or sale was submitted.

### Current-source owner restore retest

Manager transfer-note cold restore still failed: after force-stop and eventual authenticated
bootstrap, the same-owner transfer form was blank. The full manager → storekeeper → manager cycle
was stopped at that first failed assertion. No test marker remained visible and nothing was
submitted.

### Final focused device acceptance

Transfer note with incomplete destination/items now passes background preservation, same-owner
cold restore, manager → storekeeper non-disclosure, manager return restore, explicit discard, and
cold absence verification. No transfer was submitted. Receipt-create remains contract-blocked;
write-off/production draft paths were not covered by this focused retest.
  retention, confirmed-success clearing, and ambiguous-timeout/conflict states into the existing
  write-off, transfer, and production-order forms.
- Transfer submit re-fetches stock and fails with a Ukrainian conflict state when a referenced
  batch disappeared or quantity decreased. Production performs an immediate server refetch and
  revalidates that the selected recipe is still active. A failed pre-submit refetch is reported
  as a safe failed state because the mutation was not started. FEFO/allocation logic was not
  implemented on the client. Write-off items contain product IDs but no selected stock/batch IDs,
  so changed stock is enforced by the authoritative create endpoint and retained as an explicit
  conflict when the backend returns `409`; mobile does not invent batch allocation.
- Draft hook output now fails closed synchronously when tenant, user, operation, or scope changes,
  preventing a previous owner's restored payload/status from appearing during async hydration.
- The current mobile receipt area has no receipt-create form/API model; only processing and
  confirming an existing server receipt are present. This contract gap is in the handoff.

## Verification

- `npm run type-check`: PASS.
- `npm run lint`: PASS, 0 errors / 13 pre-existing warnings.
- `npm run test:ci`: PASS, 17 suites / 74 tests.
- Tests cover persistence/restore, schema and owner isolation, operation separation, field
  preservation, secret stripping, failed/uncertain/conflict retention, success clear primitive,
  rapid-write ordering, timeout classification, and transfer request shape without client FEFO.
  A hook regression covers immediate owner-context isolation.
- Android force-close and live backend acceptance: not run; remains under TASK-435.

## Files

- `mobile/features/operational-drafts/storage.ts`
- `mobile/features/operational-drafts/useOperationalDraft.ts`
- `mobile/features/operational-drafts/submission.ts`
- `mobile/features/operational-drafts/__tests__/*`
- `mobile/features/transfers/api/__tests__/transferApi.test.ts`
- `mobile/app/(app)/write-offs/create.tsx`
- `mobile/app/(app)/transfers/create.tsx`
- `mobile/app/(app)/production/index.tsx`

## Handoff

`.claude/logs/handoffs/444-to-backend-product_mobile-developer.md`
