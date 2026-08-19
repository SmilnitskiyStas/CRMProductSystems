# TASK-444 — Owner-namespaced operational drafts

**Date:** 2026-07-29  
**Agent:** mobile-developer (Codex)  
**Status:** fix_ready_for_device_retest / receipt-create contract pending

## Implemented

- Namespaced each draft key by tenant, user, kind, and scope.
- Scoped load/save/clear queues and explicit discard to the current owner.
- Preserved a first owner's draft while another owner uses the same operation form.
- Added safe migration from the former shared versioned key only for its embedded owner.
- Kept foreign legacy records undisclosed and undeleted.
- Applied through the shared hook used by transfer, write-off, and production.

## Verification

TypeScript passes; lint passes with 0 errors/13 existing warnings; Jest passes
(20 suites/90 tests); Android export passes. Multi-owner physical retest remains pending.

## Same-owner restore follow-up

Found and fixed a second root cause: load validation rejected incomplete-but-valid form snapshots
that autosave had persisted, especially an empty transfer source/location around bootstrap. Draft
validation now permits incomplete strings without weakening submit validation. The shared hook also
flushes its latest sanitized snapshot on AppState background.

New hook-level integration tests exercise full mount/edit/write/unmount/process-reset/remount and
foreign-owner/owner-return cycles. Current verification: TypeScript pass; lint 0 errors/13 existing
warnings; Jest 20 suites/92 tests; Android export pass.
