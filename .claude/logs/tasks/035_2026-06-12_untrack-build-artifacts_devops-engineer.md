---
task_id: TASK-035
date: 2026-06-12
agent: devops-engineer
status: done
---

# TASK-035 — Untrack bin/obj build artifacts

## Problem
473 files under `backend/*/bin` and `backend/*/obj` were committed before the
`bin/` / `obj/` gitignore rules existed — gitignore never untracks already-tracked
files. Every `dotnet build` polluted `git status` with ~70 phantom modifications
and caused a real stash conflict on 2026-06-11.

## Fix
- `git rm -r --cached` on 12 bin/obj directories across 6 projects
  (HashGen, Api, Application, Domain, Infrastructure, Tests) — files stay on disk
- Bonus: untracked `mobile/.expo/devices.json` and added `.expo/` to mobile/.gitignore
  (Expo device cache churned on every dev-client connect)

## Verification
- tracked bin/obj count: 473 → **0**
- fresh `dotnet build` → `git status` **clean**
- server pulled the deletion commit cleanly (Docker builds from source — host
  bin/obj files unused)

## Note
Hit "filename too long" passing 473 paths to git on Windows — directory-level
`git rm -r --cached` is the way.
