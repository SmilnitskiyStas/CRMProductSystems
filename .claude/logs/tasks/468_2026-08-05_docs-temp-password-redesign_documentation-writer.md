# TASK-468: Docs for temp-password forgot-password redesign (api-contracts, ADR-026, database-schema verification)

**Agent:** documentation-writer
**Date:** 2026-08-05
**Status:** done — docs only, no code touched. Session was interrupted mid-task right at the
`current.md` edit (platform-side, unrelated to this task); resumed and completed, see "Session
interruption" note below.

## Context

TASK-464 (database-engineer), TASK-465 (backend-developer), TASK-466 (frontend-developer)
replaced the one-time link/token forgot-password flow (TASK-455..460, ADR-024, live on prod since
2026-07-30) with a temporary-password design: `POST /api/auth/forgot-password` now generates a
directly-usable temp password (3h validity) instead of a reset link; `POST /api/auth/reset-password`
no longer exists. This task documents that redesign — no code changes.

## Done

### 1. `.claude/docs/api-contracts.md`
- Bumped `**Updated:**` to 2026-08-05.
- `POST /api/auth/login`: added the new specific `401` case ("Temporary password has expired.
  Please request a new one.") with a note that it's only reachable after a real hash match against
  an expired temp password, never on a genuinely wrong password.
- `POST /api/auth/forgot-password`: rewrote the description to say it generates and sends a
  temporary password (14 chars, letter+digit guaranteed, no ambiguous chars, 3h validity via
  `User.TempPasswordExpiresAt`) instead of a link — no second step, login goes through the
  ordinary `/api/auth/login`.
- Removed the `POST /api/auth/reset-password` endpoint block entirely; replaced the old
  "Reset-link (лист/Telegram)" paragraph with a "Доставка" paragraph describing the same outbox
  mechanism (`auth.password_reset_requested`, now `Payload={tempPassword, expiresInMinutes}`) and
  a note that password changes now go through the existing authenticated `change-password`, with
  `/api/auth/reset-password` explicitly called out as 404/gone.
- `AuthUserDto`: added `passwordIsTemporary: boolean` and `temporaryPasswordExpiresAt: string|null`
  to the JSON example, plus an explanatory paragraph (mirrors `capabilities`/`tabs`' existing
  paragraph style) noting it's fresh at every mint site and self-clears on change/expiry.

### 2. `.claude/docs/decisions.md`
- **ADR-024**: `Status` changed to `superseded by ADR-026`, plus a short `⚠️ Why superseded`
  callout right after the status line. Original content (all 5 decision points, consequences,
  extends) left completely untouched below it, per the "never delete, only mark superseded" rule.
  The callout states precisely which points are dead (2 — the fail-open RLS exception; 5 — the
  400-vs-401 reasoning for the now-gone `reset-password` endpoint) vs. which remain true in
  substance (1, 3, 4 — outbox reuse, email/Telegram channel choice, the
  `Environment.GetEnvironmentVariable` Application-layer pattern).
- **New ADR-026** (inserted at the top of the file, above ADR-025, per the file's newest-first
  order; file's own `**Updated:**` bumped to 2026-08-05, ADR's own `Date:` set to 2026-08-04 to
  match the underlying work, following this file's existing convention of dating an ADR to when
  the decision/work happened, not when it was written up): full record of the redesign —
  temp-password-overwrites-PasswordHash-directly mechanics, the RLS exception count dropping back
  to 2, `reset-password`'s removal in favor of the existing `change-password`, and the auth-locale
  default flip (English for non-uk browsers) as a smaller bundled change.
  - **Point 4** is the cooldown question the brief asked me to resolve against TASK-467's
    recommendation. At first-draft time, `.claude/logs/tasks/467_*` did not exist yet (confirmed
    via `Glob` immediately before drafting, and re-confirmed again right before what I thought was
    the final step) — TASK-467 (security-reviewer) was still in flight in parallel, so the first
    draft of point 4 documented only what was known then (TASK-460's old 60s
    `PasswordResetCooldown` not carried over, per-IP rate limit the only throttle left, KI-014
    already flagging that as unreliable in prod) and explicitly told the next reader to check for
    a TASK-467 log rather than treat the question as settled — no verdict was fabricated.
    TASK-467 finished shortly after (its own session hit the same platform interruption, at the
    same last step — writing its `current.md` entry — but its task log
    `.claude/logs/tasks/467_2026-08-05_security-review-temp-password-redesign_security-reviewer.md`
    was already complete). Read that log directly (not just the coordinator's relay of it) before
    revising: verdict **CLEAR TO SHIP**, 0 HIGH, 2 MEDIUM. Rewrote point 4 to report both findings
    in full — (a) no per-user cooldown, judged *materially worse* than the superseded design since
    every call now overwrites the real password immediately rather than just sending another link;
    (b) `ForgotPasswordAsync` never calls `RevokeAllForUserAsync`, unlike the old
    `ResetPasswordAsync`, so a stolen refresh token from an earlier compromise survives a
    forgot-password request — and updated the Consequences section's matching bullet to match.
    Neither MEDIUM fix has landed; both are open recommendations for a not-yet-numbered
    backend-developer follow-up, cited in the ADR.

### 3. `.claude/docs/database-schema.md`
Verified only, no edit needed — TASK-464 had already done this correctly:
- "Documented exceptions" table is back to exactly 2 rows (`users`, `refresh_tokens`).
- The note beneath it correctly explains `password_reset_tokens`' removal with pointers to both
  `## TASK-455` (superseded) and `## TASK-464`.
- `## TASK-455` section carries the `⚠️ Superseded by TASK-464` note at its top, original content
  otherwise intact below it (historical record, not deleted).
- `## TASK-464` section has full detail (dropped/added schema, `User.cs` method shapes, the
  `AuthService` build-blocker note for TASK-465).
- The regression-test description correctly reads "two exceptions" (not "three").
No drift found between what TASK-464's log claimed and the file's actual live content.

## Additional fix (flagging, not one of the brief's 3 sections)

`.claude/tasks/blocked.md`'s TASK-260 entry (the Resend DNS blocker) had a stale sentence from
2026-07-30 describing the forgot-password flow as delivering "лінка відновлення" (a recovery
link) and citing only ADR-024. Left as-is, it would actively contradict the docs this task just
wrote. Fixed the one paragraph to say the flow now delivers a temp password directly, cites
ADR-024 as superseded, and points to ADR-026/TASK-464..466 — kept the paragraph's actual point
(this DNS blocker also blocks the email channel for this flow, Telegram fallback unaffected)
unchanged. Small, low-risk, single-paragraph, docs-only — same subject matter as the rest of this
task, so treated as in-scope rather than filing a separate follow-up.

## Session interruption (transparency note)

The session hit a platform-side "model temporarily unavailable" error mid-edit, exactly on the
`current.md` write (first attempt) — nothing else was lost, all prior edits (api-contracts.md,
decisions.md, blocked.md, the task log itself) had already landed. The coordinator relayed that
TASK-467 had, in the meantime, also finished and hit the same interruption at its own equivalent
step. Rather than take the coordinator's paraphrase of TASK-467's verdict at face value for a
permanent architecture record, I read TASK-467's task log directly (full file, all 8 checklist
items) before revising ADR-026 point 4 — the paraphrase turned out accurate, but the ADR now cites
and is grounded in the primary source, not a secondhand relay. `current.md` also already had
TASK-467's own entry by the time I got back to it (that agent's session resumed independently) —
confirmed via a fresh read rather than assuming my stale in-context copy was still current, since
my first edit attempt had failed against it.

## Not in scope (per brief)

- No code changes.
- No security review — TASK-467 (security-reviewer) owns that; its finished verdict is now fully
  incorporated into ADR-026 point 4 (see above), not just flagged as pending.
- `glossary.md` not touched — "temporary password" isn't a domain term on the level of
  FEFO/CDA/ADU that needs a glossary entry, and it wasn't in the brief's 3-file scope.

## Files

Modified:
- `.claude/docs/api-contracts.md`
- `.claude/docs/decisions.md`
- `.claude/tasks/blocked.md` (see "Additional fix" above)
- `.claude/tasks/current.md` (new TASK-468 entry, see below)

Verified unchanged (already correct):
- `.claude/docs/database-schema.md`

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
