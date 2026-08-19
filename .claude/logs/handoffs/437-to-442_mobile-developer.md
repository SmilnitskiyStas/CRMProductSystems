# Handoff: TASK-437 to TASK-442

**Date:** 2026-07-29
**From:** mobile-developer
**To:** security-reviewer
**Task:** Review mobile refresh/session termination changes

## What was completed

Implemented authenticated-only single-flight refresh, exactly-once retry, centralized terminal
session cleanup, React Query cache clearing, resilient SecureStore deletion, and a session-epoch
guard against logout/refresh races. Seven suites and 24 tests pass.

## What to do next

1. Review `mobile/lib/api-client.ts` and `mobile/features/auth/session.ts`.
2. Verify no refresh path can loop or bypass terminal cleanup.
3. Verify no late refresh can resurrect a terminated session.
4. Verify query-cache clearing is sufficient for tenant/consumer isolation.
5. Review consumer behavior given the absence of a dedicated refresh/logout endpoint.
6. Recheck SecureStore and error-handling paths.

## Important context

- Login `401` without a Bearer token does not trigger refresh.
- Concurrent authenticated `401` responses share one raw Axios refresh call.
- The raw refresh call is outside the configured `apiClient`, so its own `401` is not intercepted.
- Route redirects remain owned by auth-layout guards.
- Android native cookie behavior is not yet verified because TASK-435 is blocked.

## Risks / Blockers

- Native Axios `withCredentials` cookie persistence must be tested on Android.
- Consumer tokens have no dedicated refresh/logout API; an authenticated consumer `401` attempts
  the shared refresh endpoint and cleanly terminates if it fails.

## Files to review

- `mobile/lib/api-client.ts`
- `mobile/features/auth/session.ts`
- `mobile/features/auth/store.ts`
- `mobile/app/_layout.tsx`
- `mobile/app/(app)/profile/index.tsx`
- `mobile/app/(consumer)/account.tsx`
- `mobile/lib/__tests__/api-client.test.ts`
- `mobile/features/auth/__tests__/store.test.ts`

## Definition of done

- No unresolved critical/high security finding.
- Any medium/low finding is logged with a follow-up task.
- Device-only cookie behavior remains assigned to TASK-435, not inferred from unit tests.
