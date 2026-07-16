# TASK-373 — Block 19 (final): pre-launch readiness go/no-go + stale-doc refresh

**Status:** done (2026-07-16) · **By:** main session (documentation-writer + project-manager, direct)
**Block:** 19 (final) of the pre-launch audit `eager-pondering-tower.md` · **Depends:** TASK-350..372

## What was done
Synthesised the entire 20-block audit (Blocks 0–18 + this one, TASK-350..372) into one go/no-go
launch document, and refreshed the three stale architecture docs.

### Deliverable (new)
`.claude/docs/prelaunch-readiness.md` — the main output. Sections: executive verdict, per-block
summary (19 rows), critical findings FIXED grouped by severity, **launch blockers** (deploy to prod,
run 8 new migrations, verify prod DB role, device-test mobile), open items needing a user decision,
accepted risks, metrics.

### Docs refreshed (surgical, not rewritten — added "Last reviewed: 2026-07-16")
- `architecture.md` — domain-module table (v1→v4 all shipped; Store→Location / Product→Item renames),
  worker queue table (added ai-order/weather/fiscalization-retry/telegram + worker `app.role` rule),
  AI status "not started"→shipped, infrastructure-state table (dev/staging/prod ports, non-superuser
  RLS role note, prod-not-yet-deployed warning), ADR range 008→020+.
- `backend-structure.md` — startup sequence (KI-006 resolved + KI-028 canary + KI-027 role note),
  migration history (3 rows → ~75 migrations + list of the 8 audit migrations), feature-status table
  (all shipped; retirements noted).
- `frontend-structure.md` — KI-004 note flipped to resolved (+ KI-021 caveat), pages table refreshed
  (~43 pages / 35 features; /notifications+/settings done; error boundaries added).

## Key synthesis facts
- **Verdict: NO-GO today, short path to GO.** All audit fixes are on dev/staging only and are an
  **uncommitted working tree** (verified via `git status`: new migrations, `RlsRoleGuard.cs`, loadtests,
  staging compose all untracked; modified controllers/services/worker modified-not-committed).
  Production still runs the full pre-audit codebase.
- **4 launch blockers:** (1) commit + deploy audit to prod; (2) run the 8 dev-applied migrations on prod
  (+ decide on the un-applied `ExpandProviderBypassToProviderAdmin`); (3) SSH-verify prod's Postgres
  role is non-superuser (`rolsuper=f, rolbypassrls=f`) — assumption, not confirmed this session; (4)
  device-test mobile (KI-024/025/026 verified at code level only, no device in audit env).
- **Metrics:** backend `dotnet test` 854/854, frontend `npx vitest run` 48/48 (was 0 pre-audit);
  ~16 P0/critical + ~12 P1/high fixed; ~11 KI resolved, ~13 open (mostly accepted risk / user decision).
- **KI status split:** resolved KI-004/005/006/008/013/016/024/025/026/027/028; open/decision
  KI-007/009/010/011/014/015/017/018/019/020/021/022/023.

## Build/test
No code changed this block (docs only). Last known green: `dotnet test` 854/854 (TASK-372),
`npx vitest run` 48/48 (TASK-371).

## Files
`.claude/docs/prelaunch-readiness.md` (new), `.claude/docs/architecture.md`,
`.claude/docs/backend-structure.md`, `.claude/docs/frontend-structure.md`,
`.claude/tasks/current.md` (TASK-373 entry).
