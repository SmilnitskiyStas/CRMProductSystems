# TASK-470: Mark TASK-467's 2 MEDIUM findings as resolved (docs sync)

**Agent:** documentation-writer
**Date:** 2026-08-05
**Status:** done — docs-only, no code touched.

## Numbering note

Confirmed 470 was free before writing: `.claude/tasks/current.md`'s own `## TASK-` headers max
was 469, `.claude/logs/tasks/` max was 469 (`46*`/`47*` glob, no 470 file yet).

## Context

TASK-469 (backend-developer) fixed both MEDIUM findings from TASK-467's security review
(per-user forgot-password cooldown + refresh-token revocation), but per its own brief left docs
untouched and flagged `decisions.md`'s ADR-026 §4 and `current.md`'s TASK-467 entry as now-stale
("neither fix has landed" / "both still open"). This task closes that documentation gap only.

## Done

- `.claude/docs/decisions.md` — ADR-026 §4: point 4's intro sentence now states both MEDIUM
  findings are fixed (TASK-469, same day) instead of leaving only "recommended to fix soon";
  point 4's closing paragraph ("Neither fix has landed...") replaced with a summary of what
  TASK-469 actually did for each finding (cooldown derivation from `TempPasswordExpiresAt`;
  `RevokeAllForUserAsync` call mirroring `ChangePasswordAsync`) plus build/test verification
  numbers and a link to TASK-469's log alongside the existing TASK-467 log link. Consequences
  section's matching bullet ("both still open as of this writing") updated to state both are now
  fixed, same-day, by TASK-469. The two MEDIUM-finding bullets themselves (original description of
  what TASK-467 found) were left as-is — historical record of the review, not rewritten.
- `.claude/tasks/current.md` — TASK-467's entry: status line's **Next** field now points at
  TASK-469 ("closed both MEDIUM findings") instead of "a backend-developer follow-up ... not yet
  task-numbered"; added a short **Update (TASK-469, 2026-08-05)** paragraph after the entry's
  existing body, summarizing both fixes with a link to TASK-469's log. TASK-469's own entry
  (including its "Next: ... flagged for a future documentation-writer pass" note) left untouched —
  it's an accurate historical record of what TASK-469 knew at the time; this task's own new
  TASK-470 entry (below) is what closes that flagged gap.
- Added `## TASK-470` entry to `current.md` (inserted above `## TASK-469`, sprint's most recent).

## Not in scope (per brief)

- No code changes.
- No new security findings or re-review — pure documentation sync of an already-fixed state.

## Files

Modified:
- `.claude/docs/decisions.md`
- `.claude/tasks/current.md`

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
