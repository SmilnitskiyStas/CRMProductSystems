# TASK-642 — Verify prod `tenant_isolation` on `items`: fail-open or fail-closed?

**Agent:** database-engineer · **Model:** opus · **Date:** 2026-08-30 · **Status:** done
**Part C** of the marketplace cross-tenant RLS-leak fix (TASK-641..646), plan
`snappy-dreaming-hanrahan.md` → «Частина C».

## Question

`.claude/docs/database-schema.md:108` claimed *"Production status (as of 2026-07-14): this fix is
applied to the dev database only. Production still runs the fail-open policy shape."* — but
`20260714180000_FixFailOpenTenantIsolationOnReset` already lists `'items'` in its Group A (:48),
and migrations auto-run on deploy (`Program.cs` → `MigrateAsync`). Hypothesis: stale doc.

## Verdict

**Stale doc. Production has been fail-closed since the 2026-07-16 audit deploy (commit `84c48061`).
No migration needed, none written.**

Same failure mode as memory note `shelfguard-store-scope-checklist-doc-stale`: a dated status line
in a doc that nobody rewrote after the deploy that invalidated it. It nearly caused a redundant
production DDL migration.

## Method (read-only — `SELECT` only, no writes/DDL/migrations against prod)

```
ssh -i ~/.ssh/workmate-deploy -p 10048 administrator@93.127.143.98
docker exec shelfguard_postgres psql -U shelfguard -d shelfguard -Atc "<query>"
```
Prod DB container `shelfguard_postgres` (postgres:16-alpine), db/role `shelfguard`.

## Evidence 1 — migration history

`SELECT "MigrationId" FROM "__EFMigrationsHistory" WHERE ... ORDER BY "MigrationId"` — all present
on prod:

- `20260714100000_FixMissingRlsGuardsAndProviderBypass`
- `20260714150000_ExpandProviderBypassToProviderAdmin` ← the "decision required, never applied to
  any DB" one from `prelaunch-readiness.md:131-135`. **It rode in and is applied on prod.**
- `20260714180000_FixFailOpenTenantIsolationOnReset` ← the fail-open fix in question
- `20260714210933_AddStockReceiptsTransfersTenantIndexes`
- `20260715054917_AddProductStockXminConcurrencyToken`
- `20260715120000_FixNotificationSettingsRlsFailOpen`
- `20260715153812_AddChatAndSupportMessagesRls`
- `20260715180053_AddActivityLogsIndexesAndDropSupersededStockIndexes`
- `20260715204612_AddChatSessionsAndSupplySchedulesTenantIndexes`
- `20260716120000_FixActivityLogsInsertUnderFailClosedRls`
- (…through `20260719193545_AddLocationStoreScopeRlsPolicies`)

i.e. every migration `prelaunch-readiness.md` blocker 2 lists as "applied only to dev so far".

## Evidence 2 — actual prod policy expressions

`SELECT c.relname, pg_get_expr(p.polqual, p.polrelid), pg_get_expr(p.polwithcheck, p.polrelid)
FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid WHERE p.polname = 'tenant_isolation' …`

All 11 requested Group-A tables — `items`, `suppliers`, `supplier_items`,
`supplier_item_barcodes`, `supplier_item_images`, `supplier_metrics`, `supplier_reviews`,
`product_stock`, `categories`, `product_segments`, `product_supplier_settings` — return
**identically**:

```
("TenantId" = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid)
```
with `polwithcheck = NULL`. That is exactly the canonical fail-closed form. No `IS NULL OR` prefix.

Group-C (EXISTS-through-parent) spot check — `location_zones`, `pos_transaction_items`,
`stock_receipt_items`, `write_off_items` — likewise fail-closed, e.g.:
```
EXISTS (SELECT 1 FROM locations l WHERE l."Id" = location_zones."LocationId"
        AND l."TenantId" = (NULLIF(current_setting('app.tenant_id'::text, true), ''::text))::uuid)
```

## Evidence 3 — exhaustive sweep, not just spot checks

Prod has **107** `tenant_isolation` policies. Filtering for the session-level fail-open branch:

```sql
SELECT c.relname FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
WHERE p.polname = 'tenant_isolation'
  AND pg_get_expr(p.polqual, p.polrelid)
      LIKE '%current_setting(''app.tenant_id''::text, true), ''''::text) IS NULL%';
```
→ **exactly two rows: `users`, `refresh_tokens`.** Precisely the two documented, load-bearing
pre-auth exceptions. Nothing else on prod is fail-open. Per the brief these were not touched.

A looser `LIKE '%IS NULL%'` returns 5 tables — the extra 3 are false positives that matter to
anyone re-running this check:
- `activity_logs`, `notification_queue` — Group B, `… OR "TenantId" IS NULL`. That is the
  *row-level* provider/system-row clause the migration deliberately keeps, not a session-level
  fail-open branch.
- `notification_settings` — `EXISTS(… u."TenantId" = … OR u."TenantId" IS NULL)`. Row-level
  provider-user clause only; its session-level branch was correctly removed by
  `20260715120000_FixNotificationSettingsRlsFailOpen`, which is applied on prod.

## Evidence 4 — RLS is actually enforced on prod (blocker 3 / KI-027)

A fail-closed policy is worthless if the app connects as a superuser, so this was checked too:

| Check | Result |
|---|---|
| `SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolcanlogin` | `shelfguard \| t \| t` (bootstrap), `shelfguard_app \| f \| f` |
| `SELECT DISTINCT usename FROM pg_stat_activity WHERE datname='shelfguard'` | `shelfguard_app` (the API's live connections) |
| `SELECT tableowner FROM pg_tables WHERE tablename='items'` | `shelfguard_app` |
| `relrowsecurity` / `relforcerowsecurity` on `items`, `suppliers`, `supplier_items` | `t` / `t` |

The KI-027 non-superuser role fix **is** applied on prod. FORCE RLS means the owning role doesn't
bypass either. Production RLS is live, not inert.

## Bonus finding relevant to Parts A/B (TASK-643)

Prod `items` full policy set confirms plan finding **F1** on the live database:

```
provider_bypass  | FOR ALL | PERMISSIVE | USING (current_setting('app.role', true)
                                                = ANY (ARRAY['provider','provider_admin']))
                 | WITH CHECK = NULL   ← Postgres defaults WITH CHECK to USING ⇒ write bypass too
tenant_isolation | FOR ALL | PERMISSIVE | USING ("TenantId" = (NULLIF(...))::uuid)
worker_bypass    | FOR ALL | PERMISSIVE | USING (current_setting('app.role', true) = 'worker')
```

So the leaked session-level `app.role='provider'` from `MarketplaceRepository.SetProviderRoleAsync`
grants cross-tenant **read and write** on prod's real `items` table. The Part-C outcome (no
migration) does **not** reduce the severity of TASK-643 — the fail-closed `tenant_isolation` is
OR-ed with a permissive `provider_bypass`, so fixing the policy shape was never going to contain
this leak. Part A remains the only fix.

## Changes made

| File | Change |
|---|---|
| `.claude/docs/database-schema.md` | Replaced the stale ":108" production-status paragraph with the verified 2026-08-30 status: migration IDs found, the 107-policy sweep result, the two real exceptions vs the three `%IS NULL%` false positives, the spot-checked table list, and the `shelfguard_app` role evidence. |
| `.claude/docs/prelaunch-readiness.md` | Added a dated ⚠️ correction banner above the "LAUNCH BLOCKERS" blockquote (which still asserts "Production is untouched"), plus ✅ CLOSED notes on blocker 2 (migrations — incl. that `ExpandProviderBypassToProviderAdmin` rode in) and blocker 3 (prod Postgres role). Other blockers were not re-checked and are left as-is. |

No migration file created. No schema change. No code touched. All changes uncommitted per brief.

## Verification status

- Prod queries: read-only, `SELECT` only. No writes, no DDL, no `MigrateAsync` against prod.
- `dotnet test --filter "FullyQualifiedName~RlsCrossTenant"` **not run** — no migration was
  produced, so there is nothing new for it to cover. `TenantIsolationPolicies_HaveNoFailOpenBranch_
  ExceptDocumentedPreAuthLookups` (`RlsCrossTenantIntegrationTests.cs:289`) already asserts on dev
  exactly the invariant this task confirmed by hand on prod.
- `users` / `refresh_tokens` / `notification_settings` untouched, per brief.

## Handoff

Nothing blocking. TASK-643 (backend-developer, Parts A+B) proceeds unchanged; the F1 confirmation
above is the only thing worth carrying forward.
