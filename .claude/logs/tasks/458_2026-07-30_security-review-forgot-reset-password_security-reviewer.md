# TASK-458: Security review — forgot/reset-password (staff web)

**Agent:** security-reviewer
**Date:** 2026-07-30
**Status:** done — **verdict: NOT clear to ship as-is.** 1 HIGH finding (live reset token
persisted where any same-tenant user can read it), 1 MEDIUM (no per-user throttle behind
KI-014's already-ineffective per-IP limiter). All other checklist items: OK. Read-only audit,
no code changed.

## Scope

Read TASK-455/456/457 logs, the plan (`reflective-churning-quail.md` §"Security review
(TASK-458)"), then the code directly — `AuthService.cs`, `AuthController.cs`, `Program.cs`,
`JwtService.cs`, the migration + `RlsCrossTenantIntegrationTests.cs` +
`database-schema.md`, `PasswordResetTokenRepository.cs`/`RefreshTokenRepository.cs`,
`PasswordValidator.cs`, the frontend reset-password surface, and the worker's
`notification-dispatch.job.ts`/`notification-log.ts`. Re-ran `dotnet build ShelfGuard.sln`
(0 warnings/0 errors) and `npx tsc --noEmit` in `worker/` (clean) before writing this — no
code touched, so TASK-456's 1220/1220 `dotnet test` figure stands.

## Checklist verdicts

### 1. Token entropy/generation — OK

`AuthService.GenerateResetToken()` (`AuthService.cs:611-612`):
`Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))` — 64 bytes/512 bits from
`System.Security.Cryptography.RandomNumberGenerator` (CSPRNG), byte-for-byte the same call
`JwtService.GenerateRefreshToken()` (`JwtService.cs:131-134`) uses for its raw half. Hashing
via `_jwt.HashToken()` = unsalted `SHA256.HashData` (`JwtService.cs:197-201`) — correct choice
for a 512-bit-entropy secret (unlike a password, brute-forcing/rainbow-tabling this space is
infeasible regardless of hash speed); same convention already used for refresh tokens and TOTP
recovery codes. No issue.

### 2. Forgot-password generic response / timing side-channel — OK (low, pre-existing pattern)

`AuthController.ForgotPassword` (`AuthController.cs:218-227`) always returns `204`, unconditionally.
`AuthService.ForgotPasswordAsync` (`AuthService.cs:311-369`): unknown/inactive email → 1 SELECT +
in-memory log, returns immediately (no hash op, no DB write). Known active email → 1 SELECT + 1
bulk `UPDATE` (commits via `InvalidateActiveTokensAsync`) + RNG + hash + 1 combined INSERT round
trip (token + activity log + outbox row, flushed by `EnqueueAsync`'s own `SaveChangesAsync`). This
is a real, measurable timing asymmetry — larger than `LoginAsync`'s own (which only skips a single
password-hash-verify call for an unknown email, no DB writes on that branch either way,
`AuthService.cs:70-76`). Same *category* of accepted, pre-existing behavior already in this
codebase (`LoginAsync` doesn't equalize unknown-vs-known-email timing either — only the
locked-out-vs-wrong-password branches are deliberately equalized, `AuthService.cs:81-87`), just
larger in magnitude here due to the extra DB round trips. Not flagging as a blocker: the response
body is byte-identical either way, and exploiting a few-millisecond DB-write-count timing
difference over a real network needs a fairly sophisticated, repeated-sampling attacker — but it's
worth being aware of if this bar ever gets tightened.

### 3. Rate-limit assignment — policy correct, but MEDIUM gap given KI-014

Confirmed `"auth-forgot-password"` registered in `Program.cs:125-133` (5/min/IP, same shape as
`"public-leads"`) and applied via `AuthController.cs:220`
(`[EnableRateLimiting("auth-forgot-password")]`). `/api/auth/reset-password` confirmed
`[EnableRateLimiting("auth-login")]` (`AuthController.cs:232`, 10/min/IP,
`Program.cs:90-98`) — matches the brief.

However, `.claude/docs/known-issues.md` KI-014 (lines 83-98) already documents, confirmed live,
that per-IP rate limiting **does not work in production** (the hosting provider's edge doesn't
preserve client IPs — 15 parallel wrong logins all return 401 in prod). For login/2FA, the
IP-independent backstop that actually works is **per-account lockout**. `ForgotPasswordAsync` has
**no equivalent** — `InvalidateActiveTokensAsync` only invalidates the prior token before minting a
new one; it does not throttle request *frequency* for a given user/email. Since Telegram delivery
already works today (unlike email, still blocked on TASK-260), an attacker who knows/guesses any
user's email can trigger unlimited forgot-password notification sends to that person's linked
Telegram, with the per-IP limiter providing no real backstop in production per KI-014 — a
notification-spam/harassment vector, plus repeated DB writes server-side per request (minor
resource cost on its own). This is exactly the class of gap the brief asked about. **Recommend:**
add a per-user (not per-IP) cooldown in `ForgotPasswordAsync` — e.g. skip re-issuing if the most
recent token for this user was created within the last ~60s, independent of what
`InvalidateActiveTokensAsync` already does. MEDIUM severity — no tenant/account boundary is
crossed, and the token itself stays single-use/30-min-TTL, but it's a real, currently-unmitigated
gap and KI-014 is already a *confirmed*, not theoretical, production condition.

### 4. RLS fail-open exception + test/doc coverage — OK

Migration (`20260730090415_AddPasswordResetTokens.cs:73-89`) matches the described shape exactly:
`tenant_isolation` fail-open branch only fires when `app.tenant_id` is NULL (pre-auth); once a
tenant session var is set, it's a normal `EXISTS`-through-`users` tenant match — not a broader
bypass than `refresh_tokens`' existing policy (verified same shape, only table/column names
differ). `provider_bypass`/`worker_bypass` present and correctly scoped.

`RlsCrossTenantIntegrationTests.cs:290` — `allowedFailOpen` is exactly
`{ "users", "refresh_tokens", "password_reset_tokens" }`; the stale `notification_settings`
reference is gone from the assertion text (now correctly explained in the doc-comment at
lines 259-283 as removed by TASK-360, not silently dropped). `AllForceRlsTables_...`
(lines 218-257) auto-discovers every FORCE-RLS table by querying `pg_policies`/`pg_class` directly
— needed no edit and already covers the new table.

`.claude/docs/database-schema.md:72-83` — exceptions table now lists exactly 3 rows
(`users`/`refresh_tokens`/`password_reset_tokens`), with `notification_settings`'s removal
explicitly explained rather than silently dropped from history. New `## TASK-455` section
(lines 632-648) documents the table and fail-open rationale accurately. Confirmed directly, not
just trusting the task log's claim.

### 5. Lockout-clear + refresh-revocation correctness — OK

`ResetPasswordAsync` (`AuthService.cs:371-410`): token validity → owner validity →
`PasswordValidator.Validate` (all three can short-circuit with an error) → **only then**
`user.ChangePassword(...)` + `user.ResetLockout()` + `_users.Update(user)` → `token.MarkUsed()` →
`_refreshTokens.RevokeAllForUserAsync(...)` → `ActivityLog` → one final
`_passwordResetTokens.SaveChangesAsync(ct)`. Confirmed:
- `RefreshTokenRepository.RevokeAllForUserAsync` (`RefreshTokenRepository.cs:28-36`) loads tracked
  entities and calls `.Revoke()` on each but never calls `SaveChangesAsync` itself — genuinely
  deferred, as the code comment claims.
- `PasswordResetTokenRepository.GetActiveByHashAsync` (`PasswordResetTokenRepository.cs:21-26`)
  runs a normal (not `.AsNoTracking()`) query, so the returned `token` is tracked by the same
  `AppDbContext` — `token.MarkUsed()`'s mutation is picked up by the final `SaveChangesAsync()`
  with no separate `Update()` call needed.
- Since every write in this method (`_users`, `token.MarkUsed()`, revoked refresh tokens, the
  activity log) shares one `AppDbContext` and only ever gets flushed by that one trailing
  `SaveChangesAsync`, there is no code path where `MarkUsed()` "sticks" without the password change
  (or vice versa) — an exception anywhere in between means nothing commits, not a partial state.

### 6. Token-in-URL / Referer-leak (frontend) — OK, nothing found on this surface

Read `ResetPasswordForm.tsx`, `ResetPasswordCard.tsx`,
`app/(auth)/reset-password/page.tsx`, `AuthLogo.tsx`, and `app/(auth)/layout.tsx` in full. No
external-origin resources anywhere (no cross-origin `<img>`/`<link>`/`<script>`; the only links
are same-origin Next.js `<Link href="/login">` / `<Link href="/">`). No `console.log` of the URL
or token anywhere in these files. The actual request
(`authApi.resetPassword` → `api.post("/api/auth/reset-password", { token, newPassword })`,
`features/auth/api/auth.ts:61-62`) sends the token in the POST body, not as a query string, so
network tooling/proxies logging request *paths* wouldn't see it either. Grepped all of
`frontend/app` for analytics/tracking scripts (`gtag`, `googletagmanager`, `posthog`, `sentry`,
`hotjar`, `clarity`, `mixpanel`, `next/script`) — zero matches anywhere in the app, so no global
script could echo the full URL (with query string) via its own network calls or a Referer header
on this or any other page. Verdict: no Referer-leak vector found for this feature.

### 7. Enumeration via reset-password — OK

`PasswordResetTokenRepository.GetActiveByHashAsync` filters
`UsedAt == null && ExpiresAt > DateTime.UtcNow` (`PasswordResetTokenRepository.cs:21-26`) — a
nonexistent hash, an already-used token, and an expired token all produce the identical `null`
result → the same `GenericResetError` ("Invalid or expired reset link.") from
`AuthService.cs:373-375`. Owner not-found/inactive (`AuthService.cs:377-379`) returns the exact
same string. No branch anywhere returns a different message for these four cases. Only
`PasswordValidator` messages differ from the generic sentinel, which is intentional and
appropriate (the caller already holds a valid, freshly-verified token — a private-channel proof of
ownership, same posture as 2FA-verify).

### 8. Worker-side — **HIGH finding: the live reset token ends up in a broadly-readable table**

`formatEmail`/`formatText` (`notification-dispatch.job.ts:76-123`) themselves never `console.log`
the URL or token — that specific ask is clean. But tracing where `row.payload`
(`{resetUrl, expiresInMinutes}`, containing the live, unhashed, still-valid, single-use raw token)
actually flows afterward surfaces a more serious problem than a stdout log:

1. `dispatchTargeted()` (`notification-dispatch.job.ts:207-213`) calls
   `logNotifications(client, { ..., payload: row.payload, outcomes })` for **every** delivery
   attempt (email + Telegram), regardless of outcome (`sent`/`skipped`/`failed`).
2. `logNotifications` (`worker/src/services/notification-log.ts:36-52`) `INSERT`s one row per
   channel into `notification_queue`, storing `JSON.stringify(params.payload)` — i.e. the full
   `resetUrl` (with the live raw token) — as that row's `"Payload"`, with `"Channel"` = `"email"` /
   `"telegram"` (**not** `"system"`).
3. `NotificationRepository.GetHistoryAsync` (`NotificationRepository.cs:60-64`) excludes only
   `Channel = 'system'` rows from history — the per-channel delivery-result rows created in step 2
   are `Channel = 'email'`/`'telegram'`, so they are **not** excluded.
4. `NotificationsController.GetHistory`/`GetById` (`NotificationsController.cs:57-80`) scope
   **only by `tenantId`** (resolved from the JWT) — there is no enforcement that
   `NotificationHistoryQuery.UserId` (`NotificationDtos.cs:41`, an optional caller-supplied filter)
   equals the caller's own id, and the controller carries a bare `[Authorize]`
   (`NotificationsController.cs:12`) with no role/capability restriction.
5. `NotificationHistoryDto` (`NotificationDtos.cs:18-30`) includes `Payload` verbatim.

Net effect: **any authenticated user in a tenant, of any role, can call
`GET /api/notifications/history` (optionally filtered by `eventType=auth.password_reset_requested`,
or by another user's `userId`, or by neither) and read every colleague's live password-reset link**
— a fully usable, unexpired, single-use account-takeover credential — for as long as it's within
its 30-minute window, and the historical row (Payload included) persists for up to
`CLEANUP_NOTIFICATION_DAYS` (default 90, `worker/src/jobs/cleanup.job.ts:10`) regardless of the
token's own lifetime. This requires no IP tricks, no brute force, no cross-tenant boundary — just
one ordinary authenticated `GET` from any existing session in the same tenant, and it's exactly the
kind of request a legitimate notification-bell UI already polls routinely.

This is not a bug in TASK-455 or TASK-457. `/api/notifications/history`'s lack of per-user default
scoping is a **pre-existing** gap (every other event type's payload — `receipt.created`,
`supplier.message`, `access.temporary_expiring_soon`, etc. — is purely informational, so reading a
colleague's history row was low-severity before). TASK-456 is what turns this into a real
account-takeover primitive, by being the first event type whose `Payload` carries a bearer secret
and routing it through the same generic outbox → `logNotifications` → history-API path used for
non-secret events, with no redaction step for this specific event type.

**Recommend:** in `dispatchTargeted()`/`logNotifications()`'s call for
`auth.password_reset_requested`, don't persist the raw `resetUrl` into the row that
`logNotifications` writes — pass a redacted payload (e.g. `{ expiresInMinutes }` only, or a
`{ delivered: true }` marker) to `logNotifications`, while still using the real, un-redacted
`resetUrl` for the `sendEmail`/`sendTelegramMessage` calls themselves (which already happen earlier
in the same function, before `logNotifications` runs — no reordering needed, just don't reuse
`row.payload` verbatim for the log call). This is a small, targeted worker-side change, but it's a
deliberate design decision about exactly what should and shouldn't survive into history for this
event type, so I'm reporting it rather than patching it, per the brief. Separately (not blocking
this feature, but worth its own follow-up): `/api/notifications/history`/`GetById` having no
default same-user scoping at all is a broader, pre-existing access-control gap that will bite again
the next time any event type's payload carries something sensitive — worth a dedicated ticket.

## Overall verdict

**NOT clear to ship as-is.**

- **HIGH, should fix before this goes live for real users:** item 8 — the live reset token is
  persisted into `notification_queue` rows that `GET /api/notifications/history` returns to any
  authenticated same-tenant user, of any role, turning "forgot my password" into a same-tenant
  account-takeover vector. Fix is scoped to the worker's `dispatchTargeted()`/the payload handed to
  `logNotifications()` for this one event type.
- **MEDIUM, should fix soon:** item 3 — no per-user/per-email cooldown in `ForgotPasswordAsync`
  independent of the per-IP limiter, which KI-014 already confirms is ineffective in production —
  currently an unmitigated Telegram-notification-spam vector against any known/guessed email.
- **OK, no action needed:** items 1, 2, 4, 5, 6, 7 (item 2 has a low-severity, pre-existing-pattern
  timing note; not a blocker).

No fixes were applied in this pass (audit only, per the brief) — both findings above are
recommendations for a follow-up implementation task.

## Not in scope / not re-verified

- Did not re-run `dotnet test` (read-only review, no code changed; TASK-456's own log already
  reports 1220/1220 green). Re-ran `dotnet build ShelfGuard.sln` (clean) and worker `tsc --noEmit`
  (clean) only.
- Did not audit the broader `/api/notifications/history` endpoint beyond what's needed to assess
  finding 8's exploitability (i.e., did not enumerate every other event type for similar payload
  sensitivity) — flagged as a separate, broader follow-up above rather than expanding this review's
  scope.
- Mobile/consumer-facing surfaces are untouched by TASK-455/456/457 and were not reviewed here.

## Git

Not committed (repo convention — main session/user commits; this is a docs-only log file).
