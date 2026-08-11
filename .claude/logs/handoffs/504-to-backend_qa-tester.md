# Handoff: TASK-504 (qa-tester) → backend-developer / project-architect

Store-migration feature (TASK-501..503) is otherwise solid — full checklist in
`.claude/logs/tasks/504_2026-08-10_store-migration-qa_qa-tester.md`. One real bug found, needs a
backend/architecture decision, not a QA fix.

## Bug: store-scope RLS silently corrupts store-migration results for non-exempt roles

**Severity: high (silent wrong data, not a crash/403) — root cause pre-existing, first exposed as
incorrect business math by this feature.**

### Repro

Tenant `8abfbbb5-3190-4de9-9f91-f4de59101bca` ("Свіжий Кут"), 4 locations. `manager@demo.local`
(store_manager) has `user_locations` grants for only 2 of them (the tenant's original 2 — never
granted the 2 I added for QA testing). Call the same request as two different callers:

```
GET /api/marketing-analytics/store-migration   (period=6m, no store filter)
```

- As `ea@demo.local` (enterprise_admin, RLS-exempt via the `store_scope` policy's bypass list):
  returns 3 flows, `migratedCustomerCount=3`, matches raw SQL ground truth exactly.
- As `manager@demo.local` (store_manager, scoped to 2/4 locations): returns **2 flows**,
  `migratedCustomerCount=2`. The "Троєщина→Подільський" flow (customer `Loyal One`) vanishes
  entirely — not filtered, *reclassified*: that customer's true earliest transaction was at the
  store `manager@demo.local` isn't granted, so their visible earliest transaction shifts to
  Store1, which is also their latest → looks like "not migrated" when they truly migrated. The
  remaining visible flow ("Центральний→Подільський", customer `Champion Two`) has its revenue and
  receipt count silently undercounted: 3004.25/21 receipts vs the true 3124.25/22 — exactly
  missing the one transaction at the location `manager@demo.local` isn't granted. No indication
  anywhere in the response that the data is partial.

After granting `manager@demo.local` `user_locations` rows for the 2 missing locations (SQL only,
no code change), the store_manager's response became byte-identical to enterprise_admin's —
confirms the aggregation logic itself (repository CTEs in
`MarketingAnalyticsRepository.GetStoreMigrationFlowsAsync`/`GetStoreMigrationCustomersAsync`, the
service's `NetFlowByStore` derivation) is correct. The bug is entirely about RLS visibility.

### Root cause

`pos_transactions`' RESTRICTIVE `store_scope` policy (migration
`20260719193545_AddLocationStoreScopeRlsPolicies.cs`, TASK-393 decision) only admits rows whose
`LocationId` is in the caller's `user_locations`, unless the caller's role is
`provider`/`provider_admin`/`worker`/`enterprise_admin`. `network_manager` and `store_manager`
are NOT in that bypass list. The new store-migration repository methods run through the caller's
own RLS session like any other query on this connection, so they inherit this scoping.

This is architecturally different from most other RLS-scoped queries in the app (where "only see
your own store's data" is exactly the intended behavior, e.g. POS reports). Store-migration's
entire premise is comparing a customer's activity **across the whole tenant** — for any caller
who isn't tenant-wide-exempt, the query silently answers a different, narrower question than the
one it's supposed to, without saying so.

- `network_manager` (e.g. `netmgr@demo.local`) currently sees **zero data for the entire
  marketing-analytics module**, not just store-migration — already tracked as **KI-031**
  (`.claude/docs/known-issues.md`), confirmed still reproducing, root cause is 0 `user_locations`
  grants for that demo account. Not new, not this ticket's finding.
- The **store_manager silent-undercounting/reclassification** behavior is a *different, more
  serious* consequence of the same policy, and is **not** covered by KI-031: it happens for any
  *normally provisioned* store_manager (scoped to their real subset of stores — the expected
  shape of this role per KI-031's own description), not just an under-seeded demo account. The
  frontend explicitly treats store_manager as a first-class, fully-trusted user of this exact
  feature (`canExportMarketingAnalyticsPii` = store_manager+ can even export unmasked PII from
  it) — so shipping this as-is means the most commonly deployed privileged role for this feature
  gets confidently wrong analytics with no error and no "partial data" signal.
- Also reproduces on the pre-existing RFM overview endpoint (`GET
  /api/marketing-analytics/overview`, shipped TASK-404..418): store_manager's `periodRevenue` was
  understated by exactly the transactions at the 2 locations they weren't granted. So this is
  debt the whole marketing-analytics module already had; store-migration is just the first place
  where the consequence is an outright wrong classification instead of "just" a smaller total.

### Suggested directions (architecture call, not mine to make)

1. Have the store-migration (and arguably all marketing-analytics) repository queries run under a
   connection/role that bypasses `store_scope` for this specific read path — precedent exists
   (`provider_bypass`/`worker_bypass` policies already carve out exceptions elsewhere) — since
   these are explicitly network-wide aggregate features, not per-store operational views.
2. Or: keep RLS scoping but make it visible — label the response/UI when the caller's own store
   grants don't cover the full tenant, so a store_manager sees "partial view" instead of
   confidently wrong totals.
3. Whatever's chosen, it likely applies to the whole `MarketingAnalyticsController`, not just the
   3 new store-migration actions — worth a project-architect look rather than a narrow patch.

### Dev-only test data touching this

I added 2 locations, 11+3 pos_transactions, 1 customer, and 2 `user_locations` grants (for
`manager@demo.local`, mirroring KI-031's own resolution) to tenant `8abfbbb5-...` to get non-empty
cross-store data for QA — see full list in the task log. Left in place for future QA/demo use of
this tenant, same convention as the existing `RFM-SEED-*` data already there.
