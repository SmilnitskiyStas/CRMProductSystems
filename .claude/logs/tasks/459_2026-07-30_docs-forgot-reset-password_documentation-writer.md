# TASK-459: Documentation — forgot/reset-password flow

**Agent:** documentation-writer
**Date:** 2026-07-30
**Status:** done — docs only, no code touched. No blocker.

## Context

Part B closeout of `C:\Users\stass\.claude\plans\reflective-churning-quail.md` — TASK-455
(schema), TASK-456 (backend/worker), TASK-457 (frontend) are all done and verified. This task
documents the shipped flow: `api-contracts.md`, a `database-schema.md` accuracy check (not a
rewrite), a new ADR in `decisions.md`, and a cross-reference in `blocked.md`.

## Done

### `.claude/docs/api-contracts.md`
Added `POST /api/auth/forgot-password` and `POST /api/auth/reset-password` to the `### Auth`
code block, after `change-password`, in the file's existing mixed UA/EN style. Content taken
verbatim from TASK-456's log and cross-checked directly against `AuthController.cs`/
`AuthService.cs`/`Program.cs` (rate-limit policies, 204/400 shapes, the generic
`"Invalid or expired reset link."` non-enumeration text). Added a short paragraph after the code
block documenting the reset-link URL shape (`{Frontend__BaseUrl}/reset-password?token=...`,
single-use, 30 min TTL) and pointing at ADR-024 for the outbox delivery mechanism. Header
`**Updated:**` bumped 2026-07-27 → 2026-07-30.

### `.claude/docs/database-schema.md`
Verified, did not duplicate. TASK-455 had already:
- Fixed the "Documented exceptions" table to exactly 3 rows (`users`, `refresh_tokens`,
  `password_reset_tokens`), with `notification_settings` correctly noted as removed (TASK-360)
  rather than silently dropped from the history.
- Added a `## TASK-455` section documenting the table, the fail-open `EXISTS`-through-`users`
  rationale, and the `provider_bypass` `IN (...)` vs. the stale singular form in the `RLS
  Template` section.

Cross-checked both against the live migration
(`backend/ShelfGuard.Infrastructure/Migrations/20260730090415_AddPasswordResetTokens.cs`) —
byte-for-byte match, no inaccuracy found. Only change made: bumped the stale header
`**Updated:** 2026-07-27` → `2026-07-30` to match the date content was actually last added
(TASK-455 edited the file but never bumped this field).

### `.claude/docs/decisions.md` — new ADR-024
Added `## ADR-024: Forgot/reset-password flow — outbox reuse, third fail-open RLS exception,
env-var frontend URL, 400 not 401` immediately above ADR-023 (current max was ADR-023, dated
2026-07-26). Five decision points, each verified directly against shipped code before writing
(not copied from the plan uncritically):
1. Delivery reuses the existing Postgres outbox + `dispatchTargeted()` (ADR-018/019) — no new
   C# BullMQ producer. Confirmed `AuthService.ForgotPasswordAsync`'s `INotificationRepository
   .EnqueueAsync` call and the worker's `TARGETED_EVENT_CHANNELS["auth.password_reset_requested"]
   = ["email", "telegram"]` entry directly.
2. `password_reset_tokens` is the 3rd fail-open RLS exception, not a 4th — same shape as
   `refresh_tokens`, `notification_settings` already removed from the list independently.
3. Email-primary/Telegram-fallback is a recorded product decision (`AskUserQuestion`), with an
   explicit, tracked dependency on TASK-260 (Resend DNS) for the email leg; Telegram has no such
   dependency.
4. `Frontend__BaseUrl` read via `Environment.GetEnvironmentVariable`, not `IConfiguration` —
   confirmed `ShelfGuard.Application.csproj` still has no `Microsoft.Extensions.Configuration`
   reference, and the pattern is copied verbatim from `TelegramLinkService.cs`.
5. `reset-password` returns 400, not 401, unlike `2fa/verify` — it authenticates nothing and
   issues no tokens, same category as `change-password`.

Header `**Updated:**` bumped 2026-07-27 → 2026-07-30.

### `.claude/tasks/blocked.md`
Added a short paragraph under the existing `TASK-260` entry cross-referencing this flow: the
email channel depends on the same Resend DNS blocker; the Telegram fallback (already-linked
accounts) does not and works today. Did not touch the entry's `Status`/`Updated` fields (the
blocker itself is unchanged) and did not create a new `known-issues.md` entry — per brief, this
is a new dependent of an already-tracked blocker, not a new problem.

## Verification

Read every changed source directly before writing docs (`AuthController.cs`, `AuthService.cs`,
`Program.cs`, `notification-dispatch.job.ts`, the `AddPasswordResetTokens` migration) rather than
trusting the task logs' prose alone — no discrepancy found between the three logs (TASK-455/456/
457) and the actual shipped code. No build/test run — no code changed.

## Not in scope (per brief)

- No code changes (backend/frontend/worker).
- No security review — TASK-458, parallel agent.
- No new `known-issues.md` entry — deliberate, see above.

## Files

- `.claude/docs/api-contracts.md` (new Auth endpoints + header date)
- `.claude/docs/database-schema.md` (header date only — content already correct)
- `.claude/docs/decisions.md` (new ADR-024 + header date)
- `.claude/tasks/blocked.md` (TASK-260 cross-reference paragraph)
- `.claude/tasks/current.md` (new TASK-459 entry)

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
