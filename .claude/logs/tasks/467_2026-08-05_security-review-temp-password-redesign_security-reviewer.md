# TASK-467: Security review — temporary-password forgot-password redesign

**Agent:** security-reviewer
**Date:** 2026-08-05
**Status:** done — **verdict: CLEAR TO SHIP.** 0 HIGH findings (TASK-458's HIGH stays fixed
under the new design). 2 MEDIUM findings (fix soon, not blockers). All other checklist items:
OK. Read-only audit, no code changed.

## Scope

Read TASK-464 (db), TASK-465 (backend), TASK-466 (frontend) logs, then TASK-458 (the previous
review, for comparison) — then the code directly: `AuthService.cs`, `User.cs`, `AuthController.cs`,
`IAuthService.cs`, `AuthDtos.cs`, `UserService.cs`, `PasswordValidator.cs`,
`worker/src/jobs/notification-dispatch.job.ts`, `NotificationsController.cs`,
`TemporaryPasswordBanner.tsx`, `LoginForm.tsx`, `settings-user/page.tsx`, `Program.cs`'s rate
limiter config, `known-issues.md` (KI-014), and the new/changed tests in `AuthServiceTests.cs` /
`UserServicePasswordTests.cs`. Confirmed current task-log max was 466 and `current.md`'s own
`## TASK-` headers max was 466 before numbering this 467.

## Checklist verdicts

### 1. Temp-password entropy/generation — OK

`GenerateTempPassword()` (`AuthService.cs:583-602`): 14 chars, `RandomNumberGenerator.GetInt32`
(CSPRNG, same class already reviewed OK for reset tokens/recovery codes in TASK-458) drawing from
a 56-character alphabet (48 letters + 8 digits, ambiguous chars 0/O/1/I/l excluded) — roughly 81
bits of entropy. Letter and digit character classes are **provably** guaranteed regardless of the
later Fisher–Yates shuffle: `chars[0]`/`chars[1]` are fixed from letter-only/digit-only pools
*before* the shuffle, and shuffling only permutes array positions — it cannot remove characters
from the multiset, so `password.Any(char.IsLetter)`/`Any(char.IsDigit)` stay true unconditionally
after any shuffle outcome. Verified this by tracing the loop, not just trusting the doc comment.

Cross-checked against `PasswordValidator.Validate` (`PasswordValidator.cs:58-81`) directly:
`MinLength=12` (14-char output always passes), letter/digit checks (structurally guaranteed, see
above), ~100-entry common-password blocklist (exact-string match; given ~81 bits of entropy vs.
~100 fixed strings, collision probability is astronomically negligible — not structurally
impossible but not a practical concern), and an email-local-part substring check (moot for a CSPRNG
string — the check exists to stop *chosen* weak passwords, and it isn't even invoked here, see
next paragraph). No issue.

Note: `ForgotPasswordAsync` never actually calls `PasswordValidator.Validate` on the generated
value — it's bypassed by design, which is only safe because the two rules that matter (length,
letter+digit) are unconditionally guaranteed by construction as shown above. Not a finding, just
confirming the "guaranteed by construction, not left to the draw" claim in the code comment is
mechanically true, per the brief's ask to verify this directly rather than trust the report.

### 2. Worker redaction — OK, confirmed still fixed (this was TASK-458's HIGH)

`dispatchTargeted()` in `notification-dispatch.job.ts:212-224`: before calling `logNotifications()`,
builds `logPayload` that, for `event_type === "auth.password_reset_requested"`, keeps only
`{ expiresInMinutes }` and drops `tempPassword` entirely. The real `tempPassword` value is used
**earlier** in the same function (lines 184-185 `formatText`/`formatEmail`, consumed by
`sendTelegramMessage`/`sendEmail` at lines 190-199) — no reordering needed, redaction just doesn't
reuse `row.payload` verbatim for the log call. This is byte-for-byte the same defense TASK-460 built
for the old `resetUrl`, correctly re-derived for the new secret shape (a live password, not a
single-use link) without being asked to. `logNotifications` (`notification-log.ts`) still writes
whatever payload it's given verbatim into `notification_queue`, and
`NotificationsController.GetHistory`/`GetById` (`NotificationsController.cs:12,59`) are still bare
`[Authorize]` with tenant-only scoping (no default same-user filter) — re-confirmed this broader,
pre-existing gap is still open, exactly as TASK-458 flagged it as a separate follow-up ticket (not
re-flagging here, just confirming the *specific* mitigation for this event type holds even though
the general endpoint-scoping gap wasn't touched). This is the single most important confirmation in
this review and it holds cleanly.

### 3. Login timing/ordering for expired temp password — OK

`LoginAsync` (`AuthService.cs:66-113`): order is unknown-email → inactive → locked-out (dummy
`Verify` call for timing, TASK-329's existing pattern, unchanged) → `!Verify(password, hash)` →
generic error — **only past a successful hash match** does
`if (user.TempPasswordExpiresAt.HasValue && !user.HasActiveTempPassword)` run (line 103), returning
the specific `TempPasswordExpiredError` before the TOTP branch. A wrong guess against an account
with an expired temp password can never reach that branch — confirmed both by reading the code and
by the dedicated test `LoginAsync_wrong_password_against_expired_temp_password_stays_generic`
(`AuthServiceTests.cs:343-357`), which asserts the generic message. No new timing/enumeration
oracle: whoever triggers the specific message already proved possession of the correct (if expired)
credential.

### 4. No per-user cooldown on forgot-password — CONFIRMED GAP, recommend fixing soon (MEDIUM)

Confirmed: no cooldown of any kind exists in `ForgotPasswordAsync` (`AuthService.cs:322-374`) —
matches TASK-465's own log, which flagged this as a deliberate, in-scope-per-brief omission.
`"auth-forgot-password"` rate limit unchanged (`Program.cs:125-131`, 5/min per client IP), and
`known-issues.md` KI-014 (lines 83-97) still documents, as a **confirmed production condition**
(not theoretical), that per-IP partitioning never accumulates in prod because the hosting
provider's port-mapping layer doesn't preserve client IPs. KI-014's own mitigations list
(line 94-95) names per-account lockout as the IP-independent backstop for login/2FA — forgot-password
has no equivalent, and by nature of "no wrong attempt" can't reuse the lockout mechanism as-is.

**This is a materially different risk than TASK-458's original MEDIUM for the old design.** In the
superseded link design, repeated forgot-password calls only spammed notifications — the account's
real password was untouched until someone completed a separate reset step with a valid token. In
this design, **every** `ForgotPasswordAsync` call immediately overwrites `PasswordHash`
(`AuthService.cs:336`). An attacker who knows/guesses a victim's email can call
`POST /api/auth/forgot-password` in a loop — defeating the per-IP limiter the same way KI-014
already describes — and continuously invalidate whatever credential the legitimate user currently
holds, faster than the user can realistically receive and use any single one of them. This is a
low-effort, repeatable **account-lockout/denial-of-access** vector against one targeted user, not
just a harassment/spam nuisance as before. It requires no new capability beyond what KI-014 already
concedes is true today, and doesn't cross a tenant/account boundary or leak data, which keeps it at
MEDIUM rather than HIGH — but the consequence (a targeted employee can be kept locked out of their
own account indefinitely) is more severe than "spam," so **recommend implementing a per-user
cooldown now rather than deferring it again.**

Concrete low-cost fix: no new migration needed. `TempPasswordExpiresAt` already encodes "when was
the current temp password issued" indirectly (`issuedAt = TempPasswordExpiresAt -
TempPasswordValidHours`, both are `AuthService.cs` constants/fields already in scope) — skip
re-issuing (or just no-op/204 early) when `user.TempPasswordExpiresAt.HasValue &&` that derived
`issuedAt` is within the last ~60s, mirroring TASK-460's original per-user cooldown for the old
design without needing a dedicated column.

### 5a. Old password superseded immediately — OK, confirmed intentional

Confirmed the account's real, previously-working password stops working the instant
`ForgotPasswordAsync` runs (no dual-validity window) — this is documented directly in
`AuthService.cs:20-26`'s class-level comment, `TASK-464`'s `User.cs` doc comments, and the
frontend's own success copy ("Sign in with it, then set a new password"). Intentional, not an
oversight.

### 5b. Anti-hijack session revocation on forgot-password — CONFIRMED GAP, recommend fixing (MEDIUM)

Grepped every production call site of `RevokeAllForUserAsync` across `backend/`: exactly three —
`UserService.ChangePasswordAsync` (`UserService.cs:419`, self-service authenticated password
change), `AuthService.RefreshAsync`'s refresh-token-reuse-detected branch (`AuthService.cs:130`,
unrelated anti-replay defense), and `ProviderTeamService.cs:67` (provider-team flow, out of scope).
**None of these is `ForgotPasswordAsync`.** The superseded `ResetPasswordAsync` (per TASK-458's own
item 5) called `_refreshTokens.RevokeAllForUserAsync(...)` as its last write, before persisting —
an explicit anti-hijack measure. The new `ForgotPasswordAsync` has no equivalent call anywhere in
its body (`AuthService.cs:322-374`).

Concrete impact: if an attacker already holds a live, stolen refresh token (7-day TTL,
`RefreshToken.Create(..., DateTime.UtcNow.AddDays(7))`) from an **earlier, unrelated** compromise —
phishing, malware, a forgotten logged-in device, etc. — and the legitimate user runs forgot-password
specifically because they suspect exactly that kind of compromise, the new design no longer evicts
the attacker's session as a side effect of recovery. The attacker's refresh token keeps minting
fresh access tokens, completely undisturbed, until the user separately completes a full
authenticated `ChangePasswordAsync` (which does still call `RevokeAllForUserAsync` — confirmed at
`UserService.cs:419`). Since nothing forces that follow-up step — the temp password is a fully
usable, ordinary password for up to 3 hours, and a user can keep re-requesting fresh temp passwords
indefinitely without ever visiting the "set a new password" flow — the remediation window is not
tightly bounded in practice.

This requires a **pre-existing** compromise to matter at all (it's a failure to fully remediate an
existing takeover, not a standalone new attack vector granting unauthorized access to someone who
didn't already have it), which is why this stays MEDIUM rather than HIGH under the same rubric
TASK-458 used. But it is a real, concrete regression versus the previously-reviewed design, for a
very plausible legitimate trigger ("I think someone else has access to my account"). **Recommend:**
add `await _refreshTokens.RevokeAllForUserAsync(user.Id, ct)` inside `ForgotPasswordAsync`,
mirroring `UserService.ChangePasswordAsync`'s existing call — ideally flushed in the same early
`_users.SaveChangesAsync(ct)` round trip (`AuthService.cs:343`) that already durably commits the
credential change before the activity log/notification writes.

### 6. `ChangePasswordAsync` clears `TempPasswordExpiresAt` — OK

`UserService.ChangePasswordAsync` (`UserService.cs:397-427`): `user.ClearTempPasswordExpiry()`
called at line 415, immediately after `user.ChangePassword(...)` (line 410) and before
`_users.Update(user)` — confirmed in the authenticated self-service flow, matches TASK-465's claim.
Also confirmed this method still calls `RevokeAllForUserAsync` (line 419) as the "someone took
control of their account" checkpoint — see 5b above for why that same call is missing from
`ForgotPasswordAsync` itself.

### 7. `/api/auth/reset-password` removed — OK, confirmed at every layer

Controller: read all of `AuthController.cs` — no `reset-password` route exists (routing table has
exactly `login`, `2fa/verify`, `2fa/setup`, `2fa/enable`, `2fa/disable`, `refresh`, `logout`,
`me` GET/PUT, `change-password`, `forgot-password`). Service interface: `IAuthService.cs` has no
`ResetPasswordAsync` member at all. DTOs: `AuthDtos.cs` has no `ResetPasswordRequest` — only
`ForgotPasswordRequest(string Email)` remains under the "Forgot password / temporary password"
section. Frontend: grepped `frontend/` (case-insensitive) for
`reset-password|resetPassword|ResetPasswordRequest|resetUrl` — exactly one hit, a code comment in
`LoginForm.tsx:81` documenting that its sentinel-matching convention follows the now-deleted
`ResetPasswordForm.tsx`'s pattern (historical note, not a live import/link/dead route). No
functional dead references anywhere.

### 8. `TemporaryPasswordBanner.tsx` — OK

Read the full component. Renders nothing unless `user?.passwordIsTemporary &&
user.temporaryPasswordExpiresAt`; only ever displays the formatted expiry timestamp and a static
action label — no code path renders or logs the raw password string. This is also structurally
enforced one layer down: `AuthUserDto` (`AuthDtos.cs:24-80`) has exactly `PasswordIsTemporary`
(bool) and `TemporaryPasswordExpiresAt` (nullable datetime) — there is no raw-password field on the
DTO at all for a frontend bug to accidentally expose. Grepped `frontend/features/auth/` for
`console.log|console.error|console.warn` — zero matches anywhere in the feature directory. Action
link `/settings-user#password` resolves correctly: `settings-user/page.tsx` defines
`id: "password"` as one of its four sections (line 25), renders `id={section.id}` on the section
wrapper (line 142), and renders `<ChangePasswordForm />` when `section.id === "password"` (line
176) — the anchor lands on the right card.

## Overall verdict

**CLEAR TO SHIP.**

- **HIGH:** none. TASK-458's original HIGH (live secret leaking into same-tenant-readable
  notification history) is confirmed still fixed under the new payload shape — this was the
  highest-risk item to re-check given the payload now carries a directly-usable password instead of
  a single-purpose link, and it holds.
- **MEDIUM, recommend fixing soon (not blockers for this deploy):**
  - Item 4 — no per-user forgot-password cooldown; in this design that means repeated calls
    continuously overwrite the account's real password, a low-effort targeted lockout/DoS vector,
    worse than the old design's "notification spam" framing. Fix available without a migration
    (derive issuance time from the existing `TempPasswordExpiresAt` field).
  - Item 5b — `ForgotPasswordAsync` no longer revokes existing refresh-token sessions the way the
    superseded `ResetPasswordAsync` did, weakening the "recover from suspected compromise" use case
    specifically. Fix is a one-line addition mirroring `UserService.ChangePasswordAsync`'s existing
    call.
- **OK, no action needed:** items 1, 2, 3, 5a, 6, 7, 8.

No fixes were applied in this pass (audit only, per the brief and matching TASK-458's own
convention) — both MEDIUM findings above are recommendations for a follow-up backend-developer task
(not yet numbered — next free task ID should be confirmed against `current.md`/`.claude/logs/tasks/`
at that time, same renumbering caution TASK-464/465/466 already had to apply once).

## Not in scope / not re-verified

- Did not re-run `dotnet test`/`dotnet build` (read-only review, no code changed; TASK-465's own
  log already reports 1220/1220 green and 0 warnings/errors, TASK-466's log reports a clean
  frontend build). Independently re-read the relevant test bodies in `AuthServiceTests.cs` (lines
  284-357) directly rather than trusting the log's summary of what they assert.
- Did not re-audit `/api/notifications/history`'s broader lack of default same-user scoping beyond
  confirming it's still present and that the redaction for this specific event type still covers
  it — TASK-458 already flagged the broader gap as its own separate follow-up ticket; still open,
  still worth one, not re-litigated here.
- Did not review the mobile app's auth surfaces — TASK-464/465/466 only touched backend/frontend;
  mobile forgot-password (if it exists) was out of scope for the redesign itself.
- RLS/migration aspects of dropping `password_reset_tokens` were already covered by TASK-464's own
  verification (table + policies confirmed gone live); not re-audited here since this review's
  checklist is about the new application-layer behavior, not the schema change itself.

## Git

Not committed (repo convention — main session/user commits; this is a docs-only log file, no
source changed).
