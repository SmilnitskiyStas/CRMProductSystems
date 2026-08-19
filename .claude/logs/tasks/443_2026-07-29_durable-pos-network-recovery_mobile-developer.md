# TASK-443: Durable POS cart and network recovery

**Date:** 2026-07-29  
**Agent:** mobile-developer (Codex)  
**Status:** review_pending_device

## Physical-device acceptance — 2026-07-29

`store_manager` and `storekeeper` both reached POS, but the seeded environment reports
`Зміна не відкрита`. Opening a shift is a server-side business mutation and was prohibited.
Therefore cart/customer persistence, force-stop restoration, and duplicate-submit behavior are
**not tested** with the exact blocker “no existing active POS shift”. No shift or sale was created.

## Contract conclusion

The existing `POST /api/pos/sales` contract has no idempotency key and no reconciliation lookup by
client request ID. Mobile therefore does not automatically retry an ambiguous timeout. It preserves
an explicit `uncertain` draft and requires shift reconciliation. Backend follow-up:
`.claude/logs/handoffs/443-to-backend_mobile-developer.md`.

## Implemented

- Added Expo-compatible AsyncStorage persistence for active shift, enriched cart, quantities,
  customer/membership selection, redemption amount, payment type, and entered cash.
- Added schema version validation, corrupt-snapshot rejection, and strict tenant+user ownership.
- Clears in-memory state immediately when the authenticated owner changes.
- Added a whitelist serializer; rotating loyalty QR/code values, TOTP/recovery/challenge values,
  and auth tokens are not part of the durable schema and are stripped from contaminated objects.
- Restores navigation/process-kill drafts. A persisted `pending` submission becomes `uncertain`
  because commit outcome cannot be inferred after restart.
- Added NetInfo offline UI and disables submission while offline.
- Added process-level single-flight locking so double taps share one request.
- Serialized/coalesced persistence writes so rapid scans cannot leave an older snapshot as the
  final durable value.
- A restored non-empty cart cannot be silently rebound to a different shift; it becomes a blocking
  conflict requiring reconciliation and explicit discard.
- Editing cart/customer cannot clear `uncertain` or `conflict`; the cashier must explicitly confirm
  reconciliation before discarding the draft.
- Added explicit `pending`, `failed`, `completed`, `conflict`, and `uncertain` states.
- `409` becomes a retained conflict draft; deterministic failures retain the draft; timeout/no
  response becomes uncertain without retry.
- Durable and in-memory sale draft is cleared only after a confirmed successful API response.

## Verification

- `npx tsc --noEmit`: PASS
- `npm run lint`: PASS, 0 errors (19 pre-existing warnings after TASK-443 warning cleanup)
- `npm run test:ci`: PASS, 13 suites / 61 tests
- Covered persistence/restore, corrupt/version mismatch, wrong tenant, wrong user, secret absence,
  interrupted pending restore, failed preservation, confirmed-success clear, concurrent submit,
  `409`, timeout/no-response ambiguity, deterministic failure, rapid-write ordering, cross-shift
  conflict, and uncertain-state preservation through edits.
- `npx expo-doctor`: package version issue corrected by pinning AsyncStorage 2.2.0; remaining
  pre-existing `.expo/` gitignore check is unrelated to TASK-443.

## Pending acceptance

- Android force-close/process-kill restore.
- Real connectivity drop during submit and current-shift reconciliation UX.
- Real device double-tap/rotation/navigation checks.

These remain under TASK-435. TASK-443 must not be marked done until device acceptance passes.
