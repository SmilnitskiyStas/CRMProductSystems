# Handoff 343 → 344 (security-reviewer)

TASK-343 added a `worker_bypass` RLS policy (mirrors `provider_bypass`, gated on
`current_setting('app.role', true) = 'worker'`) to all 73 tables with FORCE ROW LEVEL
SECURITY, fixing the confirmed prod incident where worker cron writes were silently
dropped by RLS (`stock_status_snapshots` had 0 rows). Migration:
`backend/ShelfGuard.Infrastructure/Migrations/20260712175141_AddWorkerBypassRlsPolicy.cs`.
Not deployed yet — needs your sign-off first per the hotfix instructions.

## Separate pre-existing issue — NOT touched by this hotfix, please review

While auditing every `tenant_isolation` policy to build the FORCE-RLS table list, found
a **permissive fallback pattern** on several tables, distinct from `notification_queue`
(which you already know about — same pattern, `(NULLIF(current_setting('app.tenant_id'),
'') IS NULL) OR (...) OR (TenantId IS NULL)`):

1. **`chat_sessions`** — `20260621161638_AddChatFeature.cs` line 32:
   ```sql
   CREATE POLICY chat_sessions_tenant ON chat_sessions USING (
     "TenantId" = current_setting('app.tenant_id', TRUE)::uuid
     OR current_setting('app.tenant_id', TRUE) IS NULL
     OR current_setting('app.tenant_id', TRUE) = ''
   );
   ```
   Any session with `app.tenant_id` unset/empty sees **all tenants' chat sessions**, not
   just its own — same shape of bug as `notification_queue`, just on a live-chat table.

2. **5 supplier tables** — `suppliers`, `supplier_profiles`, `supplier_items`,
   `supplier_metrics`, `supplier_reviews`. Originally created with the strict
   `NULLIF(...)::uuid` pattern (FullSchema / V4SupplierMarketplace), but
   `20260702192126_V41SupplierSelfService.cs` (Up, lines 38-56) **rewrote**
   `tenant_isolation` on all 5 via `ALTER POLICY` to:
   ```sql
   NULLIF(current_setting('app.tenant_id', true), '') IS NULL
   OR "TenantId" = (NULLIF(current_setting('app.tenant_id', true), ''))::uuid
   ```
   Same permissive-OR shape — worker (and anything else that forgets to `SET
   app.tenant_id`) gets unrestricted cross-tenant read/write on these 5 tables. The
   migration's own comment calls this "RLS hardening" (ADR-016/TASK-282) but it actually
   *widened* the policy to add the OR-fallback, presumably to unblock some caller that
   wasn't setting `app.tenant_id` — worth checking git history / TASK-282 log for why.

Neither of these needed touching for TASK-343 (worker now has an explicit, correctly-
scoped bypass instead of relying on the accidental fallback), but they're real
cross-tenant-leak risk on any code path that runs with `app.tenant_id` unset while
`app.role` is *not* `'worker'` or `'provider'` — e.g. a bug that forgets to call
`SET app.tenant_id` before a query would now silently see all tenants' data. Recommend
auditing every migration for this `IS NULL OR ''` / permissive-fallback shape (I only
found these while incidentally reading files for TASK-343, did not do an exhaustive
grep across all 26 RLS migrations) and tightening to the `NULLIF(...)::uuid` strict
pattern + explicit `provider_bypass`/`worker_bypass` policies, same as the documented
pattern in `.claude/agents/database-engineer.md`.
