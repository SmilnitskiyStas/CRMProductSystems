## Bug: Allowlisted offline reads unavailable after Android process death

Date: 2026-08-01  
Severity: high  
Task: TASK-463

Steps: load marketplace suppliers online as store manager; disable all phone network; force-stop;
cold-launch current source over USB.

Expected: owner-safe cached supplier summaries appear with offline timestamp/stale warning.

Actual: only `Немає з’єднання` / `Сесію збережено...` appears; cached reads are inaccessible.
Privacy fails closed, and reconnect + Retry restores Dashboard. No business mutation occurred.

## Fix implemented — 2026-08-01

**Status:** fix_ready_for_device_retest

Added a protected, versioned, 24-hour offline-session snapshot containing only tenant/user IDs,
role and exact previously verified allowlisted routes. It never duplicates the access token or
stores names, email, permissions, capabilities or module payloads. On transient `/auth/me` failure,
bootstrap requires snapshot/owner equality, hydrates only that owner cache, filters routes again to
families that actually have retained cached data, and enters a restricted offline-read shell.

The shell denies dashboard, stock, POS, details, search and every nonallowlisted route. Its three
list screens suppress network requests, detail navigation, search and refresh while offline. On
reconnect it reruns `/auth/me`, reloads module context, replaces the snapshot and promotes to the
normal app. Invalid/expired/corrupt snapshots fail closed; logout and terminal auth cleanup delete
snapshot and current-owner cache. Automated checks pass; Android process-death retest and iOS
acceptance are still required. No device/build work was run in this fix session.
