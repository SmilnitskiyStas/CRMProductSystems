# TASK-437 — Offline-safe cold bootstrap

**Date:** 2026-07-29  
**Agent:** mobile-developer (Codex)  
**Status:** fix_ready_for_device_retest

## Implemented

- Added retryable hydration state with a Ukrainian offline/retry screen.
- Preserved SecureStore token for transport, timeout, persistence-read, and server failures.
- Cleared private query cache and withheld staff identity/routes while bootstrap is unverified.
- Retained terminal cleanup for 401/403 and invalid refresh credentials/payloads.
- Retried `/auth/me` after connectivity recovery without weakening staff/consumer isolation.
- Preserved logout/session-epoch race protection.

## Verification

TypeScript passes; lint passes with 0 errors/13 existing warnings; Jest passes
(20 suites/90 tests); Android export passes. Controlled physical offline cold start remains pending.
